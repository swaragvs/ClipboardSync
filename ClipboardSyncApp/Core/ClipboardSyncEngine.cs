using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ClipboardSyncApp.Core;

public sealed class ClipboardSyncEngine
{
    private const int DefaultPort = 5001;
    private const string DefaultPeerIp = "100.64.0.2";

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly object _statusLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private string _lastReceivedMessageId = string.Empty;

    public string PeerIp { get; set; } = DefaultPeerIp;
    public int Port { get; set; } = DefaultPort;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ClipboardTextReceived;

    public void Start()
    {
        if (_listener != null)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _listenerCts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
            AppendStatus($"Application ready. Local listener on port {Port}. Use a Tailscale peer IP to sync text.");
        }
        catch (SocketException ex)
        {
            _listener = null;
            _listenerCts = null;
            AppendStatus($"Failed to start local listener on port {Port}: {ex.Message} (port is already in use or unavailable).");
        }
    }

    public void Stop()
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _listenerCts = null;
    }

    public async Task TestPeerConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(PeerIp))
        {
            AppendStatus("Enter a valid Tailscale peer IP before syncing.");
            return;
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(PeerIp, Port);
            AppendStatus($"Connection test succeeded to {PeerIp}:{Port}.");
        }
        catch (Exception ex)
        {
            AppendStatus($"Connection test failed to {PeerIp}:{Port}: {ex.Message}");
        }
    }

    public async Task SendTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(PeerIp))
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
            await client.ConnectAsync(PeerIp, Port);
            using var stream = client.GetStream();
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
            AppendStatus($"Sent clipboard update to {PeerIp}:{Port} ({text.Length} chars).");
        }
        catch (Exception ex)
        {
            AppendStatus($"Send failed to {PeerIp}:{Port}: {ex.Message}");
        }
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
            ClipboardTextReceived?.Invoke(this, payload.Text);
        }
        catch (Exception ex)
        {
            AppendStatus($"Receive failed: {ex.Message}");
        }
    }

    private void AppendStatus(string message)
    {
        lock (_statusLock)
        {
            StatusChanged?.Invoke(this, message);
        }
    }

    private sealed class ClipboardPayload
    {
        public string SessionId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
