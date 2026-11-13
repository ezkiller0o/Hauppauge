using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Core;
using HauppaugeLiveCaptureStudio.DirectShow;
using HauppaugeLiveCaptureStudio.Media;

namespace HauppaugeLiveCaptureStudio.UI
{
    /// <summary>Button actions: snapshot, recording, full screen, settings, language.</summary>
    internal sealed partial class MainForm
    {
        private bool _fullScreen;
        private FormBorderStyle _prevBorder;
        private FormWindowState _prevState;
        private Rectangle _prevBounds;
        private bool _prevTopMost;

        // ------------------------------------------------------------------ simple click handlers

        private async void OnStartLiveClick(object sender, EventArgs e) { await StartLiveAsync(); }

        private async void OnStopLiveClick(object sender, EventArgs e) { await StopLiveAsync(); }

        private async void OnRefreshClick(object sender, EventArgs e)
        {
            try
            {
                await RefreshDevicesAsync();
                if (!_live && !_busy && SelectedDevice != null && _caps.Formats.Count == 0) await ProbeSelectedAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Refresh failed", ex);
            }
        }

        private void OnFullClick(object sender, EventArgs e) { EnterFullScreen(); }

        private void OnExitFullClick(object sender, EventArgs e) { ExitFullScreen(); }

        private async void OnSnapshotClick(object sender, EventArgs e) { await TakeSnapshotAsync(); }

        private async void OnStartRecClick(object sender, EventArgs e) { await StartRecordingAsync(); }

        private async void OnStopRecClick(object sender, EventArgs e) { await StopRecordingAsync(); }

        private async void OnFsRecClick(object sender, EventArgs e)
        {
            if (_recorder.IsRecording) await StopRecordingAsync();
            else await StartRecordingAsync();
        }

        private void OnLanguageClick(object sender, EventArgs e)
        {
            string next = Loc.IsArabic ? "en" : "ar";
            _settings.Language = next;
            Loc.SetLanguage(next);
            ApplyLanguage();
            _settings.Save();
        }

        /// <summary>Clicking the status message opens Explorer on the last saved picture / video.</summary>
        private void OnMessageClick(object sender, EventArgs e)
        {
            string path = _lastSavedPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open Explorer: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ snapshot

        private async Task TakeSnapshotAsync()
        {
            if (!_live)
            {
                Toast(Loc.T("msg.noLive"), ToastKind.Warning, 3000);
                return;
            }

            ListItem format = _cmbSnapFormat.SelectedItem as ListItem;
            string fmt = format == null ? "JPG" : (string)format.Value;
            try
            {
                string path = await SnapshotService.SaveAsync(_hub, _txtSnapName.Text, fmt, AppPaths.PicturesDir);
                _lastSavedPath = path;
                Toast(Loc.T("msg.snapshotSaved", path), ToastKind.Success, 4500);
            }
            catch (Exception ex)
            {
                Logger.Error("Snapshot failed", ex);
                Toast(Loc.T("msg.snapshotFailed") + " " + ex.Message, ToastKind.Error, 6000);
            }
        }

        // ------------------------------------------------------------------ recording

        private string RecordingAspectArg()
        {
            switch (_view.Aspect)
            {
                case AspectMode.Wide:
                    return "16:9";
                case AspectMode.Auto:
                    return (_hub.Width <= 720 && _hub.Height <= 600 && _hub.Width > 0) ? "4:3" : null;
                default:
                    return null;
            }
        }

        private async Task StartRecordingAsync()
        {
            if (!_live)
            {
                Toast(Loc.T("msg.noLive"), ToastKind.Warning, 3000);
                return;
            }
            if (_recorder.IsRecording || _recBusy || _busy) return;      // never two recordings at the same time

            string ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
            if (ffmpeg == null)
            {
                MessageBox.Show(this, Loc.T("msg.noFfmpeg"), AppPaths.AppName, MessageBoxButtons.OK,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, RtlOptions());
                return;
            }

            RecorderOptions options = new RecorderOptions();
            options.FfmpegPath = ffmpeg;
            options.Directory = AppPaths.VideosDir;
            options.BaseName = _txtVideoName.Text;
            options.AppendDateTime = _chkAppendDate.Checked;
            options.Crf = _settings.RecordCrf;
            options.FragmentedMp4 = _settings.FragmentedMp4;
            options.AspectArg = RecordingAspectArg();

            _recBusy = true;
            UpdateUiState();
            try
            {
                await Task.Run(() => _recorder.Start(options));
                _lastSavedPath = _recorder.OutputPath;
                Toast(Loc.T("msg.recStarted", Path.GetFileName(_recorder.OutputPath)), ToastKind.Success, 3500);
            }
            catch (Exception ex)
            {
                Logger.Error("Start recording failed", ex);
                SetMessage(Loc.T("msg.recFailed"), true);
                MessageBox.Show(this, Loc.T("msg.recFailed") + "\n\n" + ex.Message, AppPaths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, RtlOptions());
            }
            finally
            {
                _recBusy = false;
                UpdateUiState();
            }
        }

        private async Task StopRecordingAsync()
        {
            if (_recBusy || !_recorder.IsRecording) return;

            _recBusy = true;
            UpdateUiState();
            RecorderResult result = null;
            try
            {
                result = await Task.Run(() => _recorder.Stop());
            }
            catch (Exception ex)
            {
                Logger.Error("Stop recording failed", ex);
            }
            finally
            {
                _recBusy = false;
                UpdateUiState();
            }

            if (result == null) return;
            if (result.Success)
            {
                _lastSavedPath = result.Path;
                Toast(Loc.T("msg.recSaved", result.Path), ToastKind.Success, 6000);
            }
            else
            {
                Toast(Loc.T("msg.recFailed") + " " + result.Message, ToastKind.Error, 8000);
                if (!_shutdownStarted && !_fullScreen)
                    MessageBox.Show(this, Loc.T("msg.recFailed") + "\n\n" + result.Message, AppPaths.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, RtlOptions());
            }
        }

        /// <summary>ffmpeg died or the pipe broke during a recording (raised on a background thread).</summary>
        private void OnRecorderFailed(string message)
        {
            if (IsDisposed || _shutdownStarted) return;
            try { BeginInvoke(new Action(delegate { HandleRecorderFailed(message); })); }
            catch (InvalidOperationException) { }
        }

        private async void HandleRecorderFailed(string message)
        {
            if (_recBusy) return;
            _recBusy = true;
            UpdateUiState();

            RecorderResult result = null;
            try { result = await Task.Run(() => _recorder.Stop()); }
            catch (Exception ex) { Logger.Error("Cleanup after recorder failure failed", ex); }
            finally
            {
                _recBusy = false;
                UpdateUiState();
            }

            string text = Loc.T("msg.recFailed") + "\n" + message;
            if (result != null && !string.IsNullOrEmpty(result.Path) && File.Exists(result.Path))
                text += "\n\n" + Loc.T("msg.recSaved", result.Path);

            SetMessage(Loc.T("msg.recFailed"), true);
            if (_fullScreen) ExitFullScreen();
            MessageBox.Show(this, text, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, RtlOptions());
        }

        // ------------------------------------------------------------------ full screen

        private void ToggleFullScreen()
        {
            if (_fullScreen) ExitFullScreen();
            else EnterFullScreen();
        }

        private void EnterFullScreen()
        {
            if (_fullScreen) return;

            _prevBorder = FormBorderStyle;
            _prevState = WindowState;
            _prevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _prevTopMost = TopMost;
            _fullScreen = true;

            SuspendLayout();
            _topBar.Visible = false;
            _side.Visible = false;
            _statusBar.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            TopMost = true;
            ResumeLayout(true);

            UpdateUiState();
            ShowFsBar();
        }

        private void ExitFullScreen()
        {
            if (!_fullScreen) return;
            _fullScreen = false;

            _fsHideTimer.Stop();
            _fsBar.Visible = false;

            SuspendLayout();
            TopMost = _prevTopMost;
            FormBorderStyle = _prevBorder;
            _topBar.Visible = true;
            _side.Visible = true;
            _statusBar.Visible = true;
            if (_prevState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                WindowState = FormWindowState.Normal;
                Bounds = _prevBounds;
            }
            ResumeLayout(true);

            UpdateUiState();
        }

        private void PositionFsBar()
        {
            if (_fsBar == null || _videoHost == null) return;
            _fsBar.Left = Math.Max(0, (_videoHost.ClientSize.Width - _fsBar.Width) / 2);
            _fsBar.Top = 16;
        }

        private void ShowFsBar()
        {
            if (!_fullScreen) return;
            if (!_fsBar.Visible)
            {
                PositionFsBar();
                _fsBar.Visible = true;
                _fsBar.BringToFront();
            }
            _fsHideTimer.Stop();
            _fsHideTimer.Start();
        }

        private void OnViewMouseMove(object sender, MouseEventArgs e)
        {
            if (_fullScreen) ShowFsBar();
        }

        private void OnFsHideTick(object sender, EventArgs e)
        {
            if (!_fullScreen)
            {
                _fsHideTimer.Stop();
                return;
            }
            Point p = _fsBar.PointToClient(Cursor.Position);
            if (_fsBar.ClientRectangle.Contains(p)) return;     // the mouse rests on the toolbar: keep it visible
            _fsBar.Visible = false;
            _fsHideTimer.Stop();
        }

        // ------------------------------------------------------------------ settings

        private void OnSettingsClick(object sender, EventArgs e)
        {
            using (SettingsForm dialog = new SettingsForm(_settings, BuildDiagnosticsAsync))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings.FfmpegPath = dialog.FfmpegPath;
                _settings.RecordCrf = dialog.Crf;
                _settings.FragmentedMp4 = dialog.FragmentedMp4;
                _settings.AutoStartLive = dialog.AutoStartLive;

                if (dialog.NewBaseFolder != null)
                {
                    string root = AppPaths.ComposeRoot(dialog.NewBaseFolder);
                    string error;
                    if (AppPaths.TryUseRoot(root, true, out error))
                        Toast(Loc.T("msg.rootChanged", root), ToastKind.Success, 5000);
                    else
                        MessageBox.Show(this, Loc.T("msg.rootFailed", error), AppPaths.AppName, MessageBoxButtons.OK,
                            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, RtlOptions());
                }

                bool languageChanged = !string.Equals(dialog.Language, _settings.Language, StringComparison.OrdinalIgnoreCase);
                if (languageChanged)
                {
                    _settings.Language = dialog.Language;
                    Loc.SetLanguage(dialog.Language);
                    ApplyLanguage();
                }

                _settings.Save();
                UpdateFfmpegHint();
            }
        }

        // ------------------------------------------------------------------ diagnostics text

        private async Task<string> BuildDiagnosticsAsync()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Hauppauge Live Capture Studio - diagnostics ===");
            sb.AppendLine("Version : " + typeof(MainForm).Assembly.GetName().Version);
            sb.AppendLine("Time    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("OS      : " + RuntimeInformation.OSDescription + " / " + RuntimeInformation.OSArchitecture);
            sb.AppendLine(".NET    : " + RuntimeInformation.FrameworkDescription);
            sb.AppendLine("Folder  : " + AppPaths.RootDir);

            string ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
            string ffmpegText = await Task.Run(() =>
            {
                if (ffmpeg == null) return "NOT FOUND";
                return ffmpeg + "\n          " + FfmpegLocator.GetVersionLine(ffmpeg)
                       + "\n          H.264 encoder: " + (FfmpegLocator.DetectH264Encoder(ffmpeg) ?? "none");
            });
            sb.AppendLine("FFmpeg  : " + ffmpegText);
            sb.AppendLine();

            sb.AppendLine("Capture devices (" + _devices.Count + "):");
            foreach (CaptureDevice d in _devices)
                sb.AppendLine("  - " + d.Name + "   [" + d.Id + "]");
            sb.AppendLine();

            CaptureDevice selected = SelectedDevice;
            sb.AppendLine("Selected device: " + (selected == null ? "(none)" : selected.Name));
            sb.AppendLine("Formats offered by the driver (" + _caps.Formats.Count + "):");
            foreach (VideoFormat f in _caps.Formats)
                sb.AppendLine("  - " + f.ResolutionText + "  " + f.SubTypeName + "  fps "
                              + VideoFormat.FormatFps(f.MinFps) + ".." + VideoFormat.FormatFps(f.MaxFps)
                              + " (default " + VideoFormat.FormatFps(f.DefaultFps) + ")");
            sb.AppendLine("Inputs offered by the crossbar (" + _caps.Inputs.Count + "):");
            foreach (VideoInput vi in _caps.Inputs)
                sb.AppendLine("  - " + vi.DisplayName + "  (key " + vi.Key + ", connector type " + vi.PhysicalType + ")");
            if (!string.IsNullOrEmpty(_caps.Note)) sb.AppendLine("  note: " + _caps.Note);
            sb.AppendLine();

            if (_live && _info != null)
            {
                sb.AppendLine("LIVE: " + _info.Width + "x" + _info.Height + " @ " + VideoFormat.FormatFps(_info.Fps)
                              + " fps, device format " + _info.SourceFormat + ", input " + _info.InputKey);
                string graph = "";
                try { graph = await _engine.DescribeGraphAsync(); } catch { }
                sb.AppendLine("Graph : " + graph);
            }
            else
            {
                sb.AppendLine("Live view is not running.");
            }
            sb.AppendLine("Frames received: " + _hub.FramesReceived + "   rejected frames: " + _hub.BadFrames
                          + "   display delay (software only): " + _view.LatencyMs.ToString("0.0") + " ms");
            sb.AppendLine();

            sb.AppendLine("--- log (last lines) ---");
            string[] lines = Logger.Snapshot();
            foreach (string line in lines.Skip(Math.Max(0, lines.Length - 80)))
                sb.AppendLine(line);
            return sb.ToString();
        }
    }
}
