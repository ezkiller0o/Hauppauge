using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.Media
{
    /// <summary>
    /// The single hand-over point between the capture driver and everything that consumes video.
    /// The capture callback copies each frame ONCE into a pooled buffer (flipping bottom-up DIBs to top-down)
    /// and then offers that frame to independent consumers:
    ///   - the live preview (keeps only the newest frame -> lowest latency),
    ///   - snapshots (read the newest frame),
    ///   - the recorder (own bounded queue + own encoder process).
    /// Because every consumer has its own path, starting/stopping the recorder never touches the preview.
    /// </summary>
    internal sealed class FrameHub
    {
        private readonly object _gate = new object();
        private readonly FramePool _pool = new FramePool();
        private VideoFrame _latest;
        private Action<VideoFrame>[] _sinks = new Action<VideoFrame>[0];

        private volatile int _width;
        private volatile int _height;
        private double _fps;
        private long _framesReceived;
        private long _badFrames;
        private long _lastFrameTicks;

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        public double Fps { get { return Volatile.Read(ref _fps); } }
        public long FramesReceived { get { return Interlocked.Read(ref _framesReceived); } }
        public long BadFrames { get { return Interlocked.Read(ref _badFrames); } }
        public long LastFrameTicks { get { return Interlocked.Read(ref _lastFrameTicks); } }

        /// <summary>Called by the capture engine (before streaming starts) with the real connected format.</summary>
        public void Configure(int width, int height, double fps)
        {
            VideoFrame old;
            lock (_gate)
            {
                old = _latest;
                _latest = null;
                _width = width;
                _height = height;
                Volatile.Write(ref _fps, fps);
                _pool.Clear();
            }
            if (old != null) old.Release();
            Interlocked.Exchange(ref _lastFrameTicks, Stopwatch.GetTimestamp());
        }

        /// <summary>Forgets the current frame (live stopped).</summary>
        public void Reset()
        {
            VideoFrame old;
            lock (_gate)
            {
                old = _latest;
                _latest = null;
                _width = 0;
                _height = 0;
            }
            if (old != null) old.Release();
        }

        public void AddSink(Action<VideoFrame> sink)
        {
            lock (_gate)
            {
                Action<VideoFrame>[] next = new Action<VideoFrame>[_sinks.Length + 1];
                Array.Copy(_sinks, next, _sinks.Length);
                next[next.Length - 1] = sink;
                _sinks = next;
            }
        }

        public void RemoveSink(Action<VideoFrame> sink)
        {
            lock (_gate)
            {
                int index = Array.IndexOf(_sinks, sink);
                if (index < 0) return;
                Action<VideoFrame>[] next = new Action<VideoFrame>[_sinks.Length - 1];
                int j = 0;
                for (int i = 0; i < _sinks.Length; i++)
                    if (i != index) next[j++] = _sinks[i];
                _sinks = next;
            }
        }

        /// <summary>
        /// Called on the DirectShow streaming thread for every frame. Must stay fast.
        /// Sinks run synchronously here; a sink that keeps the frame must call frame.AddRef().
        /// </summary>
        public void Publish(IntPtr source, int length, bool bottomUp)
        {
            int w = _width, h = _height;
            int stride = w * 4;
            int need = stride * h;
            if (w <= 0 || h <= 0 || length < need)
            {
                Interlocked.Increment(ref _badFrames);
                return;
            }

            VideoFrame f = _pool.Rent(need);
            f.Width = w;
            f.Height = h;
            f.Stride = stride;

            if (bottomUp)
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(IntPtr.Add(source, (h - 1 - y) * stride), f.Data, y * stride, stride);
            }
            else
            {
                Marshal.Copy(source, f.Data, 0, need);
            }

            long now = Stopwatch.GetTimestamp();
            f.Ticks = now;
            f.Sequence = Interlocked.Increment(ref _framesReceived);

            VideoFrame old;
            Action<VideoFrame>[] sinks;
            lock (_gate)
            {
                old = _latest;
                _latest = f;
                sinks = _sinks;
            }
            if (old != null) old.Release();
            Interlocked.Exchange(ref _lastFrameTicks, now);

            for (int i = 0; i < sinks.Length; i++)
            {
                try { sinks[i](f); }
                catch (Exception ex) { Logger.Error("Frame sink failed", ex); }
            }
        }

        /// <summary>Returns the newest frame with an extra reference (caller must Release it), or null.</summary>
        public VideoFrame AcquireLatest()
        {
            lock (_gate)
            {
                if (_latest == null) return null;
                _latest.AddRef();
                return _latest;
            }
        }
    }
}
