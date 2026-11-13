using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HauppaugeLiveCaptureStudio.UI
{
    /// <summary>Flat, rounded, colour-coded button that also looks right when disabled and in right-to-left mode.</summary>
    internal sealed class FlatButton : Button
    {
        private bool _hover;
        private bool _down;
        private Color _baseColor = Theme.Accent;

        public Color BaseColor
        {
            get { return _baseColor; }
            set { _baseColor = value; Invalidate(); }
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.ButtonFont;
            ForeColor = Color.White;
            Height = 40;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        private static Color Shade(Color c, double amount)
        {
            int r = (int)Math.Max(0, Math.Min(255, c.R + (amount >= 0 ? (255 - c.R) : c.R) * amount));
            int g = (int)Math.Max(0, Math.Min(255, c.G + (amount >= 0 ? (255 - c.G) : c.G) * amount));
            int b = (int)Math.Max(0, Math.Min(255, c.B + (amount >= 0 ? (255 - c.B) : c.B) * amount));
            return Color.FromArgb(r, g, b);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color back = Parent != null ? Parent.BackColor : Theme.Side;
            g.Clear(back);

            Color fill;
            if (!Enabled) fill = Theme.DisabledFill;
            else if (_down) fill = Shade(_baseColor, -0.18);
            else if (_hover) fill = Shade(_baseColor, 0.12);
            else fill = _baseColor;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(r, 8))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            if (Focused && ShowFocusCues)
            {
                Rectangle f = new Rectangle(3, 3, Width - 7, Height - 7);
                using (GraphicsPath fp = RoundedRect(f, 6))
                using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                {
                    g.DrawPath(pen, fp);
                }
            }

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.DisabledText, flags);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Small round status light.</summary>
    internal sealed class StatusLed : Control
    {
        private Color _color = Theme.Red;

        public Color LedColor
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        public StatusLed()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(16, 16);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Color.Black);

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(60, _color)))
                g.FillEllipse(halo, new Rectangle(0, 0, Width - 1, Height - 1));
            using (SolidBrush b = new SolidBrush(_color))
                g.FillEllipse(b, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4));
        }
    }
}
