using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using HauppaugeLiveCaptureStudio.Core;
using HauppaugeLiveCaptureStudio.Media;

namespace HauppaugeLiveCaptureStudio.UI
{
    /// <summary>Settings window: save folder, language, FFmpeg location, recording quality, behaviour.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Func<Task<string>> _diagnostics;

        private ComboBox _cmbLanguage;
        private ComboBox _cmbQuality;
        private TextBox _txtFolder;
        private TextBox _txtFfmpeg;
        private Label _lblFfmpegState;
        private CheckBox _chkFragmented;
        private CheckBox _chkAutoStart;
        private string _newBase;

        public string Language { get; private set; }
        public string FfmpegPath { get; private set; }
        public int Crf { get; private set; }
        public bool FragmentedMp4 { get; private set; }
        public bool AutoStartLive { get; private set; }

        /// <summary>Folder chosen by the user (the "Hauppauge Capture Studio" folder is created inside it), or null if unchanged.</summary>
        public string NewBaseFolder { get { return _newBase; } }

        public SettingsForm(AppSettings settings, Func<Task<string>> diagnostics)
        {
            _settings = settings;
            _diagnostics = diagnostics;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Text = Loc.T("set.title");
            Font = Theme.UiFont;
            BackColor = Theme.Side;
            ForeColor = Theme.SideText;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(660, 590);
            RightToLeft = Loc.IsArabic ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = Loc.IsArabic;

            Language = settings.Language;
            FfmpegPath = settings.FfmpegPath ?? "";
            Crf = settings.RecordCrf;
            FragmentedMp4 = settings.FragmentedMp4;
            AutoStartLive = settings.AutoStartLive;

            BuildUi();
        }

        private void BuildUi()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.Padding = new Padding(22, 16, 22, 16);
            t.BackColor = Theme.Side;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // language
            AddCaption(t, Loc.T("set.language"));
            _cmbLanguage = new ComboBox();
            _cmbLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLanguage.Items.Add(new ListItem("English", "en"));
            _cmbLanguage.Items.Add(new ListItem("العربية", "ar"));
            _cmbLanguage.SelectedIndex = string.Equals(Language, "ar", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _cmbLanguage.Width = 220;
            _cmbLanguage.Margin = new Padding(0, 0, 0, 10);
            AddRow(t, _cmbLanguage);

            // save folder
            AddCaption(t, Loc.T("set.folder"));
            _txtFolder = new TextBox();
            _txtFolder.ReadOnly = true;
            _txtFolder.BorderStyle = BorderStyle.FixedSingle;
            _txtFolder.Text = AppPaths.RootDir;
            FlatButton browseFolder = SmallButton(Loc.T("set.browse"));
            browseFolder.Click += OnBrowseFolder;
            AddRow(t, TextWithButton(_txtFolder, browseFolder));

            // ffmpeg
            AddCaption(t, Loc.T("set.ffmpeg"));
            _txtFfmpeg = new TextBox();
            _txtFfmpeg.BorderStyle = BorderStyle.FixedSingle;
            _txtFfmpeg.PlaceholderText = Loc.T("set.ffmpegAuto");
            _txtFfmpeg.Text = FfmpegPath;
            _txtFfmpeg.TextChanged += delegate { UpdateFfmpegState(); };
            FlatButton browseFfmpeg = SmallButton(Loc.T("set.browse"));
            browseFfmpeg.Click += OnBrowseFfmpeg;
            AddRow(t, TextWithButton(_txtFfmpeg, browseFfmpeg));

            _lblFfmpegState = new Label();
            _lblFfmpegState.AutoSize = true;
            _lblFfmpegState.MaximumSize = new Size(600, 0);
            _lblFfmpegState.Font = Theme.SmallFont;
            _lblFfmpegState.Margin = new Padding(0, 0, 0, 10);
            AddRow(t, _lblFfmpegState);
            UpdateFfmpegState();

            // quality
            AddCaption(t, Loc.T("set.quality"));
            _cmbQuality = new ComboBox();
            _cmbQuality.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbQuality.Items.Add(new ListItem(Loc.T("set.q.high"), 18));
            _cmbQuality.Items.Add(new ListItem(Loc.T("set.q.std"), 23));
            _cmbQuality.Items.Add(new ListItem(Loc.T("set.q.small"), 28));
            _cmbQuality.SelectedIndex = Crf <= 20 ? 0 : (Crf <= 25 ? 1 : 2);
            _cmbQuality.Width = 300;
            _cmbQuality.Margin = new Padding(0, 0, 0, 10);
            AddRow(t, _cmbQuality);

            // options
            _chkFragmented = new CheckBox();
            _chkFragmented.AutoSize = true;
            _chkFragmented.MaximumSize = new Size(600, 0);
            _chkFragmented.Text = Loc.T("set.fragmented");
            _chkFragmented.Checked = FragmentedMp4;
            _chkFragmented.Margin = new Padding(0, 0, 0, 8);
            AddRow(t, _chkFragmented);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.MaximumSize = new Size(600, 0);
            _chkAutoStart.Text = Loc.T("set.autostart");
            _chkAutoStart.Checked = AutoStartLive;
            _chkAutoStart.Margin = new Padding(0, 0, 0, 14);
            AddRow(t, _chkAutoStart);

            // tools row
            FlowLayoutPanel tools = new FlowLayoutPanel();
            tools.FlowDirection = FlowDirection.LeftToRight;
            tools.WrapContents = false;
            tools.AutoSize = true;
            tools.BackColor = Theme.Side;
            tools.Margin = new Padding(0, 0, 0, 14);
            FlatButton diag = SmallButton(Loc.T("set.diag"));
            diag.Width = 190;
            diag.Margin = new Padding(0, 0, 8, 0);
            diag.Click += OnCopyDiagnostics;
            FlatButton open = SmallButton(Loc.T("set.openFolder"));
            open.Width = 190;
            open.Margin = new Padding(0);
            open.Click += OnOpenFolder;
            tools.Controls.Add(diag);
            tools.Controls.Add(open);
            AddRow(t, tools);

            // OK / Cancel
            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.AutoSize = true;
            buttons.BackColor = Theme.Side;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlatButton ok = new FlatButton();
            ok.BaseColor = Theme.Accent;
            ok.Text = Loc.T("set.ok");
            ok.Dock = DockStyle.Fill;
            ok.Margin = new Padding(0, 0, 8, 0);
            ok.Click += OnOk;

            FlatButton cancel = new FlatButton();
            cancel.BaseColor = Theme.Gray;
            cancel.Text = Loc.T("set.cancel");
            cancel.Dock = DockStyle.Fill;
            cancel.Margin = new Padding(0);
            cancel.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(ok, 1, 0);
            buttons.Controls.Add(cancel, 2, 0);
            AddRow(t, buttons);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(t);
        }

        // ------------------------------------------------------------------ helpers

        private static void AddRow(TableLayoutPanel t, Control c)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = c is ComboBox ? DockStyle.Left : DockStyle.Fill;
            t.Controls.Add(c, 0, row);
        }

        private static void AddCaption(TableLayoutPanel t, string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.MaximumSize = new Size(600, 0);
            l.Text = text;
            l.Margin = new Padding(0, 4, 0, 4);
            AddRow(t, l);
        }

        private static FlatButton SmallButton(string text)
        {
            FlatButton b = new FlatButton();
            b.BaseColor = Theme.Gray;
            b.Text = text;
            b.Width = 120;
            b.Height = 32;
            return b;
        }

        private static TableLayoutPanel TextWithButton(TextBox box, FlatButton button)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.AutoSize = true;
            t.BackColor = Theme.Side;
            t.Margin = new Padding(0, 0, 0, 6);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 6, 8, 0);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0);
            t.Controls.Add(box, 0, 0);
            t.Controls.Add(button, 1, 0);
            return t;
        }

        private void UpdateFfmpegState()
        {
            string found = FfmpegLocator.Find(_txtFfmpeg.Text.Trim());
            if (found != null)
            {
                _lblFfmpegState.Text = Loc.T("set.ffmpegFound", found);
                _lblFfmpegState.ForeColor = Theme.Green;
            }
            else
            {
                _lblFfmpegState.Text = Loc.T("set.ffmpegMissing");
                _lblFfmpegState.ForeColor = Theme.Amber;
            }
        }

        // ------------------------------------------------------------------ events

        private void OnBrowseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Loc.T("dlg.pickFolder");
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;
                try
                {
                    string parent = Path.GetDirectoryName(AppPaths.RootDir);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) dialog.SelectedPath = parent;
                }
                catch { }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _newBase = dialog.SelectedPath;
                _txtFolder.Text = AppPaths.ComposeRoot(_newBase);
            }
        }

        private void OnBrowseFfmpeg(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Loc.T("dlg.pickFfmpeg");
                dialog.Filter = "ffmpeg.exe|ffmpeg.exe|*.exe|*.exe";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) _txtFfmpeg.Text = dialog.FileName;
            }
        }

        private async void OnCopyDiagnostics(object sender, EventArgs e)
        {
            try
            {
                string text = await _diagnostics();
                Clipboard.SetText(text);
                MessageBox.Show(this, Loc.T("msg.diagCopied"), AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Warn("Copying diagnostics failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(AppPaths.RootDir);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not open the folder: " + ex.Message);
            }
        }

        private void OnOk(object sender, EventArgs e)
        {
            ListItem lang = _cmbLanguage.SelectedItem as ListItem;
            Language = lang == null ? "en" : (string)lang.Value;
            FfmpegPath = _txtFfmpeg.Text.Trim();
            ListItem q = _cmbQuality.SelectedItem as ListItem;
            Crf = q == null ? 18 : (int)q.Value;
            FragmentedMp4 = _chkFragmented.Checked;
            AutoStartLive = _chkAutoStart.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
