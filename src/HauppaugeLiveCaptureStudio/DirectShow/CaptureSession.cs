using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    /// <summary>Receives every frame from the Sample Grabber on the DirectShow streaming thread.</summary>
    internal sealed class GrabberCallback : ISampleGrabberCB
    {
        private readonly Action<IntPtr, int> _onBuffer;

        public GrabberCallback(Action<IntPtr, int> onBuffer)
        {
            _onBuffer = onBuffer;
        }

        public int SampleCB(double sampleTime, IntPtr pSample)
        {
            return 0;       // not used (we ask for BufferCB)
        }

        public int BufferCB(double sampleTime, IntPtr pBuffer, int bufferLen)
        {
            try { if (_onBuffer != null) _onBuffer(pBuffer, bufferLen); }
            catch (Exception ex) { Logger.Error("BufferCB failed", ex); }
            return 0;
        }
    }

    internal sealed class OpenOptions
    {
        public int Width;                   // 0 = driver default
        public int Height;
        public double Fps;                  // 0 = driver default
        public string InputKey = "";        // "" = leave the driver's input alone
        public bool Streaming = true;       // false = only read formats/inputs (probe)
        public bool LowLatencyBuffers = true;
        public bool NoClock = true;         // run the graph without a reference clock (frames are delivered immediately)
        public bool UsePreviewPin = false;
        public Action<IntPtr, int> OnBuffer;
    }

    /// <summary>
    /// One DirectShow graph:  [crossbar] -> capture filter -> (colour converter) -> Sample Grabber (RGB32) -> Null Renderer.
    /// No video window and no renderer clock are involved, so frames go straight from the driver to our code.
    /// All members must be used from the MTA worker thread.
    /// </summary>
    internal sealed class CaptureSession : IDisposable
    {
        public readonly DeviceCaps Caps = new DeviceCaps();
        public string DeviceName = "";
        public int Width;
        public int Height;
        public double Fps;
        public bool BottomUp;
        public string SourceFormat = "";

        private IGraphBuilder _graph;
        private ICaptureGraphBuilder2 _builder;
        private IBaseFilter _capFilter;
        private IBaseFilter _grabberFilter;
        private IBaseFilter _nullFilter;
        private ISampleGrabber _grabber;
        private IMediaControl _control;
        private IMediaEvent _events;
        private IAMStreamConfig _streamConfig;
        private IAMBufferNegotiation _negotiation;
        private GrabberCallback _callback;
        private readonly List<IAMCrossbar> _crossbars = new List<IAMCrossbar>();
        private bool _disposed;

        public static CaptureSession Open(CaptureDevice device, OpenOptions options)
        {
            CaptureSession s = new CaptureSession();
            s.DeviceName = device.Name;
            try
            {
                s.Build(device, options);
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------------ building the graph

        private void Build(CaptureDevice device, OpenOptions o)
        {
            int hr;
            _graph = DsUtil.Create<IGraphBuilder>(DsGuids.CLSID_FilterGraph);
            _builder = DsUtil.Create<ICaptureGraphBuilder2>(DsGuids.CLSID_CaptureGraphBuilder2);
            Check(_builder.SetFiltergraph(_graph), "SetFiltergraph");

            _capFilter = DsDeviceEnum.BindFilter(device.Id);
            Check(_graph.AddFilter(_capFilter, "Video Capture"), "Adding the capture device to the graph");

            // 1) what can the device do, and which of it do we want?
            _streamConfig = FindStreamConfig();
            if (_streamConfig != null)
            {
                Caps.Formats = EnumerateFormats(_streamConfig);
                Logger.Info("Device offers " + Caps.Formats.Count + " format entries");
                foreach (VideoFormat f in Caps.Formats)
                    Logger.Info("  cap#" + f.CapIndex + " " + f.ResolutionText + " " + f.SubTypeName
                                + " fps " + VideoFormat.FormatFps(f.MinFps) + ".." + VideoFormat.FormatFps(f.MaxFps)
                                + " (default " + VideoFormat.FormatFps(f.DefaultFps) + ")");
                ApplyFormat(o);
            }
            else
            {
                Logger.Warn("IAMStreamConfig was not found: the driver's default format will be used");
            }

            // 2) ask the driver for as few buffers as possible (less queued video = less latency)
            if (o.LowLatencyBuffers) SuggestSmallBuffers();

            // 3) sample grabber + null renderer
            if (o.Streaming)
            {
                _grabberFilter = DsUtil.Create<IBaseFilter>(DsGuids.CLSID_SampleGrabber);
                _grabber = (ISampleGrabber)_grabberFilter;

                IntPtr pmt = DsUtil.AllocMediaType();
                try
                {
                    AMMediaType want = new AMMediaType();
                    want.MajorType = DsGuids.MEDIATYPE_Video;
                    want.SubType = DsGuids.MEDIASUBTYPE_RGB32;
                    Marshal.StructureToPtr(want, pmt, false);
                    hr = _grabber.SetMediaType(pmt);
                }
                finally { DsUtil.FreeMediaType(pmt); }
                Check(hr, "Sample Grabber SetMediaType(RGB32)");

                _grabber.SetBufferSamples(false);
                _grabber.SetOneShot(false);
                _callback = new GrabberCallback(o.OnBuffer);
                Check(_grabber.SetCallback(_callback, 1), "Sample Grabber SetCallback");
                Check(_graph.AddFilter(_grabberFilter, "Sample Grabber"), "Adding the Sample Grabber");
            }

            _nullFilter = DsUtil.Create<IBaseFilter>(DsGuids.CLSID_NullRenderer);
            Check(_graph.AddFilter(_nullFilter, "Null Renderer"), "Adding the Null Renderer");

            // 4) connect everything (this also inserts the WDM crossbar and the colour converter when needed)
            IntPtr pCat = DsUtil.AllocGuid(o.UsePreviewPin ? DsGuids.PIN_CATEGORY_PREVIEW : DsGuids.PIN_CATEGORY_CAPTURE);
            IntPtr pType = DsUtil.AllocGuid(DsGuids.MEDIATYPE_Video);
            try
            {
                hr = _builder.RenderStream(pCat, pType, _capFilter, o.Streaming ? _grabberFilter : null, _nullFilter);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pCat);
                Marshal.FreeCoTaskMem(pType);
            }
            Check(hr, "Connecting the capture pin (RenderStream)");

            if (o.Streaming) ReadConnectedFormat(o);
            DescribeSourceFormat();

            // 5) analog inputs (Composite / S-Video ...)
            DiscoverCrossbars();
            if (!string.IsNullOrEmpty(o.InputKey)) SelectInput(o.InputKey);
            RefreshActiveInput();

            // 6) run control
            _control = (IMediaControl)_graph;
            _events = (IMediaEvent)_graph;

            if (o.Streaming && o.NoClock)
            {
                IMediaFilter mf = (IMediaFilter)_graph;
                hr = mf.SetSyncSource(null);
                Logger.Info("SetSyncSource(null) -> " + DsUtil.Hex(hr));
            }

            Logger.Info("Graph built: " + DescribeGraph());
        }

        private void Check(int hr, string what)
        {
            if (hr < 0)
                throw new CaptureException(what + " failed: " + DsUtil.DescribeHr(hr), hr);
        }

        private IAMStreamConfig FindStreamConfig()
        {
            Guid iid = DsGuids.IID_IAMStreamConfig;
            Guid[] categories = { DsGuids.PIN_CATEGORY_CAPTURE, DsGuids.PIN_CATEGORY_PREVIEW };
            foreach (Guid category in categories)
            {
                IntPtr pCat = DsUtil.AllocGuid(category);
                IntPtr pType = DsUtil.AllocGuid(DsGuids.MEDIATYPE_Video);
                try
                {
                    object o;
                    int hr = _builder.FindInterface(pCat, pType, _capFilter, ref iid, out o);
                    IAMStreamConfig cfg = hr == 0 ? o as IAMStreamConfig : null;
                    if (cfg != null) return cfg;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pCat);
                    Marshal.FreeCoTaskMem(pType);
                }
            }
            return null;
        }

        private void SuggestSmallBuffers()
        {
            IntPtr pCat = DsUtil.AllocGuid(DsGuids.PIN_CATEGORY_CAPTURE);
            IntPtr pType = DsUtil.AllocGuid(DsGuids.MEDIATYPE_Video);
            try
            {
                Guid iid = DsGuids.IID_IAMBufferNegotiation;
                object o;
                int hr = _builder.FindInterface(pCat, pType, _capFilter, ref iid, out o);
                IAMBufferNegotiation neg = hr == 0 ? o as IAMBufferNegotiation : null;
                if (neg == null)
                {
                    Logger.Info("IAMBufferNegotiation is not available on this device");
                    return;
                }
                _negotiation = neg;

                AllocatorProperties ap = new AllocatorProperties();
                ap.cBuffers = 2;
                ap.cbBuffer = -1;
                ap.cbAlign = -1;
                ap.cbPrefix = -1;
                hr = neg.SuggestAllocatorProperties(ref ap);
                Logger.Info("SuggestAllocatorProperties(2 buffers) -> " + DsUtil.Hex(hr));
            }
            catch (Exception ex)
            {
                Logger.Warn("Buffer negotiation skipped: " + ex.Message);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pCat);
                Marshal.FreeCoTaskMem(pType);
            }
        }

        // ------------------------------------------------------------------ formats

        private static List<VideoFormat> EnumerateFormats(IAMStreamConfig cfg)
        {
            List<VideoFormat> list = new List<VideoFormat>();
            int count, size;
            if (cfg.GetNumberOfCapabilities(out count, out size) < 0 || count <= 0) return list;

            IntPtr scc = Marshal.AllocCoTaskMem(Math.Max(size, 256));
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr pmt;
                    if (cfg.GetStreamCaps(i, out pmt, scc) != 0 || pmt == IntPtr.Zero) continue;
                    try
                    {
                        AMMediaType mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                        if (mt.MajorType != DsGuids.MEDIATYPE_Video || mt.FormatPtr == IntPtr.Zero) continue;

                        int bmi = BitmapHeaderOffset(mt);
                        if (bmi < 0) continue;

                        VideoFormat f = new VideoFormat();
                        f.CapIndex = i;
                        f.Width = Marshal.ReadInt32(mt.FormatPtr, bmi + 4);
                        f.Height = Math.Abs(Marshal.ReadInt32(mt.FormatPtr, bmi + 8));
                        f.SubType = mt.SubType;
                        f.SubTypeName = DsUtil.SubtypeName(mt.SubType);
                        f.DefaultInterval = Marshal.ReadInt64(mt.FormatPtr, 40);
                        if (size >= 120)
                        {
                            // VIDEO_STREAM_CONFIG_CAPS: MinFrameInterval @104, MaxFrameInterval @112
                            f.MinInterval = Marshal.ReadInt64(scc, 104);
                            f.MaxInterval = Marshal.ReadInt64(scc, 112);
                        }
                        if (f.MinInterval <= 0) f.MinInterval = f.DefaultInterval;
                        if (f.MaxInterval <= 0) f.MaxInterval = f.DefaultInterval;
                        if (f.Width <= 0 || f.Height <= 0) continue;
                        list.Add(f);
                    }
                    finally
                    {
                        DsUtil.FreeMediaType(pmt);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(scc);
            }
            return list;
        }

        /// <summary>Offset of the BITMAPINFOHEADER inside the format block, or -1 if the block is not a video format.</summary>
        private static int BitmapHeaderOffset(AMMediaType mt)
        {
            if (mt.FormatPtr == IntPtr.Zero) return -1;
            if (mt.FormatType == DsGuids.FORMAT_VideoInfo && mt.FormatSize >= 88) return 48;
            if (mt.FormatType == DsGuids.FORMAT_VideoInfo2 && mt.FormatSize >= 112) return 72;
            return -1;
        }

        private void ApplyFormat(OpenOptions o)
        {
            if (o.Width <= 0 || o.Height <= 0 || Caps.Formats.Count == 0) return;

            VideoFormat best = null;
            foreach (VideoFormat f in Caps.Formats)
            {
                if (f.Width != o.Width || f.Height != o.Height) continue;
                if (best == null || DsUtil.SubtypeRank(f.SubTypeName) < DsUtil.SubtypeRank(best.SubTypeName)) best = f;
            }
            if (best == null)
            {
                Logger.Warn("Requested " + o.Width + "x" + o.Height + " is not offered by the device; using the driver default");
                return;
            }

            int count, size;
            _streamConfig.GetNumberOfCapabilities(out count, out size);
            IntPtr scc = Marshal.AllocCoTaskMem(Math.Max(size, 256));
            IntPtr pmt = IntPtr.Zero;
            try
            {
                int hr = _streamConfig.GetStreamCaps(best.CapIndex, out pmt, scc);
                if (hr < 0 || pmt == IntPtr.Zero)
                {
                    Logger.Warn("GetStreamCaps(" + best.CapIndex + ") failed: " + DsUtil.Hex(hr));
                    return;
                }

                AMMediaType mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                if (o.Fps > 0 && BitmapHeaderOffset(mt) >= 0)
                {
                    long interval = (long)Math.Round(1e7 / o.Fps);
                    if (best.MinInterval > 0 && interval < best.MinInterval) interval = best.MinInterval;
                    if (best.MaxInterval > 0 && interval > best.MaxInterval) interval = best.MaxInterval;
                    Marshal.WriteInt64(mt.FormatPtr, 40, interval);     // AvgTimePerFrame
                }

                hr = _streamConfig.SetFormat(pmt);
                Logger.Info("SetFormat " + best.ResolutionText + " " + best.SubTypeName + " @ "
                            + VideoFormat.FormatFps(o.Fps) + " fps -> " + DsUtil.Hex(hr));
            }
            finally
            {
                DsUtil.FreeMediaType(pmt);
                Marshal.FreeCoTaskMem(scc);
            }
        }

        private void ReadConnectedFormat(OpenOptions o)
        {
            IntPtr pmt = DsUtil.AllocMediaType();
            try
            {
                int hr = _grabber.GetConnectedMediaType(pmt);
                Check(hr, "Reading the connected video format");

                AMMediaType mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                int bmi = BitmapHeaderOffset(mt);
                if (bmi < 0) throw new CaptureException("The device delivered an unsupported video format block.");

                Width = Marshal.ReadInt32(mt.FormatPtr, bmi + 4);
                int h = Marshal.ReadInt32(mt.FormatPtr, bmi + 8);
                BottomUp = h > 0;
                Height = Math.Abs(h);
                int bpp = Marshal.ReadInt16(mt.FormatPtr, bmi + 14);
                if (bpp != 32) throw new CaptureException("Unexpected pixel depth after conversion: " + bpp + " bit.");

                long avg = Marshal.ReadInt64(mt.FormatPtr, 40);
                if (avg > 0) Fps = 1e7 / avg;
                else if (o.Fps > 0) Fps = o.Fps;
                else Fps = Height >= 576 ? 25.0 : 29.97;

                Logger.Info("Connected video: " + Width + "x" + Height + " RGB32 " + (BottomUp ? "bottom-up" : "top-down")
                            + " @ " + Fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " fps");
            }
            finally
            {
                DsUtil.FreeMediaType(pmt);
            }
        }

        private void DescribeSourceFormat()
        {
            if (_streamConfig == null) return;
            IntPtr pmt = IntPtr.Zero;
            try
            {
                if (_streamConfig.GetFormat(out pmt) == 0 && pmt != IntPtr.Zero)
                {
                    AMMediaType mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                    SourceFormat = DsUtil.SubtypeName(mt.SubType);
                    Logger.Info("Device output format: " + SourceFormat);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("GetFormat failed: " + ex.Message);
            }
            finally
            {
                DsUtil.FreeMediaType(pmt);
            }
        }

        // ------------------------------------------------------------------ analog inputs (crossbar)

        private void DiscoverCrossbars()
        {
            IEnumFilters en;
            if (_graph.EnumFilters(out en) != 0 || en == null) return;

            try
            {
                IBaseFilter[] one = new IBaseFilter[1];
                while (en.Next(1, one, IntPtr.Zero) == 0)
                {
                    IBaseFilter f = one[0];
                    one[0] = null;
                    IAMCrossbar xb = f as IAMCrossbar;
                    if (xb != null) _crossbars.Add(xb);
                    else DsUtil.ReleaseOne(f);
                }
            }
            finally
            {
                DsUtil.ReleaseOne(en);
            }

            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int c = 0; c < _crossbars.Count; c++)
            {
                int outCount, inCount;
                if (_crossbars[c].get_PinCounts(out outCount, out inCount) != 0) continue;

                for (int i = 0; i < inCount; i++)
                {
                    int related, type;
                    if (_crossbars[c].get_CrossbarPinInfo(true, i, out related, out type) != 0) continue;
                    if (type >= PhysConn.AudioFirst) continue;          // we never use audio

                    VideoInput vi = new VideoInput();
                    vi.CrossbarIndex = c;
                    vi.PinIndex = i;
                    vi.PhysicalType = type;
                    NameInput(vi, counts);
                    Caps.Inputs.Add(vi);
                    Logger.Info("Crossbar #" + c + " input pin " + i + ": " + vi.DisplayName + " (type " + type + ")");
                }
            }

            if (Caps.Inputs.Count == 0)
                Caps.Note = "This device does not expose selectable video inputs.";
        }

        private static void NameInput(VideoInput vi, Dictionary<string, int> counts)
        {
            string key, name;
            switch (vi.PhysicalType)
            {
                case PhysConn.VideoComposite: key = "Composite"; name = "Composite"; break;
                case PhysConn.VideoSVideo: key = "SVideo"; name = "S-Video"; break;
                case PhysConn.VideoTuner: key = "Tuner"; name = "TV Tuner"; break;
                case PhysConn.VideoRGB: key = "RGB"; name = "RGB"; break;
                case PhysConn.VideoYRYBY: key = "Component"; name = "Component"; break;
                case PhysConn.VideoAUX: key = "Aux"; name = "Aux"; break;
                case PhysConn.VideoSCART: key = "Scart"; name = "SCART"; break;
                default: key = "Input" + vi.PhysicalType; name = "Input " + vi.PhysicalType; break;
            }
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
            vi.Key = n == 0 ? key : key + (n + 1);
            vi.DisplayName = n == 0 ? name : name + " " + (n + 1);
        }

        /// <summary>Routes the chosen input to every video output of its crossbar. Works while streaming.</summary>
        public bool SelectInput(string key)
        {
            VideoInput target = null;
            foreach (VideoInput vi in Caps.Inputs)
                if (string.Equals(vi.Key, key, StringComparison.OrdinalIgnoreCase)) { target = vi; break; }
            if (target == null) return false;

            IAMCrossbar xb = _crossbars[target.CrossbarIndex];
            int outCount, inCount;
            if (xb.get_PinCounts(out outCount, out inCount) != 0) return false;

            bool routed = false;
            for (int o = 0; o < outCount; o++)
            {
                int related, type;
                if (xb.get_CrossbarPinInfo(false, o, out related, out type) != 0) continue;
                if (type >= PhysConn.AudioFirst) continue;
                if (xb.CanRoute(o, target.PinIndex) != 0) continue;

                int hr = xb.Route(o, target.PinIndex);
                Logger.Info("Route(out " + o + " <- in " + target.PinIndex + " " + target.DisplayName + ") -> " + DsUtil.Hex(hr));
                if (hr >= 0) routed = true;
            }
            if (routed) Caps.ActiveInputKey = target.Key;
            return routed;
        }

        private void RefreshActiveInput()
        {
            foreach (VideoInput vi in Caps.Inputs)
            {
                IAMCrossbar xb = _crossbars[vi.CrossbarIndex];
                int outCount, inCount;
                if (xb.get_PinCounts(out outCount, out inCount) != 0) continue;
                for (int o = 0; o < outCount; o++)
                {
                    int related, type;
                    if (xb.get_CrossbarPinInfo(false, o, out related, out type) != 0 || type >= PhysConn.AudioFirst) continue;
                    int routedIn;
                    if (xb.get_IsRoutedTo(o, out routedIn) == 0 && routedIn == vi.PinIndex)
                    {
                        Caps.ActiveInputKey = vi.Key;
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ run control

        public void Run()
        {
            int hr = _control.Run();
            if (hr < 0) throw new CaptureException("Could not start streaming: " + DsUtil.DescribeHr(hr), hr);
            Logger.Info("Graph running (Run -> " + DsUtil.Hex(hr) + ")");
        }

        public void Stop()
        {
            if (_control != null) _control.Stop();
        }

        /// <summary>Reads pending graph events. Returns a message when the device was lost or the graph aborted.</summary>
        public string PollEvents()
        {
            if (_events == null || _disposed) return null;
            string lost = null;
            for (int i = 0; i < 32; i++)
            {
                int code;
                IntPtr p1, p2;
                if (_events.GetEvent(out code, out p1, out p2, 0) != 0) break;      // E_ABORT = nothing pending
                try
                {
                    if (code == EventCodes.EC_DEVICE_LOST)
                    {
                        if (p2 == IntPtr.Zero) lost = "The USB capture device was disconnected.";
                    }
                    else if (code == EventCodes.EC_ERRORABORT || code == EventCodes.EC_ERRORABORTEX)
                    {
                        lost = "The capture graph was aborted by the driver (" + DsUtil.Hex(unchecked((int)p1.ToInt64())) + ").";
                    }
                    else if (code == EventCodes.EC_STREAM_ERROR_STOPPED)
                    {
                        lost = "The video stream stopped with an error (" + DsUtil.Hex(unchecked((int)p1.ToInt64())) + ").";
                    }
                    else
                    {
                        Logger.Info("Graph event 0x" + code.ToString("X2"));
                    }
                }
                finally
                {
                    _events.FreeEventParams(code, p1, p2);
                }
            }
            return lost;
        }

        /// <summary>Names of the filters that ended up in the graph (for the log and the diagnostics text).</summary>
        public string DescribeGraph()
        {
            StringBuilder sb = new StringBuilder();
            IEnumFilters en;
            if (_graph == null || _graph.EnumFilters(out en) != 0 || en == null) return "(no graph)";

            IntPtr info = Marshal.AllocCoTaskMem(512);
            try
            {
                IBaseFilter[] one = new IBaseFilter[1];
                while (en.Next(1, one, IntPtr.Zero) == 0)
                {
                    IBaseFilter f = one[0];
                    one[0] = null;
                    try
                    {
                        for (int i = 0; i < 512; i += 8) Marshal.WriteInt64(info, i, 0);
                        if (f.QueryFilterInfo(info) == 0)
                        {
                            // FILTER_INFO { WCHAR achName[128]; IFilterGraph* pGraph; }
                            string name = Marshal.PtrToStringUni(info);
                            IntPtr pGraph = Marshal.ReadIntPtr(info, 256);
                            if (pGraph != IntPtr.Zero) Marshal.Release(pGraph);
                            if (sb.Length > 0) sb.Append(" -> ");
                            sb.Append(name);
                        }
                    }
                    finally
                    {
                        DsUtil.ReleaseOne(f);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(info);
                DsUtil.ReleaseOne(en);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ teardown

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { if (_control != null) _control.Stop(); } catch { }
            try { if (_grabber != null) _grabber.SetCallback(null, 1); } catch { }

            // remove every filter so that the capture driver is released immediately
            try
            {
                IEnumFilters en;
                if (_graph != null && _graph.EnumFilters(out en) == 0 && en != null)
                {
                    List<IBaseFilter> all = new List<IBaseFilter>();
                    IBaseFilter[] one = new IBaseFilter[1];
                    while (en.Next(1, one, IntPtr.Zero) == 0)
                    {
                        all.Add(one[0]);
                        one[0] = null;
                    }
                    DsUtil.ReleaseOne(en);
                    foreach (IBaseFilter f in all)
                    {
                        try { _graph.RemoveFilter(f); } catch { }
                        DsUtil.ReleaseOne(f);
                    }
                }
            }
            catch { }

            foreach (IAMCrossbar xb in _crossbars) DsUtil.Release(xb);
            _crossbars.Clear();

            DsUtil.Release(_negotiation);
            DsUtil.Release(_streamConfig);
            DsUtil.Release(_grabber);
            DsUtil.Release(_grabberFilter);
            DsUtil.Release(_nullFilter);
            DsUtil.Release(_capFilter);
            DsUtil.Release(_events);
            DsUtil.Release(_control);
            DsUtil.Release(_builder);
            DsUtil.Release(_graph);

            _negotiation = null;
            _streamConfig = null;
            _grabber = null;
            _grabberFilter = null;
            _nullFilter = null;
            _capFilter = null;
            _events = null;
            _control = null;
            _builder = null;
            _graph = null;
            _callback = null;

            // make sure no RCW keeps the USB device busy
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
