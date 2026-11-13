using System;
using System.Drawing;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Core;

namespace HauppaugeLiveCaptureStudio.UI
{
    internal sealed partial class MainForm
    {
        // top bar
        private Panel _topBar;
        private Label _lblTitle;
        private StatusLed _led;
        private Label _lblStatus;
        private FlatButton _btnLang;
        private FlatButton _btnSettings;

        // video area + full-screen toolbar
        private Panel _videoHost;
        private VideoView _view;
        private FlowLayoutPanel _fsBar;
        private FlatButton _fsExit;
        private FlatButton _fsSnap;
        private FlatButton _fsRec;
        private Label _fsTimerLabel;
        private readonly System.Windows.Forms.Timer _fsHideTimer = new System.Windows.Forms.Timer();

        // side panel
        private Panel _side;
        private TableLayoutPanel _flow;
        private ComboBox _cmbDevice;
        private ComboBox _cmbSource;
        private ComboBox _cmbResolution;
        private ComboBox _cmbFps;
        private ComboBox _cmbAspect;
        private ComboBox _cmbSnapFormat;
        private FlatButton _btnRefresh;
        private FlatButton _btnStartLive;
        private FlatButton _btnStopLive;
        private FlatButton _btnSnapshot;
        private FlatButton _btnStartRec;
        private FlatButton _btnStopRec;
        private FlatButton _btnFull;
        private FlatButton _btnExitFull;
        private TextBox _txtSnapName;
        private TextBox _txtVideoName;
        private CheckBox _chkAppendDate;
        private StatusLed _recDot;
        private Label _lblRecTimer;
        private Label _lblFfmpeg;

        // status bar
        private Panel _statusBar;
        private Label _lblMsg;
        private Label _lblInfo;

        // ------------------------------------------------------------------ construction

        private void BuildLayout()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = AppPaths.AppName;
            Font = Theme.UiFont;
            BackColor = Theme.Stage;
            ForeColor = Theme.SideText;
            MinimumSize = new Size(1040, 700);
            ClientSize = new Size(1280, 760);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

            BuildVideoArea();
            BuildSidePanel();
            BuildStatusBar();
            BuildTopBar();

            // docking order: the Fill control first, then the edge controls (the last one added is docked first)
            Controls.Add(_videoHost);
            Controls.Add(_side);
            Controls.Add(_statusBar);
            Controls.Add(_topBar);

            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildTopBar()
        {
            _topBar = new Panel();
            _topBar.Dock = DockStyle.Top;
            _topBar.Height = 60;
            _topBar.BackColor = Theme.Bar;
            _topBar.Padding = new Padding(16, 0, 16, 0);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 4;
            t.RowCount = 1;
            t.BackColor = Theme.Bar;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _lblTitle = new Label();
            _lblTitle.AutoSize = true;
            _lblTitle.Font = Theme.TitleFont;
            _lblTitle.ForeColor = Theme.BarText;
            _lblTitle.Anchor = AnchorStyles.Left;
            _lblTitle.Margin = new Padding(0, 0, 28, 0);
            Bind(_lblTitle, "app.title");

            FlowLayoutPanel status = new FlowLayoutPanel();
            status.FlowDirection = FlowDirection.LeftToRight;
            status.WrapContents = false;
            status.AutoSize = true;
            status.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            status.Anchor = AnchorStyles.Left;
            status.BackColor = Theme.Bar;
            status.Margin = new Padding(0);

            _led = new StatusLed();
            _led.Margin = new Padding(0, 4, 8, 0);
            _led.LedColor = Theme.Red;

            _lblStatus = new Label();
            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Theme.BarText;
            _lblStatus.Margin = new Padding(0, 3, 0, 0);

            status.Controls.Add(_led);
            status.Controls.Add(_lblStatus);

            _btnLang = MakeButton("lang.button", Theme.Gray, 112, 34);
            _btnLang.Anchor = AnchorStyles.Right;
            _btnLang.Margin = new Padding(8, 0, 8, 0);
            _btnLang.Click += OnLanguageClick;

            _btnSettings = MakeButton("btn.settings", Theme.Gray, 112, 34);
            _btnSettings.Anchor = AnchorStyles.Right;
            _btnSettings.Margin = new Padding(0);
            _btnSettings.Click += OnSettingsClick;

            t.Controls.Add(_lblTitle, 0, 0);
            t.Controls.Add(status, 1, 0);
            t.Controls.Add(_btnLang, 2, 0);
            t.Controls.Add(_btnSettings, 3, 0);
            _topBar.Controls.Add(t);
        }

        private void BuildStatusBar()
        {
            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 32;
            _statusBar.BackColor = Theme.Bar;
            _statusBar.Padding = new Padding(14, 0, 14, 0);

            _lblInfo = new Label();
            _lblInfo.Dock = DockStyle.Right;
            _lblInfo.AutoSize = true;
            _lblInfo.Font = Theme.SmallFont;
            _lblInfo.ForeColor = Theme.BarText;
            _lblInfo.TextAlign = ContentAlignment.MiddleLeft;

            _lblMsg = new Label();
            _lblMsg.Dock = DockStyle.Fill;
            _lblMsg.Font = Theme.SmallFont;
            _lblMsg.ForeColor = Theme.BarDim;
            _lblMsg.TextAlign = ContentAlignment.MiddleLeft;
            _lblMsg.AutoEllipsis = true;
            _lblMsg.Cursor = Cursors.Hand;
            _lblMsg.Click += OnMessageClick;

            _statusBar.Controls.Add(_lblMsg);
            _statusBar.Controls.Add(_lblInfo);
        }

        private void BuildVideoArea()
        {
            _videoHost = new Panel();
            _videoHost.Dock = DockStyle.Fill;
            _videoHost.BackColor = Theme.Stage;

            _view = new VideoView();
            _view.Dock = DockStyle.Fill;
            _view.MouseMove += OnViewMouseMove;
            _view.DoubleClick += delegate { ToggleFullScreen(); };
            _videoHost.Controls.Add(_view);

            // floating toolbar shown in full-screen mode when the mouse moves
            _fsBar = new FlowLayoutPanel();
            _fsBar.FlowDirection = FlowDirection.LeftToRight;
            _fsBar.WrapContents = false;
            _fsBar.AutoSize = true;
            _fsBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _fsBar.BackColor = Theme.Bar;
            _fsBar.Padding = new Padding(10);
            _fsBar.Visible = false;
            _fsBar.MouseMove += OnViewMouseMove;

            _fsExit = MakeButton("btn.exitFull", Theme.Gray, 196, 40);
            _fsExit.Margin = new Padding(0, 0, 8, 0);
            _fsExit.Click += OnExitFullClick;

            _fsSnap = MakeButton("btn.snapshot", Theme.Accent, 150, 40);
            _fsSnap.Margin = new Padding(0, 0, 8, 0);
            _fsSnap.Click += OnSnapshotClick;

            _fsRec = MakeButton(null, Theme.Red, 210, 40);
            _fsRec.Margin = new Padding(0, 0, 8, 0);
            _fsRec.Click += OnFsRecClick;

            _fsTimerLabel = new Label();
            _fsTimerLabel.AutoSize = true;
            _fsTimerLabel.Font = Theme.TimerFont;
            _fsTimerLabel.ForeColor = Color.White;
            _fsTimerLabel.Margin = new Padding(8, 6, 6, 0);

            _fsBar.Controls.Add(_fsExit);
            _fsBar.Controls.Add(_fsSnap);
            _fsBar.Controls.Add(_fsRec);
            _fsBar.Controls.Add(_fsTimerLabel);
            _videoHost.Controls.Add(_fsBar);
            _fsBar.BringToFront();

            _videoHost.Resize += delegate { PositionFsBar(); };
            _fsBar.SizeChanged += delegate { PositionFsBar(); };
            _fsHideTimer.Interval = 2500;
            _fsHideTimer.Tick += OnFsHideTick;
        }

        private void BuildSidePanel()
        {
            _side = new Panel();
            _side.Dock = DockStyle.Right;
            _side.Width = 372;
            _side.BackColor = Theme.Side;
            _side.AutoScroll = true;
            _side.Padding = new Padding(16, 6, 16, 12);

            _flow = new TableLayoutPanel();
            _flow.Dock = DockStyle.Top;
            _flow.AutoSize = true;
            _flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _flow.ColumnCount = 1;
            _flow.BackColor = Theme.Side;
            _flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _side.Controls.Add(_flow);

            // ---- capture device
            AddSection("sec.device");
            _cmbDevice = MakeCombo();
            _cmbDevice.SelectedIndexChanged += OnDeviceComboChanged;
            _btnRefresh = MakeButton("btn.refresh", Theme.Gray, 92, 28);
            _btnRefresh.Click += OnRefreshClick;
            AddRow(TwoColumns(_cmbDevice, _btnRefresh, 92));

            // ---- video settings
            AddSection("sec.video");
            AddCaption("lbl.source");
            _cmbSource = MakeCombo();
            _cmbSource.SelectedIndexChanged += OnSourceComboChanged;
            AddRow(_cmbSource);

            AddCaption("lbl.resolution");
            _cmbResolution = MakeCombo();
            _cmbResolution.SelectedIndexChanged += OnResolutionComboChanged;
            AddRow(_cmbResolution);

            AddCaption("lbl.fps");
            _cmbFps = MakeCombo();
            _cmbFps.SelectedIndexChanged += OnFpsComboChanged;
            AddRow(_cmbFps);

            AddCaption("lbl.aspect");
            _cmbAspect = MakeCombo();
            _cmbAspect.SelectedIndexChanged += OnAspectComboChanged;
            AddRow(_cmbAspect);

            // ---- live view
            AddSection("sec.live");
            _btnStartLive = MakeButton("btn.startLive", Theme.Green, 100, 44);
            _btnStartLive.Click += OnStartLiveClick;
            _btnStopLive = MakeButton("btn.stopLive", Theme.Gray, 100, 44);
            _btnStopLive.Click += OnStopLiveClick;
            AddRow(EqualColumns(_btnStartLive, _btnStopLive));

            // ---- snapshot
            AddSection("sec.snapshot");
            AddCaption("lbl.snapName");
            _txtSnapName = MakeText();
            _cmbSnapFormat = MakeCombo();
            _cmbSnapFormat.Items.Add(new ListItem("JPG", "JPG"));
            _cmbSnapFormat.Items.Add(new ListItem("PNG", "PNG"));
            _cmbSnapFormat.SelectedIndex = string.Equals(_settings.SnapshotFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            AddRow(TwoColumns(_txtSnapName, _cmbSnapFormat, 92));
            _btnSnapshot = MakeButton("btn.snapshot", Theme.Accent, 100, 44);
            _btnSnapshot.Click += OnSnapshotClick;
            AddRow(_btnSnapshot);

            // ---- recording
            AddSection("sec.record");
            AddCaption("lbl.vidName");
            _txtVideoName = MakeText();
            AddRow(_txtVideoName);

            _chkAppendDate = new CheckBox();
            _chkAppendDate.AutoSize = true;
            _chkAppendDate.ForeColor = Theme.SideText;
            _chkAppendDate.Margin = new Padding(0, 0, 0, 10);
            Bind(_chkAppendDate, "chk.appendDate");
            AddRow(_chkAppendDate);

            _btnStartRec = MakeButton("btn.startRec", Theme.Red, 100, 44);
            _btnStartRec.Click += OnStartRecClick;
            _btnStopRec = MakeButton("btn.stopRec", Theme.Gray, 100, 44);
            _btnStopRec.Click += OnStopRecClick;
            AddRow(EqualColumns(_btnStartRec, _btnStopRec));

            FlowLayoutPanel rec = new FlowLayoutPanel();
            rec.FlowDirection = FlowDirection.LeftToRight;
            rec.WrapContents = false;
            rec.AutoSize = true;
            rec.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rec.BackColor = Theme.Side;
            rec.Margin = new Padding(0, 4, 0, 0);
            _recDot = new StatusLed();
            _recDot.LedColor = Theme.DisabledFill;
            _recDot.Margin = new Padding(0, 8, 8, 0);
            _lblRecTimer = new Label();
            _lblRecTimer.AutoSize = true;
            _lblRecTimer.Font = Theme.TimerFont;
            _lblRecTimer.ForeColor = Theme.SideDim;
            _lblRecTimer.Text = "00:00:00";
            _lblRecTimer.Margin = new Padding(0);
            rec.Controls.Add(_recDot);
            rec.Controls.Add(_lblRecTimer);
            AddRow(rec);

            _lblFfmpeg = new Label();
            _lblFfmpeg.AutoSize = true;
            _lblFfmpeg.MaximumSize = new Size(330, 0);
            _lblFfmpeg.Font = Theme.SmallFont;
            _lblFfmpeg.ForeColor = Theme.SideDim;
            _lblFfmpeg.Margin = new Padding(0, 2, 0, 0);
            AddRow(_lblFfmpeg);

            // ---- full screen
            _btnFull = MakeButton("btn.full", Theme.Accent, 100, 40);
            _btnFull.Click += OnFullClick;
            _btnExitFull = MakeButton("btn.exitFull", Theme.Gray, 100, 40);
            _btnExitFull.Click += OnExitFullClick;
            Control fs = EqualColumns(_btnFull, _btnExitFull);
            fs.Margin = new Padding(0, 18, 0, 0);
            AddRow(fs);
        }

        // ------------------------------------------------------------------ small construction helpers

        private void Bind(Control c, string key)
        {
            c.Text = Loc.T(key);
            _applyText.Add(delegate { c.Text = Loc.T(key); });
        }

        private FlatButton MakeButton(string key, Color color, int width, int height)
        {
            FlatButton b = new FlatButton();
            b.BaseColor = color;
            b.Width = width;
            b.Height = height;
            b.Margin = new Padding(0);
            if (key != null) Bind(b, key);
            return b;
        }

        private ComboBox MakeCombo()
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Standard;
            c.Font = Theme.UiFont;
            c.Margin = new Padding(0, 0, 0, 8);
            c.IntegralHeight = false;
            return c;
        }

        private TextBox MakeText()
        {
            TextBox t = new TextBox();
            t.Font = Theme.UiFont;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.MaxLength = 100;
            t.Margin = new Padding(0, 0, 0, 8);
            return t;
        }

        private void AddSection(string key)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Font = Theme.SectionFont;
            l.ForeColor = Theme.SideDim;
            l.Margin = new Padding(0, 16, 0, 6);
            Bind(l, key);
            AddRow(l);
        }

        private void AddCaption(string key)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.ForeColor = Theme.SideText;
            l.Margin = new Padding(0, 2, 0, 3);
            Bind(l, key);
            AddRow(l);
        }

        private void AddRow(Control c)
        {
            int row = _flow.RowCount;
            _flow.RowCount = row + 1;
            _flow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            _flow.Controls.Add(c, 0, row);
        }

        private TableLayoutPanel TwoColumns(Control first, Control second, int secondWidth)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.BackColor = Theme.Side;
            t.Margin = new Padding(0);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, secondWidth));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            first.Dock = DockStyle.Fill;
            first.Margin = new Padding(0, 0, 8, 8);
            second.Dock = DockStyle.Top;
            second.Margin = new Padding(0, 0, 0, 8);
            t.Controls.Add(first, 0, 0);
            t.Controls.Add(second, 1, 0);
            return t;
        }

        private TableLayoutPanel EqualColumns(Control first, Control second)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.BackColor = Theme.Side;
            t.Margin = new Padding(0, 0, 0, 6);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            first.Dock = DockStyle.Fill;
            first.Margin = new Padding(0, 0, 4, 0);
            second.Dock = DockStyle.Fill;
            second.Margin = new Padding(4, 0, 0, 0);
            t.Controls.Add(first, 0, 0);
            t.Controls.Add(second, 1, 0);
            return t;
        }

        // ------------------------------------------------------------------ language and state

        private void ApplyLanguage()
        {
            bool rtl = Loc.IsArabic;
            SuspendLayout();

            RightToLeft target = rtl ? RightToLeft.Yes : RightToLeft.No;
            if (RightToLeft != target) RightToLeft = target;
            if (RightToLeftLayout != rtl) RightToLeftLayout = rtl;

            foreach (Action apply in _applyText) apply();

            string previous = SelectedDevice != null ? SelectedDevice.Id : _settings.LastDeviceId;
            FillDeviceCombo(previous);
            PopulateVideoCombos();
            FillAspectCombo();
            UpdateFfmpegHint();
            UpdateStatusLed();
            UpdateUiState();

            ResumeLayout(true);
        }

        private void FillAspectCombo()
        {
            AspectMode current = _view.Aspect;
            _updatingUi = true;
            try
            {
                _cmbAspect.Items.Clear();
                _cmbAspect.Items.Add(new ListItem(Loc.T("aspect.auto"), AspectMode.Auto));
                _cmbAspect.Items.Add(new ListItem(Loc.T("aspect.native"), AspectMode.Native));
                _cmbAspect.Items.Add(new ListItem(Loc.T("aspect.wide"), AspectMode.Wide));
                _cmbAspect.Items.Add(new ListItem(Loc.T("aspect.stretch"), AspectMode.Stretch));
                _cmbAspect.SelectedIndex = 0;
                for (int i = 0; i < _cmbAspect.Items.Count; i++)
                {
                    ListItem it = (ListItem)_cmbAspect.Items[i];
                    if ((AspectMode)it.Value == current) { _cmbAspect.SelectedIndex = i; break; }
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void OnAspectComboChanged(object sender, EventArgs e)
        {
            if (_updatingUi) return;
            ListItem it = _cmbAspect.SelectedItem as ListItem;
            if (it == null) return;
            _view.Aspect = (AspectMode)it.Value;
            _settings.Aspect = _view.Aspect.ToString();
            _view.Invalidate();
        }

        /// <summary>Enables / disables every control according to the live and recording state.</summary>
        private void UpdateUiState()
        {
            bool recording = _recorder.IsRecording;
            bool recActive = recording || _recBusy;
            bool hasDevice = SelectedDevice != null;

            _btnStartLive.Enabled = hasDevice && !_live && !_busy;
            _btnStopLive.Enabled = _live && !_busy;

            _cmbDevice.Enabled = !_busy && !recActive;
            _btnRefresh.Enabled = !_busy && !recActive;
            _cmbSource.Enabled = _caps.Inputs.Count > 0 && !_busy;
            bool formatEditable = !_busy && !recActive && _caps.Formats.Count > 0;
            _cmbResolution.Enabled = formatEditable;
            _cmbFps.Enabled = formatEditable;

            bool canSnap = _live && !_busy;
            _btnSnapshot.Enabled = canSnap;
            _fsSnap.Enabled = canSnap;

            _txtVideoName.Enabled = !recActive;
            _chkAppendDate.Enabled = !recActive;
            _btnStartRec.Enabled = _live && !_busy && !recActive;
            _btnStopRec.Enabled = recording && !_recBusy;

            _recDot.LedColor = recording ? Theme.Red : Theme.DisabledFill;
            _lblRecTimer.ForeColor = recording ? Theme.Red : Theme.SideDim;
            if (!recording)
            {
                _lblRecTimer.Text = "00:00:00";
                _fsTimerLabel.Text = "";
            }

            _fsRec.Text = recording ? Loc.T("btn.stopRec") : Loc.T("btn.startRec");
            _fsRec.BaseColor = recording ? Theme.Gray : Theme.Red;
            _fsRec.Enabled = recording ? !_recBusy : (_live && !_busy && !_recBusy);

            _btnFull.Enabled = !_fullScreen;
            _btnExitFull.Enabled = _fullScreen;
            _btnSettings.Enabled = !recActive;
        }
    }
}
