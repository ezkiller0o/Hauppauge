using System;
using System.IO;
using System.Text.Json;

namespace HauppaugeLiveCaptureStudio.Core
{
    /// <summary>Persistent user settings (Settings\settings.json inside the main folder).</summary>
    internal sealed class AppSettings
    {
        public string Language { get; set; } = "en";            // "en" | "ar"

        // Last used device and video settings
        public string LastDeviceId { get; set; } = "";
        public string LastDeviceName { get; set; } = "";
        public string LastInputKey { get; set; } = "";          // e.g. "Composite", "SVideo"
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
        public string Aspect { get; set; } = "Auto";            // Auto | Native | Wide | Stretch

        // Snapshot / recording UI state
        public string SnapshotFormat { get; set; } = "JPG";     // JPG | PNG
        public string SnapshotName { get; set; } = "Snapshot";
        public string VideoName { get; set; } = "Recording";
        public bool AppendDateTime { get; set; } = true;

        // Behaviour
        public bool AutoStartLive { get; set; } = false;
        public string FfmpegPath { get; set; } = "";
        public int RecordCrf { get; set; } = 18;               // x264 quality: lower = better
        public bool FragmentedMp4 { get; set; } = false;       // fragmented MP4 survives a crash / power loss, but some editors dislike it

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static AppSettings Load()
        {
            try
            {
                string file = AppPaths.SettingsFile;
                if (File.Exists(file))
                {
                    AppSettings s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions);
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not read settings, using defaults: " + ex.Message);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.SettingsDir);
                string tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tmp, AppPaths.SettingsFile, true);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not save settings: " + ex.Message);
            }
        }
    }
}
