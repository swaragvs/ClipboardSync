using ClipboardSyncApp.Config;

namespace ClipboardSyncApp.UI;

public sealed class SettingsForm : Form
{
    private readonly TabControl _tabControl;
    private readonly AppSettings _settings;

    // General tab
    private CheckBox _startWithWindowsCheckBox = new();
    private CheckBox _startMinimizedCheckBox = new();
    private CheckBox _closeToTrayCheckBox = new();

    // History tab
    private CheckBox _pauseHistoryCheckBox = new();
    private NumericUpDown _historyMaxItemsUpDown = new();
    private NumericUpDown _historyMaxDaysUpDown = new();

    // Security tab
    private TextBox _pskDisplayTextBox = new();
    private Label _pairingCodeLabel = new();
    private Button _generateNewPskButton = new();

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Text = "Settings";
        Width = 500;
        Height = 400;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        _tabControl = new TabControl { Dock = DockStyle.Fill };

        CreateGeneralTab();
        CreateHistoryTab();
        CreateSecurityTab();

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var okButton = new Button { Text = "OK", Width = 80, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_tabControl);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings();
    }

    private void CreateGeneralTab()
    {
        var tab = new TabPage { Text = "General" };

        _startWithWindowsCheckBox.Text = "Start with Windows";
        _startWithWindowsCheckBox.Location = new Point(10, 10);
        _startWithWindowsCheckBox.AutoSize = true;

        _startMinimizedCheckBox.Text = "Start minimized to tray";
        _startMinimizedCheckBox.Location = new Point(10, 40);
        _startMinimizedCheckBox.AutoSize = true;

        _closeToTrayCheckBox.Text = "Close button minimizes to tray";
        _closeToTrayCheckBox.Location = new Point(10, 70);
        _closeToTrayCheckBox.AutoSize = true;

        tab.Controls.Add(_startWithWindowsCheckBox);
        tab.Controls.Add(_startMinimizedCheckBox);
        tab.Controls.Add(_closeToTrayCheckBox);

        _tabControl.TabPages.Add(tab);
    }

    private void CreateHistoryTab()
    {
        var tab = new TabPage { Text = "History" };

        _pauseHistoryCheckBox.Text = "Pause clipboard history";
        _pauseHistoryCheckBox.Location = new Point(10, 10);
        _pauseHistoryCheckBox.AutoSize = true;

        var maxItemsLabel = new Label { Text = "Max items in history:", Location = new Point(10, 40), AutoSize = true };
        _historyMaxItemsUpDown.Location = new Point(200, 40);
        _historyMaxItemsUpDown.Width = 100;
        _historyMaxItemsUpDown.Minimum = 10;
        _historyMaxItemsUpDown.Maximum = 1000;

        var maxDaysLabel = new Label { Text = "Max age (days):", Location = new Point(10, 70), AutoSize = true };
        _historyMaxDaysUpDown.Location = new Point(200, 70);
        _historyMaxDaysUpDown.Width = 100;
        _historyMaxDaysUpDown.Minimum = 1;
        _historyMaxDaysUpDown.Maximum = 365;

        tab.Controls.Add(_pauseHistoryCheckBox);
        tab.Controls.Add(maxItemsLabel);
        tab.Controls.Add(_historyMaxItemsUpDown);
        tab.Controls.Add(maxDaysLabel);
        tab.Controls.Add(_historyMaxDaysUpDown);

        _tabControl.TabPages.Add(tab);
    }

    private void CreateSecurityTab()
    {
        var tab = new TabPage { Text = "Security" };

        var pskLabel = new Label { Text = "Pre-Shared Key (for pairing):", Location = new Point(10, 10), AutoSize = true };
        _pskDisplayTextBox.Location = new Point(10, 30);
        _pskDisplayTextBox.Width = 450;
        _pskDisplayTextBox.ReadOnly = true;

        _pairingCodeLabel.Location = new Point(10, 60);
        _pairingCodeLabel.AutoSize = true;
        _pairingCodeLabel.Text = "Pairing Code: (not generated)";

        _generateNewPskButton.Text = "Generate New PSK";
        _generateNewPskButton.Location = new Point(10, 90);
        _generateNewPskButton.Width = 150;
        _generateNewPskButton.Click += (_, _) => GenerateNewPsk();

        var infoLabel = new Label
        {
            Text = "The pre-shared key is used to authenticate and encrypt communication with peer devices.",
            Location = new Point(10, 130),
            Width = 450,
            Height = 60,
            AutoSize = false
        };

        tab.Controls.Add(pskLabel);
        tab.Controls.Add(_pskDisplayTextBox);
        tab.Controls.Add(_pairingCodeLabel);
        tab.Controls.Add(_generateNewPskButton);
        tab.Controls.Add(infoLabel);

        _tabControl.TabPages.Add(tab);
    }

    private void LoadSettings()
    {
        _startWithWindowsCheckBox.Checked = _settings.StartWithWindows;
        _startMinimizedCheckBox.Checked = _settings.StartMinimized;
        _closeToTrayCheckBox.Checked = _settings.CloseToTray;
        _pauseHistoryCheckBox.Checked = _settings.PauseHistory;
        _historyMaxItemsUpDown.Value = _settings.HistoryMaxItems;
        _historyMaxDaysUpDown.Value = _settings.HistoryMaxDaysOld;
    }

    private void GenerateNewPsk()
    {
        var handshakeService = new ClipboardSyncApp.Core.Security.HandshakeService();
        var psk = handshakeService.GeneratePreSharedKey();
        var code = handshakeService.GeneratePairingCode(psk);
        _pskDisplayTextBox.Text = psk;
        _pairingCodeLabel.Text = $"Pairing Code: {code}";
    }

    public void SaveSettings()
    {
        _settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
        _settings.StartMinimized = _startMinimizedCheckBox.Checked;
        _settings.CloseToTray = _closeToTrayCheckBox.Checked;
        _settings.PauseHistory = _pauseHistoryCheckBox.Checked;
        _settings.HistoryMaxItems = (int)_historyMaxItemsUpDown.Value;
        _settings.HistoryMaxDaysOld = (int)_historyMaxDaysUpDown.Value;
        _settings.Save();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            SaveSettings();
        }

        base.OnFormClosing(e);
    }
}
