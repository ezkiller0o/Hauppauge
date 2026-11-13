using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Core;
using HauppaugeLiveCaptureStudio.DirectShow;
using HauppaugeLiveCaptureStudio.Media;

namespace HauppaugeLiveCaptureStudio.UI
{
    /// <summary>
    /// Main window. This file: state, start-up/shutdown, device handling and the live view.
    /// MainForm.Layout.cs builds the controls; MainForm.Actions.cs has snapshot, recording, full screen and settings.
    /// </summary>
    internal sealed partial class MainForm : Form
    {
        private const int WM_DEVICECHANGE = 0x0219;

        private readonly AppSettings _settings;
        private readonly FrameHub _hub = new FrameHub();
        private readonly CaptureEngine _engine;
        private readonly FfmpegRecorder _recorder;
        private readonly List<Action> _applyText = new List<Action>();
        private readonly System.Windows.Forms.Timer _uiTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _deviceChangeTimer = new System.Windows.Forms.Timer();

        private List<CaptureDevice> _devices = new List<CaptureDevice>();
        private DeviceCaps _caps = new DeviceCaps();
        private StartInfo _info;

        private bool _live;
        private bool _busy;                 // starting / stopping the live view
        private bool _recBusy;              // starting / stopping a recording
        private bool _stalled;
        private bool _lostHandled;
        private bool _updatingUi;           // true while combo boxes are being filled (suppresses their events)
        private bool _shutdownStarted;
        private bool _shutdownDone;
        private string _lastSavedPath;
        private long _lastFrames;
        private long _lastFpsStamp;
        private double _measuredFps;
        private int _pollCounter;

        public MainForm(AppSettings settings)
        {
            _settings = settings;
            _engine = new CaptureEngine(_hub);
            _recorder = new FfmpegRecorder(_hub);

            BuildLayout();

            AspectMode mode;
            if (!Enum.TryParse(_settings.Aspect, out mode)) mode = AspectMode.Auto;
            _view.Aspect = mode;
            _txtSnapName.Text = string.IsNullOrWhiteSpace(_settings.SnapshotName) ? "Snapshot" : _settings.SnapshotName;
            _txtVideoName.Text = string.IsNullOrWhiteSpace(_settings.VideoName) ? "Recording" : _settings.VideoName;
            _chkAppendDate.Checked = _settings.AppendDateTime;

            ApplyLanguage();

            _hub.AddSink(_view.Present);
            _engine.DeviceLost += OnEngineDeviceLost;
            _engine.StallChanged += OnEngineStallChanged;
            _recorder.Failed += OnRecorderFailed;

            _uiTimer.Interval = 250;
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();

            _deviceChangeTimer.Interval = 800;
            _deviceChangeTimer.Tick += delegate
            {
                _deviceChangeTimer.Stop();
                if (!_busy && !_shutdownStarted) { var ignored = RefreshDevicesAsync(); }
            };
        }

        // ------------------------------------------------------------------ start-up and shutdown

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                UpdateFfmpegHint();
                await RefreshDevicesAsync();
                if (SelectedDevice != null) await ProbeSelectedAsync();
                if (_settings.AutoStartLive && SelectedDevice != null) await StartLiveAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Start-up failed", ex);
            }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_shutdownDone)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;            // we close ourselves once recording and capture are stopped cleanly
            if (_shutdownStarted) return;

            if (_recorder.IsRecording)
            {
                DialogResult answer = MessageBox.Show(this, Loc.T("msg.quitRecording"), AppPaths.AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, RtlOptions());
                if (answer != DialogResult.Yes) return;
            }

            _shutdownStarted = true;
            UseWaitCursor = true;
            try
            {
                _uiTimer.Stop();
                if (_recorder.IsRecording) await Task.Run(() => _recorder.Stop());
                await _engine.StopAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn("Shutdown: " + ex.Message);
            }

            SaveUiToSettings();
            _settings.Save();
            try { _engine.Dispose(); } catch { }

            _shutdownDone = true;
            Close();
        }

        private void SaveUiToSettings()
        {
            _settings.SnapshotName = _txtSnapName.Text;
            _settings.VideoName = _txtVideoName.Text;
            _settings.AppendDateTime = _chkAppendDate.Checked;
            ListItem fmt = _cmbSnapFormat.SelectedItem as ListItem;
            if (fmt != null) _settings.SnapshotFormat = (string)fmt.Value;
            _settings.Aspect = _view.Aspect.ToString();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && !_shutdownStarted)
            {
                _deviceChangeTimer.Stop();      // USB events come in bursts: wait until they settle
                _deviceChangeTimer.Start();
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullScreen();
                return true;
            }
            if (keyData == Keys.Escape && _fullScreen)
            {
                ExitFullScreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private MessageBoxOptions RtlOptions()
        {
            return Loc.IsArabic ? (MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) : (MessageBoxOptions)0;
        }

        // ------------------------------------------------------------------ selections

        private CaptureDevice SelectedDevice
        {
            get
            {
                ListItem it = _cmbDevice.SelectedItem as ListItem;
                return it == null ? null : it.Value as CaptureDevice;
            }
        }

        private VideoFormat SelectedFormat
        {
            get
            {
                ListItem it = _cmbResolution.SelectedItem as ListItem;
                return it == null ? null : it.Value as VideoFormat;
            }
        }

        private double SelectedFps
        {
            get
            {
                ListItem it = _cmbFps.SelectedItem as ListItem;
                return (it != null && it.Value is double) ? (double)it.Value : 0;
            }
        }

        private string SelectedInputKey
        {
            get
            {
                ListItem it = _cmbSource.SelectedItem as ListItem;
                VideoInput vi = it == null ? null : it.Value as VideoInput;
                return vi == null ? "" : vi.Key;
            }
        }

        // ------------------------------------------------------------------ devices and capabilities

        private async Task RefreshDevicesAsync()
        {
            List<CaptureDevice> list;
            try
            {
                list = await _engine.EnumerateDevicesAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Device refresh failed", ex);
                list = new List<CaptureDevice>();
            }

            list = list.OrderByDescending(d => d.LooksLikeHauppauge).ToList();

            bool same = list.Count == _devices.Count;
            for (int i = 0; same && i < list.Count; i++)
                same = list[i].Id == _devices[i].Id && list[i].Name == _devices[i].Name;

            if (!same)
            {
                string previous = SelectedDevice != null ? SelectedDevice.Id : _settings.LastDeviceId;
                _devices = list;
                FillDeviceCombo(previous);
                Logger.Info("Capture devices: " + (list.Count == 0 ? "none" : string.Join(" | ", list.Select(d => d.Name))));

                // a new device became selected while not live -> read its formats
                if (!_live && !_busy && SelectedDevice != null) await ProbeSelectedAsync();
                else if (SelectedDevice == null) { _caps = new DeviceCaps(); PopulateVideoCombos(); }
            }

            UpdateStatusLed();
            UpdateUiState();
        }

        private void FillDeviceCombo(string preferredId)
        {
            _updatingUi = true;
            try
            {
                _cmbDevice.Items.Clear();
                int select = -1;
                for (int i = 0; i < _devices.Count; i++)
                {
                    _cmbDevice.Items.Add(new ListItem(_devices[i].Name, _devices[i]));
                    if (select < 0 && !string.IsNullOrEmpty(preferredId) && _devices[i].Id == preferredId) select = i;
                }
                if (_devices.Count == 0)
                {
                    _cmbDevice.Items.Add(new ListItem(Loc.T("dev.none"), null));
                    select = 0;
                }
                if (select < 0) select = 0;     // list is sorted: a Hauppauge device (if any) is first
                _cmbDevice.SelectedIndex = select;
            }
            finally
            {
                _updatingUi = false;
            }

            _view.Placeholder = _devices.Count == 0 ? Loc.T("view.placeholderNoDevice") : Loc.T("view.placeholder");
        }

        private async Task ProbeSelectedAsync()
        {
            CaptureDevice dev = SelectedDevice;
            _caps = new DeviceCaps();
            if (dev == null)
            {
                PopulateVideoCombos();
                return;
            }

            SetMessage(Loc.T("msg.reading"), false);
            try
            {
                _caps = await _engine.ProbeAsync(dev);
                SetMessage(Loc.T("msg.ready"), false);
            }
            catch (Exception ex)
            {
                Logger.Warn("Probe failed: " + ex.Message);
                _caps = new DeviceCaps();
                SetMessage(ex.Message, true);
            }
            PopulateVideoCombos();
            UpdateUiState();
        }

        private void PopulateVideoCombos()
        {
            _updatingUi = true;
            try
            {
                // ---- sources (Composite / S-Video ...)
                _cmbSource.Items.Clear();
                int sourceIndex = -1;
                for (int i = 0; i < _caps.Inputs.Count; i++)
                {
                    VideoInput vi = _caps.Inputs[i];
                    _cmbSource.Items.Add(new ListItem(InputText(vi), vi));
                    if (sourceIndex < 0 && string.Equals(vi.Key, _settings.LastInputKey, StringComparison.OrdinalIgnoreCase)) sourceIndex = i;
                }
                if (_caps.Inputs.Count == 0)
                {
                    _cmbSource.Items.Add(new ListItem(Loc.T("src.none"), null));
                    sourceIndex = 0;
                }
                if (sourceIndex < 0)
                {
                    for (int i = 0; i < _caps.Inputs.Count; i++)
                        if (string.Equals(_caps.Inputs[i].Key, _caps.ActiveInputKey, StringComparison.OrdinalIgnoreCase)) { sourceIndex = i; break; }
                }
                _cmbSource.SelectedIndex = sourceIndex < 0 ? 0 : sourceIndex;

                // ---- resolutions: one entry per width x height (best pixel format first)
                _cmbResolution.Items.Clear();
                Dictionary<string, VideoFormat> best = new Dictionary<string, VideoFormat>();
                foreach (VideoFormat f in _caps.Formats)
                {
                    VideoFormat current;
                    if (!best.TryGetValue(f.ResolutionText, out current)
                        || DsUtil.SubtypeRank(f.SubTypeName) < DsUtil.SubtypeRank(current.SubTypeName))
                        best[f.ResolutionText] = f;
                }
                List<VideoFormat> ordered = best.Values.OrderByDescending(f => f.Width * f.Height).ToList();

                int resIndex = -1;
                for (int i = 0; i < ordered.Count; i++)
                {
                    VideoFormat f = ordered[i];
                    _cmbResolution.Items.Add(new ListItem(f.Width + " × " + f.Height, f));
                    if (resIndex < 0 && f.Width == _settings.Width && f.Height == _settings.Height) resIndex = i;
                }
                if (resIndex < 0)
                {
                    for (int i = 0; i < ordered.Count; i++)
                        if (ordered[i].Width == 720 && (ordered[i].Height == 480 || ordered[i].Height == 576)) { resIndex = i; break; }
                }
                if (ordered.Count == 0)
                {
                    _cmbResolution.Items.Add(new ListItem(Loc.T("res.none"), null));
                    resIndex = 0;
                }
                _cmbResolution.SelectedIndex = resIndex < 0 ? 0 : resIndex;
            }
            finally
            {
                _updatingUi = false;
            }
            PopulateFpsCombo();
        }

        private void PopulateFpsCombo()
        {
            _updatingUi = true;
            try
            {
                _cmbFps.Items.Clear();
                VideoFormat fmt = SelectedFormat;
                if (fmt == null)
                {
                    _cmbFps.Items.Add(new ListItem(Loc.T("res.none"), null));
                    _cmbFps.SelectedIndex = 0;
                    return;
                }

                List<double> options = fmt.GetFpsOptions();
                int index = -1;
                for (int i = 0; i < options.Count; i++)
                {
                    _cmbFps.Items.Add(new ListItem(VideoFormat.FormatFps(options[i]), options[i]));
                    if (index < 0 && _settings.Fps > 0 && Math.Abs(options[i] - _settings.Fps) < 0.02) index = i;
                }
                if (index < 0)
                {
                    double target = fmt.DefaultFps;
                    for (int i = 0; i < options.Count; i++)
                        if (target > 0 && Math.Abs(options[i] - target) < 0.02) { index = i; break; }
                }
                _cmbFps.SelectedIndex = index < 0 ? 0 : index;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private static string InputText(VideoInput vi)
        {
            switch (vi.Key)
            {
                case "Composite": return Loc.T("src.composite");
                case "SVideo": return Loc.T("src.svideo");
                default: return vi.DisplayName;
            }
        }

        // ---- combo box events

        private async void OnDeviceComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            try
            {
                if (_recorder.IsRecording || _recBusy)
                {
                    Toast(Loc.T("msg.lockedRec"), ToastKind.Warning, 4000);
                    FillDeviceCombo(_settings.LastDeviceId);
                    return;
                }

                if (_live) await StopLiveAsync();
                await ProbeSelectedAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Device change failed", ex);
            }
        }

        private async void OnSourceComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            string key = SelectedInputKey;
            if (key.Length == 0) return;
            _settings.LastInputKey = key;
            if (!_live) return;
            try
            {
                bool ok = await _engine.SelectInputAsync(key);
                if (!ok) Toast(Loc.T("msg.startFailed"), ToastKind.Warning, 3000);
            }
            catch (Exception ex)
            {
                Logger.Error("Input change failed", ex);
            }
        }

        private async void OnResolutionComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            if (!CanChangeFormatNow()) return;
            PopulateFpsCombo();
            if (_live) await RestartLiveAsync();
        }

        private async void OnFpsComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            if (!CanChangeFormatNow()) return;
            if (_live) await RestartLiveAsync();
        }

        /// <summary>A format change restarts the capture graph, so it is refused while a recording is active.</summary>
        private bool CanChangeFormatNow()
        {
            if (!_recorder.IsRecording && !_recBusy) return true;
            Toast(Loc.T("msg.lockedRec"), ToastKind.Warning, 4000);
            if (_info != null)
            {
                _settings.Width = _info.Width;
                _settings.Height = _info.Height;
                _settings.Fps = _info.Fps;
                PopulateVideoCombos();
            }
            return false;
        }

        private async Task RestartLiveAsync()
        {
            await StopLiveAsync();
            await StartLiveAsync();
        }

        // ------------------------------------------------------------------ live view

        private async Task StartLiveAsync()
        {
            if (_busy || _live) return;

            CaptureDevice dev = SelectedDevice;
            if (dev == null)
            {
                Toast(Loc.T("msg.noDevice"), ToastKind.Warning, 4000);
                return;
            }

            VideoFormat fmt = SelectedFormat;
            double fps = SelectedFps;
            string inputKey = SelectedInputKey;

            _busy = true;
            UpdateUiState();
            SetMessage(Loc.T("msg.starting"), false);
            try
            {
                StartInfo info = await _engine.StartAsync(dev, fmt == null ? 0 : fmt.Width, fmt == null ? 0 : fmt.Height, fps, inputKey);

                _info = info;
                _live = true;
                _stalled = false;
                _lostHandled = false;
                _lastFrames = _hub.FramesReceived;
                _lastFpsStamp = Stopwatch.GetTimestamp();
                _measuredFps = 0;

                _settings.LastDeviceId = dev.Id;
                _settings.LastDeviceName = dev.Name;
                _settings.Width = info.Width;
                _settings.Height = info.Height;
                _settings.Fps = info.Fps;
                if (!string.IsNullOrEmpty(info.InputKey)) _settings.LastInputKey = info.InputKey;
                _settings.Save();

                if (_caps.Formats.Count == 0 && info.Caps != null && info.Caps.Formats.Count > 0)
                {
                    _caps = info.Caps;
                    PopulateVideoCombos();
                }
                _view.Placeholder = "";
                Toast(Loc.T("msg.liveStarted"), ToastKind.Success, 2500);
            }
            catch (Exception ex)
            {
                Logger.Error("Start live failed", ex);
                _live = false;
                SetMessage(Loc.T("msg.startFailed"), true);
                MessageBox.Show(this, Loc.T("msg.startFailed") + "\n\n" + ex.Message, AppPaths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, RtlOptions());
            }
            finally
            {
                _busy = false;
                UpdateStatusLed();
                UpdateUiState();
            }
        }

        private async Task StopLiveAsync()
        {
            if (_busy || !_live) return;

            _busy = true;
            UpdateUiState();
            SetMessage(Loc.T("msg.stopping"), false);
            try
            {
                // let a recording that is just starting or stopping finish first, then close it properly before the graph stops
                for (int i = 0; i < 100 && _recBusy; i++) await Task.Delay(100);
                if (_recorder.IsRecording) await StopRecordingAsync();
                await _engine.StopAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Stop live failed", ex);
            }
            finally
            {
                _live = false;
                _stalled = false;
                _info = null;
                _busy = false;
                _view.ClearFrame();
                _view.Placeholder = _devices.Count == 0 ? Loc.T("view.placeholderNoDevice") : Loc.T("view.placeholder");
                SetMessage(Loc.T("msg.liveStopped"), false);
                UpdateStatusLed();
                UpdateUiState();
            }
        }

        // ------------------------------------------------------------------ device lost / stalled

        private void OnEngineDeviceLost(string reason)
        {
            if (IsDisposed || _shutdownStarted) return;
            try { BeginInvoke(new Action(delegate { HandleDeviceLost(reason); })); }
            catch (InvalidOperationException) { }
        }

        private void OnEngineStallChanged(bool stalled)
        {
            if (IsDisposed || _shutdownStarted) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!_live) return;
                    _stalled = stalled;
                    UpdateStatusLed();
                    Toast(stalled ? Loc.T("msg.stalled") : Loc.T("msg.resumed"), stalled ? ToastKind.Warning : ToastKind.Success, stalled ? 6000 : 2500);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private async void HandleDeviceLost(string reason)
        {
            if (_lostHandled) return;
            _lostHandled = true;
            _busy = true;
            UpdateUiState();

            string savedPath = null;
            try
            {
                if (_recorder.IsRecording)
                {
                    RecorderResult r = await Task.Run(() => _recorder.Stop());
                    if (r.Path != null && System.IO.File.Exists(r.Path)) savedPath = r.Path;
                }
                await _engine.StopAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Cleanup after device loss failed", ex);
            }

            _live = false;
            _stalled = false;
            _info = null;
            _busy = false;
            _view.ClearFrame();
            _view.Placeholder = Loc.T("view.placeholder");
            _led.LedColor = Theme.Red;
            _lblStatus.Text = Loc.T("led.lost");
            UpdateUiState();

            string text = Loc.T("msg.deviceLost") + "\n(" + reason + ")";
            if (savedPath != null) text += "\n\n" + Loc.T("msg.deviceLostRec", savedPath);
            text += "\n\n" + Loc.T("msg.deviceLostHint");
            SetMessage(Loc.T("msg.deviceLost"), true);

            if (_fullScreen) ExitFullScreen();
            MessageBox.Show(this, text, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1, RtlOptions());

            try { await RefreshDevicesAsync(); } catch { }
        }

        // ------------------------------------------------------------------ periodic UI update

        private void OnUiTick(object sender, EventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            _view.ResetPaintRequest();      // safety net: a lost paint request can never freeze the preview for more than 250 ms

            if (_live && _info != null)
            {
                double dt = (now - _lastFpsStamp) / (double)Stopwatch.Frequency;
                if (dt >= 1.0)
                {
                    long frames = _hub.FramesReceived;
                    _measuredFps = (frames - _lastFrames) / dt;
                    _lastFrames = frames;
                    _lastFpsStamp = now;
                }

                string head = _info.Width + "x" + _info.Height + " @ " + VideoFormat.FormatFps(_info.Fps) + " fps"
                              + (string.IsNullOrEmpty(_info.SourceFormat) ? "" : " (" + _info.SourceFormat + ")");
                long dropped = _recorder.IsRecording ? _recorder.DroppedFrames : 0;
                string stats = Loc.T("info.stats", _measuredFps.ToString("0.0", CultureInfo.InvariantCulture), dropped,
                    _view.LatencyMs.ToString("0", CultureInfo.InvariantCulture));
                _lblInfo.Text = head + "    " + stats;
            }
            else
            {
                _lblInfo.Text = "";
            }

            if (_recorder.IsRecording)
            {
                string t = FormatDuration(_recorder.Elapsed);
                _lblRecTimer.Text = t;
                _fsTimerLabel.Text = "● " + t;
                _view.RecordingText = "REC  " + t;
            }
            else if (_view.RecordingText != null)
            {
                _view.RecordingText = null;
            }

            // slow fallback for hot-plug detection when no WM_DEVICECHANGE arrives
            if (!_live && !_busy && !_recBusy && ++_pollCounter % 16 == 0)
            {
                var ignored = RefreshDevicesAsync();
            }
        }

        private static string FormatDuration(TimeSpan t)
        {
            return ((int)t.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   t.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ small UI helpers

        private void SetMessage(string text, bool error)
        {
            _lblMsg.Text = text;
            _lblMsg.ForeColor = error ? Color.FromArgb(255, 130, 130) : Theme.BarDim;
        }

        private void Toast(string text, ToastKind kind, int milliseconds)
        {
            _view.ShowToast(text, kind, milliseconds);
            SetMessage(text, kind == ToastKind.Error);
        }

        private void UpdateStatusLed()
        {
            CaptureDevice dev = SelectedDevice;
            if (_live && _info != null)
            {
                if (_stalled)
                {
                    _led.LedColor = Theme.Amber;
                    _lblStatus.Text = Loc.T("led.stalled");
                }
                else
                {
                    _led.LedColor = Theme.Green;
                    _lblStatus.Text = Loc.T("led.live", _info.DeviceName);
                }
            }
            else if (dev != null)
            {
                _led.LedColor = Theme.Green;
                _lblStatus.Text = Loc.T("led.ready", dev.Name);
            }
            else
            {
                _led.LedColor = Theme.Red;
                _lblStatus.Text = Loc.T("led.notfound");
            }
        }

        private void UpdateFfmpegHint()
        {
            string path = FfmpegLocator.Find(_settings.FfmpegPath);
            if (path != null)
            {
                _lblFfmpeg.Text = Loc.T("ffmpeg.ok");
                _lblFfmpeg.ForeColor = Theme.SideDim;
            }
            else
            {
                _lblFfmpeg.Text = Loc.T("ffmpeg.missing");
                _lblFfmpeg.ForeColor = Theme.Amber;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _uiTimer.Dispose();
                _deviceChangeTimer.Dispose();
                _fsHideTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
