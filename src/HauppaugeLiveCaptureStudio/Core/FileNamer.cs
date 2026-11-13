using System;
using System.IO;
using System.Text;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>Safe file names and collision-free creation (never overwrites an existing file).</summary>
    internal static class FileNamer
    {
        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string Sanitize(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) name = fallback;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            string s = sb.ToString().Trim().TrimEnd('.', ' ');
            if (s.Length == 0) s = fallback;
            if (s.Length > 100) s = s.Substring(0, 100).TrimEnd('.', ' ');

            string upper = s.ToUpperInvariant();
            foreach (string r in Reserved)
            {
                if (upper == r || upper.StartsWith(r + "."))
                {
                    s = "_" + s;
                    break;
                }
            }
            return s;
        }

        /// <summary>
        /// Atomically creates a new empty file whose name does not exist yet:
        /// "name.ext", "name (1).ext", "name (2).ext" ...
        /// Because the file is created with FileMode.CreateNew, an existing file can never be overwritten.
        /// </summary>
        public static FileStream CreateUnique(string directory, string baseName, string extension, out string path)
        {
            Directory.CreateDirectory(directory);
            string ext = extension.StartsWith(".") ? extension : "." + extension;

            for (int i = 0; i < 10000; i++)
            {
                string candidate = Path.Combine(
                    directory,
                    i == 0 ? baseName + ext : baseName + " (" + i + ")" + ext);

                try
                {
                    FileStream fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    path = candidate;
                    return fs;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    // name already taken -> try the next number
                }
            }
            throw new IOException("Could not find a free file name in " + directory);
        }
    }
}
