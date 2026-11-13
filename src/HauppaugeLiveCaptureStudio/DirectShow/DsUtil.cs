using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HauppaugeLiveCaptureStudio.DirectShow
{
    internal static class DsUtil
    {
        /// <summary>Creates a COM object from its CLSID (works on the MTA worker thread).</summary>
        public static T Create<T>(Guid clsid) where T : class
        {
            Type t = Type.GetTypeFromCLSID(clsid, true);
            return (T)Activator.CreateInstance(t);
        }

        /// <summary>Allocates a zero-filled AM_MEDIA_TYPE with CoTaskMemAlloc.</summary>
        public static IntPtr AllocMediaType()
        {
            IntPtr p = Marshal.AllocCoTaskMem(Marshal.SizeOf<AMMediaType>());
            Marshal.StructureToPtr(new AMMediaType(), p, false);
            return p;
        }

        /// <summary>Frees an AM_MEDIA_TYPE (format block + the structure itself). Both are CoTaskMem memory.</summary>
        public static void FreeMediaType(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            try
            {
                AMMediaType mt = Marshal.PtrToStructure<AMMediaType>(pmt);
                if (mt.FormatSize != 0 && mt.FormatPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.FormatPtr);
                if (mt.Unknown != IntPtr.Zero) Marshal.Release(mt.Unknown);
            }
            catch { }
            Marshal.FreeCoTaskMem(pmt);
        }

        /// <summary>Copies a Guid into unmanaged memory (for the "const GUID*" parameters). Free with FreeCoTaskMem.</summary>
        public static IntPtr AllocGuid(Guid g)
        {
            IntPtr p = Marshal.AllocCoTaskMem(16);
            Marshal.StructureToPtr(g, p, false);
            return p;
        }

        /// <summary>Releases every RCW reference of a COM object (no-op for null / non-COM objects).</summary>
        public static void Release(object o)
        {
            if (o == null) return;
            try
            {
                if (Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
            }
            catch { }
        }

        /// <summary>Drops one reference that was added when a COM pointer was marshalled into managed code.</summary>
        public static void ReleaseOne(object o)
        {
            if (o == null) return;
            try
            {
                if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
            }
            catch { }
        }

        /// <summary>"YUY2", "UYVY", "RGB32" ... from a media subtype GUID.</summary>
        public static string SubtypeName(Guid g)
        {
            if (g == DsGuids.MEDIASUBTYPE_RGB24) return "RGB24";
            if (g == DsGuids.MEDIASUBTYPE_RGB32) return "RGB32";

            byte[] b = g.ToByteArray();
            // FOURCC based subtype: XXXXXXXX-0000-0010-8000-00AA00389B71
            bool fourcc = b[4] == 0x00 && b[5] == 0x00 && b[6] == 0x10 && b[7] == 0x00 && b[8] == 0x80 && b[9] == 0x00
                          && b[10] == 0x00 && b[11] == 0xAA && b[12] == 0x00 && b[13] == 0x38 && b[14] == 0x9B && b[15] == 0x71;
            if (fourcc)
            {
                StringBuilder sb = new StringBuilder(4);
                for (int i = 0; i < 4; i++)
                {
                    char c = (char)b[i];
                    sb.Append(c >= 32 && c < 127 ? c : '?');
                }
                return sb.ToString().Trim();
            }
            return g.ToString().Substring(0, 8).ToUpperInvariant();
        }

        /// <summary>Lower number = preferred (uncompressed, cheap to convert).</summary>
        public static int SubtypeRank(string name)
        {
            switch (name)
            {
                case "YUY2": return 0;
                case "UYVY": return 1;
                case "YV12":
                case "I420":
                case "IYUV":
                case "NV12": return 2;
                case "RGB32":
                case "RGB24": return 3;
                default: return 9;      // MJPG, H264, ... need a decoder in the graph
            }
        }

        public static string Hex(int hr)
        {
            return "0x" + unchecked((uint)hr).ToString("X8");
        }

        /// <summary>Human readable text for the HRESULTs that matter for capture devices.</summary>
        public static string DescribeHr(int hr)
        {
            uint u = unchecked((uint)hr);
            string text;
            switch (u)
            {
                case 0x80070020: text = "the device is in use by another application"; break;
                case 0x80070005: text = "access denied (check Windows privacy settings for camera access)"; break;
                case 0x8007001F: text = "the device is not functioning (driver problem or USB error)"; break;
                case 0x800705AA: text = "insufficient system resources (try another USB port)"; break;
                case 0x80040154: text = "a required DirectShow component is not registered (qedit.dll)"; break;
                case 0x80040217: text = "no combination of filters could connect the capture pin (unsupported video format)"; break;
                case 0x80040218: text = "the capture stream could not be rendered"; break;
                case 0x80040200: text = "invalid media type"; break;
                case 0x80040228: text = "the device does not support the requested format"; break;
                case 0x80004005: text = "unspecified error (E_FAIL)"; break;
                case 0x80004004: text = "operation aborted"; break;
                default: text = null; break;
            }
            return text == null ? Hex(hr) : text + " [" + Hex(hr) + "]";
        }
    }
}
