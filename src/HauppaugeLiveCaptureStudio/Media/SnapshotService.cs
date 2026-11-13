using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.Media
{
    /// <summary>
    /// Saves the newest live frame as JPG (quality 100) or PNG (lossless) at the full resolution delivered by the device.
    /// The work runs on a pool thread and only reads the frame, so it never disturbs the preview or a running recording.
    /// An existing file is never overwritten: "name.jpg", "name (1).jpg", "name (2).jpg" ...
    /// </summary>
    internal static class SnapshotService
    {
        public static Task<string> SaveAsync(FrameHub hub, string name, string format, string directory)
        {
            VideoFrame frame = hub.AcquireLatest();
            if (frame == null)
                throw new InvalidOperationException("No video frame is available yet. Start the live view first.");

            return Task.Run(() =>
            {
                try { return Save(frame, name, format, directory); }
                finally { frame.Release(); }
            });
        }

        private static string Save(VideoFrame frame, string name, string format, string directory)
        {
            bool png = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase);
            string baseName = FileNamer.Sanitize(name, "Snapshot");

            string path;
            FileStream fs = FileNamer.CreateUnique(directory, baseName, png ? ".png" : ".jpg", out path);
            bool ok = false;

            GCHandle pin = GCHandle.Alloc(frame.Data, GCHandleType.Pinned);
            try
            {
                using (fs)
                using (Bitmap source = new Bitmap(frame.Width, frame.Height, frame.Stride, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject()))
                using (Bitmap bmp = source.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format24bppRgb))
                {
                    if (png)
                    {
                        bmp.Save(fs, ImageFormat.Png);
                    }
                    else
                    {
                        ImageCodecInfo jpeg = FindJpegCodec();
                        using (EncoderParameters parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 100L);
                            bmp.Save(fs, jpeg, parameters);
                        }
                    }
                    fs.Flush();
                }
                ok = true;
            }
            finally
            {
                pin.Free();
                if (!ok)
                {
                    try { File.Delete(path); } catch { }
                }
            }

            Logger.Info("Snapshot saved: " + path);
            return path;
        }

        private static ImageCodecInfo FindJpegCodec()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            throw new InvalidOperationException("The JPEG encoder is not available on this system.");
        }
    }
}
