using System;
using System.Collections.Generic;
using System.Globalization;

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    internal sealed class CaptureDevice
    {
        public string Id;       // moniker display name (unique per device)
        public string Name;     // friendly name shown to the user

        public override string ToString() { return Name; }

        public bool LooksLikeHauppauge
        {
            get
            {
                string n = (Name ?? "").ToLowerInvariant();
                return n.Contains("hauppauge") || n.Contains("usb-live") || n.Contains("usb live") || n.Contains("wintv");
            }
        }
    }

    /// <summary>One entry of IAMStreamConfig::GetStreamCaps (a resolution + pixel format + frame-rate range).</summary>
    internal sealed class VideoFormat
    {
        public int CapIndex;
        public int Width;
        public int Height;
        public Guid SubType;
        public string SubTypeName;
        public long DefaultInterval;     // 100 ns units, 0 = unknown
        public long MinInterval;         // shortest frame interval  -> highest fps
        public long MaxInterval;         // longest frame interval   -> lowest fps

        public double DefaultFps { get { return DefaultInterval > 0 ? 1e7 / DefaultInterval : 0; } }
        public double MaxFps { get { return MinInterval > 0 ? 1e7 / MinInterval : DefaultFps; } }
        public double MinFps { get { return MaxInterval > 0 ? 1e7 / MaxInterval : DefaultFps; } }

        public string ResolutionText { get { return Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture); } }

        private static readonly double[] Standard =
            { 60, 59.94, 50, 30, 29.97, 25, 24, 23.976, 20, 15, 12, 10, 7.5, 5 };

        /// <summary>Frame rates that the device really offers for this format.</summary>
        public List<double> GetFpsOptions()
        {
            List<double> list = new List<double>();
            double lo = MinFps, hi = MaxFps;

            if (hi > 0 && lo > 0 && hi - lo > 0.05)
            {
                foreach (double s in Standard)
                    if (s >= lo - 0.02 && s <= hi + 0.02) list.Add(s);
            }

            double def = DefaultFps;
            if (def > 0 && !ContainsFps(list, def)) list.Add(Math.Round(def, 3));
            if (list.Count == 0 && hi > 0) list.Add(Math.Round(hi, 3));
            if (list.Count == 0) list.Add(29.97);

            list.Sort(delegate (double a, double b) { return b.CompareTo(a); });
            return list;
        }

        private static bool ContainsFps(List<double> list, double v)
        {
            foreach (double d in list) if (Math.Abs(d - v) < 0.02) return true;
            return false;
        }

        public static string FormatFps(double fps)
        {
            return fps.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A selectable analog input (Composite, S-Video ...) exposed by the device's crossbar.</summary>
    internal sealed class VideoInput
    {
        public int CrossbarIndex;
        public int PinIndex;
        public int PhysicalType;
        public string Key;              // stable id stored in the settings: "Composite", "SVideo", ...
        public string DisplayName;      // English fallback name

        public override string ToString() { return DisplayName; }
    }

    internal sealed class DeviceCaps
    {
        public List<VideoFormat> Formats = new List<VideoFormat>();
        public List<VideoInput> Inputs = new List<VideoInput>();
        public string ActiveInputKey = "";
        public string Note = "";
    }

    internal sealed class StartInfo
    {
        public string DeviceName;
        public int Width;
        public int Height;
        public double Fps;
        public string SourceFormat;     // pixel format delivered by the device (before conversion)
        public string InputKey;
        public DeviceCaps Caps;
    }

    internal sealed class CaptureException : Exception
    {
        public readonly int Hr;

        public CaptureException(string message, int hr = 0, Exception inner = null)
            : base(message, inner)
        {
            Hr = hr;
        }
    }
}
