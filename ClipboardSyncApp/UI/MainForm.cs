using ClipboardSyncApp.Config;
using ClipboardSyncApp.Core;
using ClipboardSyncApp.Platform.Windows;

namespace ClipboardSyncApp.UI;

public partial class MainForm : Form
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_DESTROY = 0x0002;

    private readonly object _statusLock = new();
    private readonly AppSettings _settings;
    private readonly ClipboardSyncEngine _engine;
    private WindowsClipboardWatcher? _watcher;
    private WindowsClipboardService? _clipboardService;
    private TrayContext? _trayContext;

    public ClipboardSyncEngine Engine => _engine;

    public MainForm(AppSettings? settings = null)
    {
        InitializeComponent();

        _settings = settings ?? AppSettings.Load();
        _clipboardService = new WindowsClipboardService();

        _engine = new ClipboardSyncEngine(_settings, _clipboardService, null);
        _engine.StatusChanged += (_, message) => AppendStatus(message);
        _engine.ClipboardTextReceived += (_, text) => AppendStatus($"Received remote text ({text.Length} chars).");
        _engine.ClipboardImageReceived += (_, bytes) => AppendStatus($"Received remote image ({bytes.Length} bytes).");
        _engine.ClipboardRtfReceived += (_, rtf) => AppendStatus("Received remote RTF text.");
        _engine.ClipboardFileReceived += (_, path) => AppendStatus($"Received file: {path}");

        portNumericUpDown.Value = _engine.Port;

        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
        Text = "Clipboard Sync";
    }

    public void SetTrayContext(TrayContext trayContext)
    {
        _trayContext = trayContext;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (this.Handle != IntPtr.Zero)
        {
            _watcher = new WindowsClipboardWatcher(this.Handle);
            _watcher.ClipboardChanged += (_, _) => _engine.NotifyClipboardChanged();
            _watcher.Register();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (_watcher != null && _watcher.HandleWndProc(m.Msg))
        {
            return;
        }

        if (m.Msg == WM_DESTROY)
        {
            _watcher?.Unregister();
        }

        base.WndProc(ref m);
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        _engine.Start();
        AppendStatus($"Application ready. Local listener on port {_engine.Port}. Multi-format P2P sync enabled.");
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (_trayContext != null)
            {
                _trayContext.ShowBalloon("ClipboardSync is still running in the background.");
            }
            return;
        }

        _watcher?.Unregister();
        _engine.Stop();
    }

    private void connectButton_Click(object sender, EventArgs e)
    {
        var targetIp = peerIpTextBox.Text.Trim();
        var targetPort = (int)portNumericUpDown.Value;

        if (string.IsNullOrWhiteSpace(targetIp))
        {
            AppendStatus("Enter a valid peer IP to connect.");
            return;
        }

        var profile = new Storage.ConnectionProfile
        {
            Name = $"Peer {targetIp}",
            TailscaleIp = targetIp,
            Port = targetPort
        };

        _ = _engine.AttemptPeerConnectionAsync(profile);
    }

    private void sendTestButton_Click(object sender, EventArgs e)
    {
        _ = _engine.SendTextAsync($"Test message from {Environment.MachineName} at {DateTime.Now:HH:mm:ss}");
    }

    private void AppendStatus(string message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => AppendStatus(message)));
            }
            catch
            {
            }
            return;
        }

        lock (_statusLock)
        {
            statusTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
            statusTextBox.SelectionStart = statusTextBox.TextLength;
            statusTextBox.ScrollToCaret();
        }
    }
}
