using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class ClipboardSyncEngine : IDisposable
{
    private const int DefaultPort = 5001;
    private const string DefaultPeerIp = "100.64.0.2";
    private const byte ProtocolVersion = 1;

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly object _statusLock = new();
    private readonly Dictionary<string, PeerConnection> _activePeerConnections = new();
    private PeerManager? _peerManager;
    private AutoconnectManager? _autoconnectManager;
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
            _peerManager = new PeerManager();
            _autoconnectManager = new AutoconnectManager(_peerManager, AttemptPeerConnectionAsync);
            _autoconnectManager.Start();

            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _listenerCts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
            AppendStatus($"Application ready. Local listener on port {Port}. Autoconnect profiles: {_peerManager.GetAutoConnectProfiles().Count}");
        }
        catch (SocketException ex)
        {
            _listener = null;
            _listenerCts = null;
            _autoconnectManager?.Stop();
            AppendStatus($"Failed to start local listener on port {Port}: {ex.Message} (port is already in use or unavailable).");
        }
    }

    public void Stop()
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _listenerCts = null;

        _autoconnectManager?.Stop();
        _autoconnectManager?.Dispose();
        _autoconnectManager = null;

        lock (_activePeerConnections)
        {
            foreach (var conn in _activePeerConnections.Values)
            {
                conn.Dispose();
            }

            _activePeerConnections.Clear();
        }
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
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var payload = new ClipboardPayload
        {
            Kind = ClipboardPayloadKind.Text,
            SessionId = _instanceId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text
        };

        // Fan-out to all autoconnect peers
        if (_peerManager != null)
        {
            var profiles = _peerManager.GetAutoConnectProfiles();
            var sendTasks = profiles.Select(p => SendToPeerAsync(p, payload)).ToList();
            _ = Task.WhenAll(sendTasks);
        }

        // Also send to manually-specified peer for backward compatibility
        if (!string.IsNullOrWhiteSpace(PeerIp))
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(PeerIp, Port);
                using var stream = client.GetStream();
                var json = JsonSerializer.Serialize(payload);
                var body = Encoding.UTF8.GetBytes(json);
                await WriteFrameAsync(stream, body);
                await stream.FlushAsync();
                AppendStatus($"Sent clipboard update to {PeerIp}:{Port} ({text.Length} chars).");
            }
            catch (Exception ex)
            {
                AppendStatus($"Send failed to {PeerIp}:{Port}: {ex.Message}");
            }
        }
    }

    private async Task SendToPeerAsync(ConnectionProfile profile, ClipboardPayload payload)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(profile.TailscaleIp, profile.Port);
            using var stream = client.GetStream();
            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            await WriteFrameAsync(stream, body);
            await stream.FlushAsync();

            // Update last connected time on success
            profile.LastConnectedUtc = DateTime.UtcNow;
            _peerManager?.AddOrUpdate(profile);

            AppendStatus($"Sent to {profile.Name} ({profile.TailscaleIp}:{profile.Port}).");
        }
        catch (Exception ex)
        {
            AppendStatus($"Send to {profile.Name} failed: {ex.Message}");
        }
    }

    private async Task AttemptPeerConnectionAsync(ConnectionProfile profile)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(profile.TailscaleIp, profile.Port);
            profile.LastConnectedUtc = DateTime.UtcNow;
            _peerManager?.AddOrUpdate(profile);
            _autoconnectManager?.ResetBackoff(profile.Id);
            AppendStatus($"Autoconnect: {profile.Name} ({profile.TailscaleIp}) connected.");
        }
        catch
        {
            // Backoff manager will retry
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
            var payload = await ReadFrameAsync(stream);
            if (payload == null)
            {
                return;
            }

            if (payload.SessionId == _instanceId || payload.MessageId == _lastReceivedMessageId)
            {
                return;
            }

            _lastReceivedMessageId = payload.MessageId;
            if (payload.Kind == ClipboardPayloadKind.Text && !string.IsNullOrWhiteSpace(payload.Text))
            {
                ClipboardTextReceived?.Invoke(this, payload.Text);
            }
        }
        catch (Exception ex)
        {
            AppendStatus($"Receive failed: {ex.Message}");
        }
    }

    private static async Task WriteFrameAsync(NetworkStream stream, byte[] payload)
    {
        var header = new byte[5];
        header[0] = ProtocolVersion;
        BitConverter.GetBytes(IPAddress.NetworkToHostOrder((short)payload.Length));
        header[1] = (byte)((payload.Length >> 24) & 0xFF);
        header[2] = (byte)((payload.Length >> 16) & 0xFF);
        header[3] = (byte)((payload.Length >> 8) & 0xFF);
        header[4] = (byte)(payload.Length & 0xFF);

        await stream.WriteAsync(header, 0, header.Length);
        await stream.WriteAsync(payload, 0, payload.Length);
    }

    private static async Task<ClipboardPayload?> ReadFrameAsync(NetworkStream stream)
    {
        var versionBuffer = new byte[1];
        var bytesRead = await ReadExactlyAsync(stream, versionBuffer, 1);
        if (bytesRead == 0)
        {
            return null;
        }

        var version = versionBuffer[0];
        if (version != ProtocolVersion)
        {
            throw new InvalidOperationException($"Protocol version mismatch: received {version}, expected {ProtocolVersion}.");
        }

        var lengthBuffer = new byte[4];
        var lengthRead = await ReadExactlyAsync(stream, lengthBuffer, 4);
        if (lengthRead < 4)
        {
            return null;
        }

        var payloadLength = ((lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3]);
        var payloadBuffer = new byte[payloadLength];
        var payloadRead = await ReadExactlyAsync(stream, payloadBuffer, payloadLength);
        if (payloadRead < payloadLength)
        {
            return null;
        }

        var json = Encoding.UTF8.GetString(payloadBuffer);
        return JsonSerializer.Deserialize<ClipboardPayload>(json);
    }

    private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int length)
    {
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private void AppendStatus(string message)
    {
        lock (_statusLock)
        {
            StatusChanged?.Invoke(this, message);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
