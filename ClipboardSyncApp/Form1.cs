using System.Runtime.InteropServices;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.Core;
using ClipboardSyncApp.UI;

namespace ClipboardSyncApp;

public partial class Form1 : Form
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_DESTROY = 0x0002;

    private readonly object _statusLock = new();
    private readonly AppSettings _settings;
    private readonly ClipboardSyncEngine _engine;
    private bool _suppressClipboardUpdate;
    private TrayContext? _trayContext;

    public ClipboardSyncEngine Engine => _engine;

    public Form1(AppSettings? settings = null)
    {
        InitializeComponent();

        _settings = settings ?? AppSettings.Load();
        _engine = new ClipboardSyncEngine();
        _engine.StatusChanged += (_, message) => AppendStatus(message);
        _engine.ClipboardTextReceived += (_, text) => ApplyRemoteClipboard(text);

        peerIpTextBox.Text = _engine.PeerIp;
        portNumericUpDown.Value = _engine.Port;

        Shown += Form1_Shown;
        FormClosing += Form1_FormClosing;
        Text = "Clipboard Sync";
    }

    public void SetTrayContext(TrayContext trayContext)
    {
        _trayContext = trayContext;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (this.Handle != IntPtr.Zero && !NativeMethods.AddClipboardFormatListener(this.Handle))
        {
            AppendStatus("Clipboard format listener could not be registered. Clipboard sync will not work.");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
        {
            HandleClipboardUpdate();
            return;
        }

        if (m.Msg == WM_DESTROY)
        {
            NativeMethods.RemoveClipboardFormatListener(this.Handle);
        }

        base.WndProc(ref m);
    }

    private void Form1_Shown(object? sender, EventArgs e)
    {
        _engine.Start();
        _engine.PeerIp = peerIpTextBox.Text.Trim();
        _engine.Port = (int)portNumericUpDown.Value;
        AppendStatus($"Application ready. Local listener on port {_engine.Port}. Use a Tailscale peer IP to sync text.");
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
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

        _engine.Stop();
    }

    private void connectButton_Click(object sender, EventArgs e)
    {
        _engine.PeerIp = peerIpTextBox.Text.Trim();
        _engine.Port = (int)portNumericUpDown.Value;
        _ = _engine.TestPeerConnectionAsync();
    }

    private void sendTestButton_Click(object sender, EventArgs e)
    {
        _engine.PeerIp = peerIpTextBox.Text.Trim();
        _engine.Port = (int)portNumericUpDown.Value;
        _ = _engine.SendTextAsync($"Test message from {Environment.MachineName} at {DateTime.Now:HH:mm:ss}");
    }

    private void HandleClipboardUpdate()
    {
        if (_suppressClipboardUpdate)
        {
            _suppressClipboardUpdate = false;
            return;
        }

        var text = TryReadClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _engine.PeerIp = peerIpTextBox.Text.Trim();
        _engine.Port = (int)portNumericUpDown.Value;
        _ = _engine.SendTextAsync(text);
    }

    private void ApplyRemoteClipboard(string text)
    {
        _suppressClipboardUpdate = true;
        Clipboard.SetText(text);
        AppendStatus($"Received clipboard text from another machine ({text.Length} chars).");
    }

    private string TryReadClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void AppendStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendStatus(message)));
            return;
        }

        lock (_statusLock)
        {
            statusTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
            statusTextBox.SelectionStart = statusTextBox.TextLength;
            statusTextBox.ScrollToCaret();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
