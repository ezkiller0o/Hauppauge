using System.Drawing;

namespace HauppaugeLiveCaptureStudio.UI
{
    /// <summary>Colours and fonts. Dark "stage" (top bar, video, status bar) with a light side panel.</summary>
    internal static class Theme
    {
        public static readonly Color Stage = Color.FromArgb(10, 12, 16);
        public static readonly Color Bar = Color.FromArgb(20, 24, 31);
        public static readonly Color BarText = Color.FromArgb(232, 236, 242);
        public static readonly Color BarDim = Color.FromArgb(140, 150, 168);

        public static readonly Color Side = Color.FromArgb(243, 245, 248);
        public static readonly Color SideText = Color.FromArgb(28, 34, 44);
        public static readonly Color SideDim = Color.FromArgb(104, 114, 130);
        public static readonly Color Border = Color.FromArgb(212, 218, 228);

        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color Gray = Color.FromArgb(96, 106, 124);
        public static readonly Color DisabledFill = Color.FromArgb(205, 211, 221);
        public static readonly Color DisabledText = Color.FromArgb(150, 158, 172);

        public static readonly Font UiFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SectionFont = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font ButtonFont = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font TimerFont = new Font("Consolas", 14f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font ToastFont = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>Item for combo boxes: shows Text, carries a typed Value.</summary>
    internal sealed class ListItem
    {
        public readonly string Text;
        public readonly object Value;

        public ListItem(string text, object value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString() { return Text; }
    }
}
