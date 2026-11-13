using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HauppaugeLiveCaptureStudio.Core;
using HauppaugeLiveCaptureStudio.Media;

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    /// <summary>
    /// Public face of the capture layer. Every DirectShow call runs on a private MTA thread; the UI only awaits tasks.
    /// While live, a small watchdog watches graph events (EC_DEVICE_LOST ...) and the frame flow so that a USB
    /// disconnect is reported instead of leaving a frozen picture.
    /// </summary>
    internal sealed class CaptureEngine : IDisposable
    {
        private volatile MtaWorker _worker = new MtaWorker("DirectShow-MTA");
        private readonly FrameHub _hub;
        private CaptureSession _session;            // touched only on the worker thread
        private Timer _monitor;
        private volatile bool _running;
        private string _deviceId = "";
        private int _lostRaised;
        private int _monitorBusy;
        private int _pollTimeouts;
        private int _stallTicks;
        private volatile bool _stalled;

        /// <summary>Raised (on a thread-pool thread) when the device disappeared or the driver aborted the stream.</summary>
        public event Action<string> DeviceLost;

        /// <summary>Raised (thread-pool thread) when frames stop arriving (true) or resume (false).</summary>
        public event Action<bool> StallChanged;

        public CaptureEngine(FrameHub hub)
        {
            _hub = hub;
        }

        public FrameHub Hub { get { return _hub; } }
        public bool IsRunning { get { return _running; } }

        // ------------------------------------------------------------------ queries

        public Task<List<CaptureDevice>> EnumerateDevicesAsync()
        {
            return _worker.Run(() => DsDeviceEnum.Enumerate());
        }

        /// <summary>Reads the formats and analog inputs of a device without starting it.</summary>
        public Task<DeviceCaps> ProbeAsync(CaptureDevice device)
        {
            return _worker.Run(() =>
            {
                OpenOptions o = new OpenOptions();
                o.Streaming = false;
                o.LowLatencyBuffers = false;
                o.NoClock = false;
                using (CaptureSession s = CaptureSession.Open(device, o))
                {
                    return s.Caps;
                }
            });
        }

        public Task<string> DescribeGraphAsync()
        {
            return _worker.Run(() =>
            {
                CaptureSession s = _session;
                return s == null ? "" : s.DescribeGraph();
            });
        }

        // ------------------------------------------------------------------ start / stop

        public Task<StartInfo> StartAsync(CaptureDevice device, int width, int height, double fps, string inputKey)
        {
            return _worker.Run(() => StartCore(device, width, height, fps, inputKey));
        }

        private StartInfo StartCore(CaptureDevice device, int width, int height, double fps, string inputKey)
        {
            StopCore();

            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                OpenOptions o = new OpenOptions();
                o.Width = width;
                o.Height = height;
                o.Fps = fps;
                o.InputKey = inputKey ?? "";
                o.Streaming = true;
                o.LowLatencyBuffers = attempt == 0;     // attempt 1: smallest buffers, no clock
                o.NoClock = attempt == 0;               // attempt 2: driver defaults
                o.UsePreviewPin = attempt == 2;         // attempt 3: use the preview pin instead of the capture pin

                CaptureSession s = null;
                o.OnBuffer = delegate (IntPtr ptr, int len)
                {
                    CaptureSession cs = s;
                    if (cs != null) _hub.Publish(ptr, len, cs.BottomUp);
                };

                try
                {
                    Logger.Info("Start attempt " + (attempt + 1) + " for '" + device.Name + "'");
                    s = CaptureSession.Open(device, o);
                    _hub.Configure(s.Width, s.Height, s.Fps);
                    s.Run();

                    _session = s;
                    _deviceId = device.Id;
                    _lostRaised = 0;
                    _pollTimeouts = 0;
                    _stallTicks = 0;
                    _stalled = false;
                    _running = true;
                    StartMonitor();

                    StartInfo info = new StartInfo();
                    info.DeviceName = device.Name;
                    info.Width = s.Width;
                    info.Height = s.Height;
                    info.Fps = s.Fps;
                    info.SourceFormat = s.SourceFormat;
                    info.InputKey = s.Caps.ActiveInputKey;
                    info.Caps = s.Caps;
                    return info;
                }
                catch (Exception ex)
                {
                    Logger.Warn("Start attempt " + (attempt + 1) + " failed: " + ex.Message);
                    last = ex;
                    if (s != null)
                    {
                        try { s.Dispose(); } catch { }
                    }
                }
            }

            CaptureException ce = last as CaptureException;
            if (ce != null) throw ce;
            COMException com = last as COMException;
            if (com != null)
                throw new CaptureException("Could not start the capture device: " + DsUtil.DescribeHr(com.HResult), com.HResult, com);
            throw new CaptureException("Could not start the capture device: " + (last == null ? "unknown error" : last.Message), 0, last);
        }

        public async Task StopAsync()
        {
            _running = false;
            StopMonitor();

            MtaWorker worker = _worker;
            Task job = worker.Run(() => StopCore());
            Task finished = await Task.WhenAny(job, Task.Delay(8000));
            if (finished != job)
            {
                Logger.Error("Stopping the capture graph timed out - abandoning the DirectShow worker thread");
                _worker = new MtaWorker("DirectShow-MTA");
                worker.Dispose();
            }
            _hub.Reset();
        }

        private void StopCore()
        {
            CaptureSession s = _session;
            _session = null;
            if (s != null)
            {
                try { s.Dispose(); }
                catch (Exception ex) { Logger.Warn("Error while stopping the capture graph: " + ex.Message); }
            }
        }

        public Task<bool> SelectInputAsync(string inputKey)
        {
            return _worker.Run(() =>
            {
                CaptureSession s = _session;
                return s != null && s.SelectInput(inputKey);
            });
        }

        // ------------------------------------------------------------------ watchdog

        private void StartMonitor()
        {
            StopMonitor();
            _monitor = new Timer(OnMonitorTick, null, 500, 500);
        }

        private void StopMonitor()
        {
            Timer m = _monitor;
            _monitor = null;
            if (m != null) m.Dispose();
        }

        private void OnMonitorTick(object state)
        {
            if (!_running) return;
            if (Interlocked.CompareExchange(ref _monitorBusy, 1, 0) != 0) return;

            try
            {
                string reason = null;
                Task<string> poll = _worker.Run(() =>
                {
                    CaptureSession s = _session;
                    return s == null ? null : s.PollEvents();
                });

                if (poll.Wait(6000))
                {
                    _pollTimeouts = 0;
                    reason = poll.Result;
                }
                else if (++_pollTimeouts >= 2)
                {
                    reason = "The capture driver stopped responding (the USB device may have been unplugged).";
                }

                if (reason == null) CheckFrameFlow();
                if (reason != null) RaiseLost(reason);
            }
            catch (Exception ex)
            {
                Logger.Warn("Capture monitor: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _monitorBusy, 0);
            }
        }

        private void CheckFrameFlow()
        {
            double seconds = (Stopwatch.GetTimestamp() - _hub.LastFrameTicks) / (double)Stopwatch.Frequency;

            if (seconds > 3.0)
            {
                bool first = !_stalled;
                _stalled = true;
                if (first || ++_stallTicks % 10 == 0)
                {
                    if (!IsDeviceStillPresent())
                    {
                        RaiseLost("The USB capture device is no longer detected.");
                        return;
                    }
                }
                if (first)
                {
                    Logger.Warn("No video frames for 3 seconds");
                    Action<bool> h = StallChanged;
                    if (h != null) h(true);
                }
            }
            else if (_stalled)
            {
                _stalled = false;
                _stallTicks = 0;
                Logger.Info("Video frames resumed");
                Action<bool> h = StallChanged;
                if (h != null) h(false);
            }
        }

        private bool IsDeviceStillPresent()
        {
            try
            {
                Task<bool> t = _worker.Run(() =>
                {
                    foreach (CaptureDevice d in DsDeviceEnum.Enumerate())
                        if (string.Equals(d.Id, _deviceId, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                });
                if (!t.Wait(6000)) return false;
                return t.Result;
            }
            catch
            {
                return true;        // could not check: do not claim the device is gone
            }
        }

        private void RaiseLost(string reason)
        {
            if (Interlocked.Exchange(ref _lostRaised, 1) != 0) return;
            _running = false;
            Logger.Error("Device lost: " + reason);
            Action<string> h = DeviceLost;
            if (h != null) h(reason);
        }

        public void Dispose()
        {
            _running = false;
            StopMonitor();
            try
            {
                MtaWorker worker = _worker;
                Task job = worker.Run(() => StopCore());
                job.Wait(4000);
                worker.Dispose();
            }
            catch { }
        }
    }
}
