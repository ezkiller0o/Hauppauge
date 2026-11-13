using System;
using System.IO;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>
    /// Folder layout:  [base]\Hauppauge Capture Studio\{Pictures, Videos, Settings}
    /// The chosen base folder is remembered in %LOCALAPPDATA% (a tiny pointer file) so that the
    /// real settings file can live inside the "Settings" sub-folder of the main folder.
    /// </summary>
    internal static class AppPaths
    {
        public const string AppName = "Hauppauge Live Capture Studio";
        public const string RootFolderName = "Hauppauge Capture Studio";

        private static string _root;

        private static string BootstrapDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HauppaugeLiveCaptureStudio");
            }
        }

        private static string BootstrapFile { get { return Path.Combine(BootstrapDir, "root.txt"); } }

        public static string RootDir { get { return _root; } }
        public static string PicturesDir { get { return Path.Combine(_root, "Pictures"); } }
        public static string VideosDir { get { return Path.Combine(_root, "Videos"); } }
        public static string SettingsDir { get { return Path.Combine(_root, "Settings"); } }
        public static string SettingsFile { get { return Path.Combine(SettingsDir, "settings.json"); } }
        public static string LogFile { get { return Path.Combine(SettingsDir, "app.log"); } }

        /// <summary>Folder that contains the running EXE (also valid for single-file publish).</summary>
        public static string AppDir { get { return AppContext.BaseDirectory; } }

        public static string DefaultBaseFolder()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Turns a folder picked by the user into the main folder path.</summary>
        public static string ComposeRoot(string baseFolder)
        {
            string trimmed = baseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), RootFolderName, StringComparison.OrdinalIgnoreCase))
                return trimmed;
            return Path.Combine(trimmed, RootFolderName);
        }

        public static void Initialize()
        {
            string saved = null;
            try
            {
                if (File.Exists(BootstrapFile)) saved = File.ReadAllText(BootstrapFile).Trim();
            }
            catch { }

            string error;
            if (!string.IsNullOrWhiteSpace(saved) && TryUseRoot(saved, false, out error)) return;
            if (TryUseRoot(ComposeRoot(DefaultBaseFolder()), false, out error)) return;

            string fallback = Path.Combine(BootstrapDir, RootFolderName);
            if (!TryUseRoot(fallback, false, out error)) _root = fallback;
        }

        /// <summary>Creates the main folder and its three sub-folders and makes it the active root.</summary>
        public static bool TryUseRoot(string root, bool persist, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "Pictures"));
                Directory.CreateDirectory(Path.Combine(root, "Videos"));
                Directory.CreateDirectory(Path.Combine(root, "Settings"));

                string probe = Path.Combine(root, "Settings", ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);

                _root = root;
                if (persist)
                {
                    Directory.CreateDirectory(BootstrapDir);
                    File.WriteAllText(BootstrapFile, root);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
