using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.Media
{
    /// <summary>Finds ffmpeg.exe and checks which H.264 encoder the build contains.</summary>
    internal static class FfmpegLocator
    {
        private static readonly Dictionary<string, string> EncoderCache = new Dictionary<string, string>();

        /// <summary>Order: path from the settings, next to the EXE (ffmpeg\ffmpeg.exe or ffmpeg.exe), then PATH.</summary>
        public static string Find(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            string dir = AppPaths.AppDir;
            string[] candidates =
            {
                Path.Combine(dir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(dir, "ffmpeg.exe"),
                Path.Combine(dir, "tools", "ffmpeg.exe"),
                Path.Combine(dir, "ffmpeg", "bin", "ffmpeg.exe")
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;

            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVar))
            {
                foreach (string part in pathVar.Split(Path.PathSeparator))
                {
                    try
                    {
                        string p = part.Trim().Trim('"');
                        if (p.Length == 0) continue;
                        string candidate = Path.Combine(p, "ffmpeg.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>
        /// Returns "libx264" (preferred) or "h264_mf" (Windows Media Foundation encoder), or null when the build has neither.
        /// The result is cached per ffmpeg path.
        /// </summary>
        public static string DetectH264Encoder(string ffmpegPath)
        {
            lock (EncoderCache)
            {
                string cached;
                if (EncoderCache.TryGetValue(ffmpegPath, out cached)) return cached;
            }

            string result = null;
            try
            {
                string output = Run(ffmpegPath, new[] { "-hide_banner", "-encoders" }, 15000);
                if (output.Contains(" libx264 ")) result = "libx264";
                else if (output.Contains(" h264_mf ")) result = "h264_mf";
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not query FFmpeg encoders: " + ex.Message);
                return null;        // do not cache a failure
            }

            lock (EncoderCache) { EncoderCache[ffmpegPath] = result; }
            Logger.Info("FFmpeg H.264 encoder: " + (result ?? "none"));
            return result;
        }

        public static string GetVersionLine(string ffmpegPath)
        {
            try
            {
                string output = Run(ffmpegPath, new[] { "-hide_banner", "-version" }, 10000);
                int nl = output.IndexOf('\n');
                return (nl > 0 ? output.Substring(0, nl) : output).Trim();
            }
            catch (Exception ex)
            {
                return "unavailable (" + ex.Message + ")";
            }
        }

        private static string Run(string exe, string[] args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            foreach (string a in args) psi.ArgumentList.Add(a);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                // read stderr asynchronously so neither pipe can fill up and block the process
                p.ErrorDataReceived += delegate { };
                p.BeginErrorReadLine();
                string stdout = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("ffmpeg did not answer in time");
                }
                return stdout;
            }
        }
    }
}
