using System.Collections.Concurrent;
using System.Threading;

namespace HauppaugeLiveCaptureStudio.Media
{
    /// <summary>
    /// One decoded video frame: top-down BGRX (32 bit) pixels, Stride = Width * 4.
    /// Frames are reference counted and recycled through a pool so that the live path
    /// does not allocate (no garbage-collector pauses = steadier latency).
    /// Rule: whoever keeps a frame beyond the callback that delivered it must call AddRef(), and Release() when done.
    /// </summary>
    internal sealed class VideoFrame
    {
        private readonly FramePool _pool;
        private int _refs;

        public readonly byte[] Data;
        public int Width;
        public int Height;
        public int Stride;
        public long Ticks;          // Stopwatch timestamp taken when the frame arrived from the driver
        public long Sequence;

        internal VideoFrame(FramePool pool, int size)
        {
            _pool = pool;
            Data = new byte[size];
            _refs = 1;
        }

        internal void Revive()
        {
            Interlocked.Exchange(ref _refs, 1);
        }

        public void AddRef()
        {
            Interlocked.Increment(ref _refs);
        }

        public void Release()
        {
            int n = Interlocked.Decrement(ref _refs);
            if (n == 0) _pool.Return(this);
        }
    }

    internal sealed class FramePool
    {
        private readonly ConcurrentStack<VideoFrame> _free = new ConcurrentStack<VideoFrame>();
        private const int MaxRetained = 32;

        public VideoFrame Rent(int size)
        {
            VideoFrame f;
            while (_free.TryPop(out f))
            {
                if (f.Data.Length == size)
                {
                    f.Revive();
                    return f;
                }
            }
            return new VideoFrame(this, size);
        }

        internal void Return(VideoFrame f)
        {
            if (_free.Count < MaxRetained) _free.Push(f);
        }

        public void Clear()
        {
            VideoFrame f;
            while (_free.TryPop(out f)) { }
        }
    }
}
