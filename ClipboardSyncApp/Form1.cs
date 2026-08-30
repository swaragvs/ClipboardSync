using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ClipboardSyncApp;

public partial class Form1 : Form
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_DESTROY = 0x0002;

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly object _statusLock = new();
    private readonly string _defaultPeerIp = "100.64.0.2";
    private readonly int _defaultPort = 5001;

    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private bool _suppressClipboardUpdate;
    private string _lastReceivedMessageId = string.Empty;

    public Form1()
    {
        InitializeComponent();

        peerIpTextBox.Text = _defaultPeerIp;
        portNumericUpDown.Value = _defaultPort;

        Shown += Form1_Shown;
        FormClosing += Form1_FormClosing;
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
        StartListener();
        AppendStatus($"Application ready. Local listener on port {GetListenPort()}. Use a Tailscale peer IP to sync text.");
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
    }

    private void connectButton_Click(object sender, EventArgs e)
    {
        _ = TestPeerConnectionAsync();
    }

    private void sendTestButton_Click(object sender, EventArgs e)
    {
        _ = SendClipboardTextAsync($"Test message from {Environment.MachineName} at {DateTime.Now:HH:mm:ss}");
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

        _ = SendClipboardTextAsync(text);
    }

    private async Task TestPeerConnectionAsync()
    {
        if (!TryGetPeerAddress(out var peerIp, out var port))
        {
            return;
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(peerIp, port);
            AppendStatus($"Connection test succeeded to {peerIp}:{port}.");
        }
        catch (Exception ex)
        {
            AppendStatus($"Connection test failed to {peerIp}:{port}: {ex.Message}");
        }
    }

    private async Task SendClipboardTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !TryGetPeerAddress(out var peerIp, out var port))
        {
            return;
        }

        var payload = new ClipboardPayload
        {
            SessionId = _instanceId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text
        };

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(peerIp, port);
            using var stream = client.GetStream();
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
            AppendStatus($"Sent clipboard update to {peerIp}:{port} ({text.Length} chars). ");
        }
        catch (Exception ex)
        {
            AppendStatus($"Send failed to {peerIp}:{port}: {ex.Message}");
        }
    }

    private void StartListener()
    {
        if (_listener != null)
        {
            return;
        }

        var port = GetListenPort();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _listenerCts = new CancellationTokenSource();

        _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener ?? throw new InvalidOperationException("Listener was not created.");

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                AppendStatus($"Socket listener error: {ex.Message}");
                break;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                memory.Write(buffer, 0, read);
            }

            var json = Encoding.UTF8.GetString(memory.ToArray());
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var payload = JsonSerializer.Deserialize<ClipboardPayload>(json);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Text))
            {
                return;
            }

            if (payload.SessionId == _instanceId || payload.MessageId == _lastReceivedMessageId)
            {
                return;
            }

            _lastReceivedMessageId = payload.MessageId ?? string.Empty;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyRemoteClipboard(payload.Text)));
                return;
            }

            ApplyRemoteClipboard(payload.Text);
        }
        catch (Exception ex)
        {
            AppendStatus($"Receive failed: {ex.Message}");
        }
    }

    private void ApplyRemoteClipboard(string text)
    {
        _suppressClipboardUpdate = true;
        Clipboard.SetText(text);
        AppendStatus($"Received clipboard text from another machine ({text.Length} chars). ");
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

    private bool TryGetPeerAddress(out string peerIp, out int port)
    {
        peerIp = peerIpTextBox.Text.Trim();
        port = (int)portNumericUpDown.Value;

        if (string.IsNullOrWhiteSpace(peerIp))
        {
            AppendStatus("Enter a valid Tailscale peer IP before syncing.");
            return false;
        }

        if (port <= 0 || port > 65535)
        {
            AppendStatus("Enter a valid port number between 1 and 65535.");
            return false;
        }

        return true;
    }

    private int GetListenPort()
    {
        return (int)portNumericUpDown.Value;
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

    private sealed class ClipboardPayload
    {
        public string SessionId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
