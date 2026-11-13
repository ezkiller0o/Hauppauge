using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Media;

namespace HauppaugeLiveCaptureStudio.UI
{
    internal enum AspectMode { Auto, Native, Wide, Stretch }

    internal enum ToastKind { Info, Success, Warning, Error }

    /// <summary>
    /// Draws the live picture. The capture thread hands over the newest frame (older undisplayed frames are discarded,
    /// so the picture never lags behind); painting happens immediately on the UI thread. No scaling filters beyond
    /// plain bilinear and no image processing at all - the view shows exactly what the device delivers.
    /// The recording badge and the toast messages are painted on the screen only; they are never part of snapshots or recordings.
    /// </summary>
    internal sealed class VideoView : Control
    {
        private VideoFrame _pending;
        private VideoFrame _current;
        private int _paintRequested;
        private double _latencyMs;

        private string _recordingText;
        private string _placeholder = "";
        private string _toast;
        private Color _toastColor = Theme.Accent;
        private readonly System.Windows.Forms.Timer _toastTimer;

        public AspectMode Aspect { get; set; }

        /// <summary>Smoothed time between "frame arrived from the driver" and "frame painted" (software only).</summary>
        public double LatencyMs { get { return _latencyMs; } }

        public string Placeholder
        {
            get { return _placeholder; }
            set { _placeholder = value ?? ""; Invalidate(); }
        }

        public string RecordingText
        {
            get { return _recordingText; }
            set
            {
                if (_recordingText == value) return;
                _recordingText = value;
                Invalidate();
            }
        }

        public VideoView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            ForeColor = Color.White;
            TabStop = false;

            _toastTimer = new System.Windows.Forms.Timer();
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _toast = null;
                Invalidate();
            };
        }

        // ------------------------------------------------------------------ frame hand-over (capture thread)

        public void Present(VideoFrame frame)
        {
            frame.AddRef();
            VideoFrame old = Interlocked.Exchange(ref _pending, frame);
            if (old != null) old.Release();
            RequestPaint();
        }

        private void RequestPaint()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (Interlocked.Exchange(ref _paintRequested, 1) != 0) return;
            try
            {
                BeginInvoke(new Action(PaintNow));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref _paintRequested, 0); }
            catch (ObjectDisposedException) { }
        }

        private void PaintNow()
        {
            Interlocked.Exchange(ref _paintRequested, 0);
            if (IsDisposed) return;
            Invalidate();
            Update();       // paint right now instead of waiting for the low-priority WM_PAINT
        }

        /// <summary>
        /// A queued BeginInvoke can be lost when the window handle is re-created (for example when the UI language
        /// switches between left-to-right and right-to-left). The flag is then reset so the preview can never freeze.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            Interlocked.Exchange(ref _paintRequested, 0);
            base.OnHandleCreated(e);
        }

        /// <summary>Called from the UI timer as a safety net; harmless when nothing is stuck.</summary>
        public void ResetPaintRequest()
        {
            Interlocked.Exchange(ref _paintRequested, 0);
        }

        /// <summary>UI thread: forget the picture (live stopped).</summary>
        public void ClearFrame()
        {
            VideoFrame p = Interlocked.Exchange(ref _pending, null);
            if (p != null) p.Release();
            if (_current != null)
            {
                _current.Release();
                _current = null;
            }
            _latencyMs = 0;
            Invalidate();
        }

        public void ShowToast(string text, ToastKind kind, int milliseconds)
        {
            _toast = text;
            switch (kind)
            {
                case ToastKind.Success: _toastColor = Theme.Green; break;
                case ToastKind.Warning: _toastColor = Theme.Amber; break;
                case ToastKind.Error: _toastColor = Theme.Red; break;
                default: _toastColor = Theme.Accent; break;
            }
            _toastTimer.Stop();
            _toastTimer.Interval = Math.Max(1000, milliseconds);
            _toastTimer.Start();
            Invalidate();
        }

        // ------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            VideoFrame fresh = Interlocked.Exchange(ref _pending, null);
            if (fresh != null)
            {
                if (_current != null) _current.Release();
                _current = fresh;
            }

            g.Clear(Color.Black);

            VideoFrame f = _current;
            if (f == null)
            {
                DrawPlaceholder(g);
            }
            else
            {
                Rectangle dest = ComputeDestination(ClientSize.Width, ClientSize.Height, f.Width, f.Height);
                if (dest.Width > 0 && dest.Height > 0)
                {
                    GCHandle pin = GCHandle.Alloc(f.Data, GCHandleType.Pinned);
                    try
                    {
                        using (Bitmap bmp = new Bitmap(f.Width, f.Height, f.Stride, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject()))
                        {
                            g.InterpolationMode = InterpolationMode.Bilinear;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImage(bmp, dest, 0, 0, f.Width, f.Height, GraphicsUnit.Pixel);
                            g.CompositingMode = CompositingMode.SourceOver;
                        }
                    }
                    finally
                    {
                        pin.Free();
                    }

                    double ms = (Stopwatch.GetTimestamp() - f.Ticks) * 1000.0 / Stopwatch.Frequency;
                    _latencyMs = _latencyMs <= 0 ? ms : _latencyMs * 0.92 + ms * 0.08;
                }
            }

            DrawBadge(g);
            DrawToast(g);
        }

        private Rectangle ComputeDestination(int cw, int ch, int fw, int fh)
        {
            if (cw <= 0 || ch <= 0 || fw <= 0 || fh <= 0) return Rectangle.Empty;
            if (Aspect == AspectMode.Stretch) return new Rectangle(0, 0, cw, ch);

            double ratio;
            switch (Aspect)
            {
                case AspectMode.Wide: ratio = 16.0 / 9.0; break;
                case AspectMode.Native: ratio = (double)fw / fh; break;
                default: ratio = (fw <= 720 && fh <= 600) ? 4.0 / 3.0 : (double)fw / fh; break;
            }

            int w = cw;
            int h = (int)Math.Round(cw / ratio);
            if (h > ch)
            {
                h = ch;
                w = (int)Math.Round(ch * ratio);
            }
            return new Rectangle((cw - w) / 2, (ch - h) / 2, w, h);
        }

        private void DrawPlaceholder(Graphics g)
        {
            if (string.IsNullOrEmpty(_placeholder)) return;
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            Rectangle r = new Rectangle(24, 24, Math.Max(10, Width - 48), Math.Max(10, Height - 48));
            TextRenderer.DrawText(g, _placeholder, Theme.TitleFont, r, Color.FromArgb(150, 160, 178), flags);
        }

        private void DrawBadge(Graphics g)
        {
            string text = _recordingText;
            if (string.IsNullOrEmpty(text)) return;

            Size size = TextRenderer.MeasureText(g, text, Theme.ButtonFont, new Size(400, 40), TextFormatFlags.NoPrefix);
            Rectangle box = new Rectangle(16, 16, size.Width + 46, 34);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                g.FillRectangle(bg, box);
            using (SolidBrush red = new SolidBrush(Color.FromArgb(235, 40, 40)))
                g.FillEllipse(red, new Rectangle(box.X + 12, box.Y + 9, 16, 16));
            TextRenderer.DrawText(g, text, Theme.ButtonFont, new Rectangle(box.X + 34, box.Y, box.Width - 36, box.Height),
                Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawToast(Graphics g)
        {
            string text = _toast;
            if (string.IsNullOrEmpty(text)) return;

            int maxWidth = Math.Max(200, Width - 80);
            TextFormatFlags measureFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            Size size = TextRenderer.MeasureText(g, text, Theme.ToastFont, new Size(maxWidth - 40, 400), measureFlags);
            int boxWidth = Math.Min(maxWidth, size.Width + 40);
            int boxHeight = size.Height + 24;
            Rectangle box = new Rectangle((Width - boxWidth) / 2, Height - boxHeight - 28, boxWidth, boxHeight);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, _toastColor)))
                g.FillRectangle(fill, box);

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, text, Theme.ToastFont, box, Color.White, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toastTimer.Dispose();
                VideoFrame p = Interlocked.Exchange(ref _pending, null);
                if (p != null) p.Release();
                if (_current != null)
                {
                    _current.Release();
                    _current = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
