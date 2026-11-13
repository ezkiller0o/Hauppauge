using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>
    /// A dedicated MTA thread that owns every DirectShow COM object.
    /// The UI thread (STA) never touches those objects, so a driver call that blocks
    /// (for example when the USB device is unplugged) cannot freeze the window.
    /// </summary>
    internal sealed class MtaWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private volatile bool _disposed;

        public MtaWorker(string name)
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = name;
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Loop()
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception ex) { Logger.Error("Unhandled exception in " + _thread.Name, ex); }
            }
        }

        public Task<T> Run<T>(Func<T> func)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_disposed)
            {
                tcs.SetException(new ObjectDisposedException("MtaWorker"));
                return tcs.Task;
            }

            try
            {
                _queue.Add(delegate
                {
                    try { tcs.SetResult(func()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException ex)
            {
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        public Task Run(Action action)
        {
            return Run<bool>(delegate { action(); return true; });
        }

        /// <summary>Stops accepting work; the thread ends after the queued work is finished.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _queue.CompleteAdding(); } catch { }
        }
    }
}
