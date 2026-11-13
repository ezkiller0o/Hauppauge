using System;
using System.Collections.Generic;
using System.IO;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>Small thread-safe file logger (Settings\app.log) with an in-memory tail for diagnostics.</summary>
    internal static class Logger
    {
        private static readonly object Gate = new object();
        private static readonly Queue<string> Recent = new Queue<string>();
        private const long MaxBytes = 2L * 1024 * 1024;

        public static void Info(string message) { Write("INFO ", message); }
        public static void Warn(string message) { Write("WARN ", message); }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", ex == null ? message : message + " :: " + ex);
        }

        public static string[] Snapshot()
        {
            lock (Gate) { return Recent.ToArray(); }
        }

        private static void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
            lock (Gate)
            {
                Recent.Enqueue(line);
                while (Recent.Count > 400) Recent.Dequeue();

                try
                {
                    string file = AppPaths.LogFile;
                    string dir = Path.GetDirectoryName(file);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    FileInfo fi = new FileInfo(file);
                    if (fi.Exists && fi.Length > MaxBytes)
                        File.Move(file, file + ".old", true);

                    File.AppendAllText(file, line + Environment.NewLine);
                }
                catch
                {
                    // logging must never break the application
                }
            }
        }
    }
}
