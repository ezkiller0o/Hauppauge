using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.Media
{
    internal sealed class RecorderException : Exception
    {
        public RecorderException(string message) : base(message) { }
    }

    internal sealed class RecorderOptions
    {
        public string FfmpegPath;
        public string Directory;
        public string BaseName = "Recording";
        public bool AppendDateTime = true;
        public int Crf = 18;
        public bool FragmentedMp4;
        public string AspectArg;            // e.g. "4:3" or null
    }

    internal sealed class RecorderResult
    {
        public string Path;
        public bool Success;
        public string Message = "";
        public TimeSpan Duration;
        public long FramesWritten;
        public long FramesRepeated;
        public long FramesDropped;
    }

    /// <summary>
    /// Records the live frames to an MP4 (H.264, NO audio) by feeding raw frames into a separate ffmpeg process.
    ///
    /// Independence from the live view:
    ///  - the recorder is just another consumer of the FrameHub; starting or stopping it never touches the capture graph,
    ///  - frames wait in a bounded queue and are written by their own thread; if the encoder is too slow the OLDEST
    ///    queued frame is dropped, the preview is never blocked,
    ///  - the encoder runs as a separate process at below-normal priority.
    ///
    /// Timing: the writer keeps a constant frame rate by wall-clock position (it repeats the last frame after a stall and
    /// skips a frame that arrives too early), so the video length matches the real recording time.
    /// There is no audio input at all: the ffmpeg command contains "-an" and no audio device.
    /// </summary>
    internal sealed class FfmpegRecorder
    {
        private const int MaxQueue = 60;

        private readonly FrameHub _hub;
        private readonly object _stateGate = new object();
        private readonly StringBuilder _stderr = new StringBuilder();

        private Process _proc;
        private Stream _stdin;
        private Thread _writer;
        private BlockingCollection<VideoFrame> _queue;
        private Action<VideoFrame> _sink;
        private Stopwatch _clock;

        private int _width;
        private int _height;
        private double _fps;
        private int _frameSize;

        private volatile bool _recording;
        private volatile bool _stopping;
        private int _failedRaised;
        private long _written;
        private long _repeated;
        private long _dropped;

        public string OutputPath { get; private set; }
        public bool IsRecording { get { return _recording; } }
        public TimeSpan Elapsed { get { Stopwatch c = _clock; return c == null ? TimeSpan.Zero : c.Elapsed; } }

        /// <summary>Frames that were dropped because ffmpeg could not keep up (the queue overflowed). Normally 0.</summary>
        public long DroppedFrames { get { return Interlocked.Read(ref _dropped); } }

        /// <summary>Raised from a background thread if ffmpeg dies or the pipe breaks while recording.</summary>
        public event Action<string> Failed;

        public FfmpegRecorder(FrameHub hub)
        {
            _hub = hub;
        }

        // ------------------------------------------------------------------ start

        /// <summary>Blocking (takes up to ~1 s). Call it from a background task.</summary>
        public void Start(RecorderOptions o)
        {
            lock (_stateGate)
            {
                if (_recording) throw new RecorderException("A recording is already in progress.");
                if (string.IsNullOrEmpty(o.FfmpegPath) || !File.Exists(o.FfmpegPath))
                    throw new RecorderException("ffmpeg.exe was not found.");

                int w = _hub.Width, h = _hub.Height;
                double fps = _hub.Fps;
                if (w <= 0 || h <= 0) throw new RecorderException("The live view must be running before recording can start.");
                if (fps < 1) fps = 29.97;

                string encoder = FfmpegLocator.DetectH264Encoder(o.FfmpegPath);
                if (encoder == null)
                    throw new RecorderException("This FFmpeg build has no H.264 encoder (libx264 or h264_mf). Use a full 'gpl' or 'essentials' FFmpeg build.");

                // reserve a free file name; the empty placeholder is overwritten by ffmpeg (-y) and removed again on failure
                string name = FileNamer.Sanitize(o.BaseName, "Recording");
                if (o.AppendDateTime) name += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string path;
                FileNamer.CreateUnique(o.Directory, name, ".mp4", out path).Dispose();

                _width = w;
                _height = h;
                _fps = fps;
                _frameSize = w * h * 4;
                _written = _repeated = _dropped = 0;
                _failedRaised = 0;
                _stopping = false;
                _stderr.Length = 0;
                OutputPath = path;

                ProcessStartInfo psi = BuildStartInfo(o, encoder, path);
                Process p;
                try
                {
                    p = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    TryDelete(path);
                    throw new RecorderException("Could not start FFmpeg: " + ex.Message);
                }

                p.EnableRaisingEvents = true;
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { AppendStderr(e.Data); };
                p.OutputDataReceived += delegate { };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                // a wrong argument makes ffmpeg exit immediately -> report it with its own message
                if (p.WaitForExit(700))
                {
                    string tail = StderrTail();
                    TryDelete(path);
                    try { p.Dispose(); } catch { }
                    throw new RecorderException("FFmpeg stopped immediately. " + tail);
                }

                _proc = p;
                _stdin = p.StandardInput.BaseStream;
                p.Exited += OnProcessExited;

                _queue = new BlockingCollection<VideoFrame>();
                _writer = new Thread(WriterLoop);
                _writer.IsBackground = true;
                _writer.Name = "Recorder-Writer";
                _writer.Priority = ThreadPriority.BelowNormal;

                _clock = Stopwatch.StartNew();
                _recording = true;
                _writer.Start();

                _sink = OnFrame;
                _hub.AddSink(_sink);

                Logger.Info("Recording started: " + path + " (" + w + "x" + h + " @ " + fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps, " + encoder + ")");
            }
        }

        private ProcessStartInfo BuildStartInfo(RecorderOptions o, string encoder, string outputPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = o.FfmpegPath;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;

            System.Collections.ObjectModel.Collection<string> a = psi.ArgumentList;
            a.Add("-hide_banner");
            a.Add("-loglevel"); a.Add("warning");
            a.Add("-y");

            // input: raw frames from our process, no audio input exists
            a.Add("-f"); a.Add("rawvideo");
            a.Add("-pix_fmt"); a.Add("bgr0");
            a.Add("-video_size"); a.Add(_width.ToString(CultureInfo.InvariantCulture) + "x" + _height.ToString(CultureInfo.InvariantCulture));
            a.Add("-framerate"); a.Add(FrameRateText(_fps));
            a.Add("-i"); a.Add("pipe:0");

            // output: video only
            a.Add("-an");
            a.Add("-sn");
            a.Add("-dn");

            if (encoder == "libx264")
            {
                a.Add("-c:v"); a.Add("libx264");
                a.Add("-preset"); a.Add("veryfast");
                a.Add("-crf"); a.Add(Math.Max(0, Math.Min(51, o.Crf)).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                a.Add("-c:v"); a.Add("h264_mf");
                a.Add("-b:v"); a.Add("8M");
            }
            a.Add("-pix_fmt"); a.Add("yuv420p");

            if ((_width % 2) != 0 || (_height % 2) != 0)
            {
                a.Add("-vf"); a.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
            }
            if (!string.IsNullOrEmpty(o.AspectArg))
            {
                a.Add("-aspect"); a.Add(o.AspectArg);
            }
            if (o.FragmentedMp4)
            {
                a.Add("-movflags"); a.Add("+frag_keyframe+empty_moov+default_base_moof");
            }
            a.Add(outputPath);
            return psi;
        }

        /// <summary>Exact rational for the NTSC family (29.97 = 30000/1001), plain number otherwise.</summary>
        private static string FrameRateText(double fps)
        {
            double scaled = fps * 1.001;
            if (Math.Abs(scaled - Math.Round(scaled)) < 0.002 && fps > 14)
                return ((int)Math.Round(scaled)).ToString(CultureInfo.InvariantCulture) + "000/1001";
            return fps.ToString("0.######", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ frame flow

        private void OnFrame(VideoFrame f)
        {
            BlockingCollection<VideoFrame> q = _queue;
            if (q == null || _stopping || !_recording) return;

            f.AddRef();
            try
            {
                while (q.Count >= MaxQueue)
                {
                    VideoFrame old;
                    if (!q.TryTake(out old)) break;
                    old.Release();
                    Interlocked.Increment(ref _dropped);
                }
                if (!q.TryAdd(f)) f.Release();
            }
            catch (InvalidOperationException)
            {
                f.Release();        // queue was completed at the same moment
            }
        }

        private void WriterLoop()
        {
            long t0 = 0;
            bool haveT0 = false;
            long written = 0;
            VideoFrame last = null;
            int maxRepeat = (int)(_fps * 5);

            try
            {
                foreach (VideoFrame f in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        if (_failedRaised != 0 || f.Width != _width || f.Height != _height) continue;

                        if (!haveT0) { t0 = f.Ticks; haveT0 = true; }
                        double position = (f.Ticks - t0) / (double)Stopwatch.Frequency * _fps;
                        double delta = position - written;

                        if (delta < -1.1) continue;                 // arrived too early: skip it to keep the frame rate constant

                        if (delta > 1.1 && last != null)            // frames were missed: repeat the last one
                        {
                            int repeat = (int)Math.Round(delta);
                            if (repeat > maxRepeat)
                            {
                                repeat = maxRepeat;
                                written = (long)Math.Round(position) - repeat;
                            }
                            for (int i = 0; i < repeat; i++)
                            {
                                _stdin.Write(last.Data, 0, _frameSize);
                                written++;
                                Interlocked.Increment(ref _repeated);
                            }
                        }

                        _stdin.Write(f.Data, 0, _frameSize);
                        written++;
                        Interlocked.Increment(ref _written);

                        if (last != null) last.Release();
                        f.AddRef();
                        last = f;
                    }
                    catch (Exception ex)
                    {
                        RaiseFailed("The encoder pipe closed unexpectedly (" + ex.Message + "). " + StderrTail());
                    }
                    finally
                    {
                        f.Release();
                    }
                }
            }
            finally
            {
                if (last != null) last.Release();
                try { _stdin.Flush(); } catch { }
                try { _stdin.Close(); } catch { }       // end of input -> ffmpeg finalises the MP4
            }
        }

        // ------------------------------------------------------------------ stop

        /// <summary>Blocking (can take a few seconds while ffmpeg finishes the file). Call it from a background task.</summary>
        public RecorderResult Stop()
        {
            RecorderResult r = new RecorderResult();
            r.Path = OutputPath;

            Process p;
            Thread writer;
            lock (_stateGate)
            {
                if (!_recording && _proc == null)
                {
                    r.Message = "No recording is running.";
                    return r;
                }
                _stopping = true;
                _recording = false;
                p = _proc;
                writer = _writer;
            }

            if (_sink != null) _hub.RemoveSink(_sink);
            _sink = null;

            TimeSpan elapsed = Elapsed;
            if (_clock != null) _clock.Stop();

            try { if (_queue != null) _queue.CompleteAdding(); } catch { }
            if (writer != null && !writer.Join(30000))
                Logger.Warn("The recorder writer thread did not finish in time");

            int exitCode = -1;
            if (p != null)
            {
                try
                {
                    if (!p.WaitForExit(30000))
                    {
                        Logger.Warn("FFmpeg did not finish in time - terminating it");
                        try { p.Kill(); } catch { }
                        p.WaitForExit(5000);
                    }
                    exitCode = p.ExitCode;
                }
                catch (Exception ex)
                {
                    Logger.Warn("Waiting for FFmpeg failed: " + ex.Message);
                }
                try { p.Dispose(); } catch { }
            }

            BlockingCollection<VideoFrame> q = _queue;
            if (q != null)
            {
                VideoFrame leftover;
                while (q.TryTake(out leftover)) leftover.Release();
            }

            lock (_stateGate)
            {
                _proc = null;
                _stdin = null;
                _writer = null;
                _queue = null;
            }

            r.Duration = elapsed;
            r.FramesWritten = Interlocked.Read(ref _written);
            r.FramesRepeated = Interlocked.Read(ref _repeated);
            r.FramesDropped = Interlocked.Read(ref _dropped);

            long size = 0;
            try { if (File.Exists(r.Path)) size = new FileInfo(r.Path).Length; } catch { }

            if (_failedRaised != 0)
            {
                r.Success = false;
                r.Message = "The recording was interrupted. " + StderrTail();
            }
            else if (exitCode != 0 || size == 0)
            {
                r.Success = false;
                r.Message = "FFmpeg reported an error (exit code " + exitCode + "). " + StderrTail();
            }
            else
            {
                r.Success = true;
            }

            if (size == 0) TryDelete(r.Path);
            Logger.Info("Recording stopped: " + r.Path + " ok=" + r.Success + " frames=" + r.FramesWritten
                        + " repeated=" + r.FramesRepeated + " dropped=" + r.FramesDropped + " size=" + size);
            return r;
        }

        // ------------------------------------------------------------------ helpers

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_recording && !_stopping)
                RaiseFailed("FFmpeg stopped unexpectedly. " + StderrTail());
        }

        private void RaiseFailed(string message)
        {
            if (_stopping) return;
            if (Interlocked.Exchange(ref _failedRaised, 1) != 0) return;
            Logger.Error("Recorder failed: " + message);
            Action<string> h = Failed;
            if (h != null) h(message);
        }

        private void AppendStderr(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_stderr)
            {
                if (_stderr.Length > 8000) _stderr.Remove(0, 4000);
                _stderr.AppendLine(line);
            }
        }

        private string StderrTail()
        {
            lock (_stderr)
            {
                string s = _stderr.ToString().Trim();
                if (s.Length > 600) s = s.Substring(s.Length - 600);
                return s;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
