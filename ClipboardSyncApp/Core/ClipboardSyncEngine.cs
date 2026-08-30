using System.Net;
using System.Net.Sockets;
using System.Text;
using ClipboardSyncApp.Config;
using ClipboardSyncApp.Platform.Windows;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class ClipboardSyncEngine : IDisposable
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly AppSettings _settings;
    private readonly IClipboardService? _clipboardService;
    private readonly IClipboardWatcher? _clipboardWatcher;
    private readonly RemoteClipboardTracker _remoteTracker = new();
    private readonly FileTransferService _fileTransferService = new();

    private readonly HashSet<string> _recentMessageIds = new();
    private readonly Queue<string> _recentMessageIdQueue = new();
    private readonly object _messageIdLock = new();

    private PeerManager? _peerManager;
    private AutoconnectManager? _autoconnectManager;
    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;

    public string InstanceId => _instanceId;
    public int Port => _settings.Port;
    public PeerManager? PeerManager => _peerManager;
    public FileTransferService FileTransferService => _fileTransferService;
    public RemoteClipboardTracker RemoteTracker => _remoteTracker;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ClipboardTextReceived;
    public event EventHandler<byte[]>? ClipboardImageReceived;
    public event EventHandler<string>? ClipboardRtfReceived;
    public event EventHandler<string>? ClipboardFileReceived;

    public ClipboardSyncEngine(AppSettings? settings = null, IClipboardService? clipboardService = null, IClipboardWatcher? clipboardWatcher = null)
    {
        _settings = settings ?? AppSettings.Load();
        _clipboardService = clipboardService;
        _clipboardWatcher = clipboardWatcher;

        if (_clipboardWatcher != null)
        {
            _clipboardWatcher.ClipboardChanged += OnLocalClipboardChanged;
        }
    }

    public void Start()
    {
        if (_listener != null)
        {
            return;
        }

        try
        {
            _peerManager = new PeerManager(_instanceId);
            _peerManager.StatusChanged += (_, msg) => AppendStatus(msg);
            _peerManager.PayloadReceived += OnPayloadReceivedFromPeer;
            _peerManager.InitializeConnections();

            _autoconnectManager = new AutoconnectManager(_peerManager, AttemptPeerConnectionAsync);
            _autoconnectManager.Start();

            _listener = new TcpListener(IPAddress.Any, _settings.Port);
            _listener.Start();
            _listenerCts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));

            AppendStatus($"Application ready. Listener active on port {_settings.Port}. Autoconnect profiles: {_peerManager.GetAutoConnectProfiles().Count}");
        }
        catch (SocketException ex)
        {
            _listener = null;
            _listenerCts = null;
            _autoconnectManager?.Stop();
            AppendStatus($"Failed to start listener on port {_settings.Port}: {ex.Message}");
        }
    }

    public void Stop()
    {
        _listenerCts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _listenerCts = null;

        _autoconnectManager?.Stop();
        _autoconnectManager?.Dispose();
        _autoconnectManager = null;

        _peerManager?.Dispose();
        _peerManager = null;
    }

    public Task SendTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        var payload = new ClipboardPayload
        {
            Type = MessageType.ClipboardText,
            SessionId = _instanceId,
            MessageId = Guid.NewGuid().ToString("N"),
            Text = text
        };

        return SendPayloadAsync(payload);
    }

    public Task SendImageAsync(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return Task.CompletedTask;
        }

        var payload = new ClipboardPayload
        {
            Type = MessageType.ClipboardImage,
            SessionId = _instanceId,
            MessageId = Guid.NewGuid().ToString("N"),
            ImageBytes = pngBytes
        };

        return SendPayloadAsync(payload);
    }

    public Task SendFileOfferAsync(string localFilePath)
    {
        if (!File.Exists(localFilePath))
        {
            return Task.CompletedTask;
        }

        var transferId = _fileTransferService.CreateFileOffer(localFilePath, out var offerPayload);
        offerPayload.SessionId = _instanceId;
        _ = SendPayloadAsync(offerPayload);

        // Stream file out
        if (_peerManager != null)
        {
            _ = Task.Run(async () =>
            {
                await _fileTransferService.StreamOutboundFileAsync(localFilePath, transferId, payload =>
                {
                    payload.SessionId = _instanceId;
                    _peerManager.FanOutPayload(payload);
                    return Task.CompletedTask;
                }, CancellationToken.None);
            });
        }

        return Task.CompletedTask;
    }

    public Task SendPayloadAsync(ClipboardPayload payload)
    {
        if (payload == null)
        {
            return Task.CompletedTask;
        }

        payload.SessionId = _instanceId;
        if (string.IsNullOrEmpty(payload.MessageId))
        {
            payload.MessageId = Guid.NewGuid().ToString("N");
        }

        // Single outbound path: Fan out through PeerManager
        if (_peerManager != null)
        {
            _peerManager.FanOutPayload(payload);
        }

        return Task.CompletedTask;
    }

    private void OnLocalClipboardChanged(object? sender, EventArgs e)
    {
        if (_clipboardService == null)
        {
            return;
        }

        if (_clipboardService.HasText())
        {
            var text = _clipboardService.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var normText = RemoteClipboardTracker.NormalizeText(text);
                var textBytes = Encoding.UTF8.GetBytes(normText);
                if (_remoteTracker.IsEcho(MessageType.ClipboardText, textBytes))
                {
                    return;
                }
                _remoteTracker.RecordSentLocal(MessageType.ClipboardText, textBytes);
                _ = SendTextAsync(text);
                return;
            }
        }

        if (_clipboardService.HasImage())
        {
            var bytes = _clipboardService.GetImageBytes();
            if (bytes != null && bytes.Length > 0)
            {
                if (_remoteTracker.IsEcho(MessageType.ClipboardImage, bytes))
                {
                    return;
                }
                _remoteTracker.RecordSentLocal(MessageType.ClipboardImage, bytes);
                _ = SendImageAsync(bytes);
                return;
            }
        }
    }

    private void OnPayloadReceivedFromPeer(object? sender, ClipboardPayload payload)
    {
        if (payload == null || payload.SessionId == _instanceId || IsDuplicateMessage(payload.MessageId))
        {
            return;
        }

        switch (payload.Type)
        {
            case MessageType.ClipboardText:
                if (!string.IsNullOrWhiteSpace(payload.Text))
                {
                    var normText = RemoteClipboardTracker.NormalizeText(payload.Text);
                    var textBytes = Encoding.UTF8.GetBytes(normText);
                    _remoteTracker.RecordInjectedRemote(MessageType.ClipboardText, textBytes, payload.MessageId);
                    _clipboardService?.SetText(payload.Text);
                    ClipboardTextReceived?.Invoke(this, payload.Text);
                    AppendStatus($"Received remote text ({payload.Text.Length} chars).");
                }
                break;

            case MessageType.ClipboardImage:
                if (payload.ImageBytes != null && payload.ImageBytes.Length > 0)
                {
                    _remoteTracker.RecordInjectedRemote(MessageType.ClipboardImage, payload.ImageBytes, payload.MessageId);
                    _clipboardService?.SetImageBytes(payload.ImageBytes);
                    ClipboardImageReceived?.Invoke(this, payload.ImageBytes);
                    AppendStatus($"Received remote image ({payload.ImageBytes.Length} bytes).");
                }
                break;

            case MessageType.ClipboardRtf:
                if (!string.IsNullOrWhiteSpace(payload.RtfText))
                {
                    var normText = RemoteClipboardTracker.NormalizeText(payload.RtfText);
                    var rtfBytes = Encoding.UTF8.GetBytes(normText);
                    _remoteTracker.RecordInjectedRemote(MessageType.ClipboardRtf, rtfBytes, payload.MessageId);
                    _clipboardService?.SetRtf(payload.RtfText);
                    ClipboardRtfReceived?.Invoke(this, payload.RtfText);
                    AppendStatus("Received remote RTF text.");
                }
                break;

            case MessageType.FileOffer:
                _ = HandleFileOfferAsync(payload);
                break;

            case MessageType.FileChunk:
                _ = _fileTransferService.ProcessIncomingChunkAsync(payload);
                break;

            case MessageType.FileComplete:
                if (!string.IsNullOrEmpty(payload.TransferId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var finalPath = await _fileTransferService.CompleteIncomingTransferAsync(payload.TransferId);
                            if (!string.IsNullOrEmpty(finalPath))
                            {
                                ClipboardFileReceived?.Invoke(this, finalPath);
                                AppendStatus($"File received and verified: {finalPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendStatus($"File transfer completion error: {ex.Message}");
                        }
                    });
                }
                break;

            case MessageType.FileCancel:
                if (!string.IsNullOrEmpty(payload.TransferId))
                {
                    _fileTransferService.CancelTransfer(payload.TransferId);
                    AppendStatus($"File transfer {payload.TransferId} was cancelled by sender.");
                }
                break;
        }
    }

    private async Task HandleFileOfferAsync(ClipboardPayload payload)
    {
        if (string.IsNullOrEmpty(payload.TransferId) || string.IsNullOrEmpty(payload.FileName))
        {
            return;
        }

        var transferId = _fileTransferService.PrepareIncomingTransfer(payload);
        AppendStatus($"Receiving file offer: {payload.FileName} ({payload.FileSize} bytes). Initialized transfer {transferId}.");

        // Send FileAccept back
        var acceptPayload = new ClipboardPayload
        {
            Type = MessageType.FileAccept,
            SessionId = _instanceId,
            MessageId = Guid.NewGuid().ToString("N"),
            TransferId = transferId
        };
        await SendPayloadAsync(acceptPayload);
    }

    public async Task AttemptPeerConnectionAsync(ConnectionProfile profile)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(profile.TailscaleIp, profile.Port);

            profile.IsOnline = true;
            profile.LastConnectedUtc = DateTime.UtcNow;
            profile.LastError = null;
            _peerManager?.AddOrUpdate(profile);
            _autoconnectManager?.ResetBackoff(profile.Id);
            AppendStatus($"Autoconnect: {profile.Name} ({profile.TailscaleIp}) verified connected.");
        }
        catch (Exception ex)
        {
            profile.IsOnline = false;
            profile.LastError = ex.Message;
            AppendStatus($"Autoconnect for {profile.Name} failed: {ex.Message}");
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener ?? throw new InvalidOperationException("Listener was not created.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = client.GetStream();
                var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;

                // Find or create profile for incoming peer
                var profile = _peerManager?.Profiles.FirstOrDefault(p => string.Equals(p.TailscaleIp, remoteIp, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    profile = new ConnectionProfile { Name = $"Peer {remoteIp}", TailscaleIp = remoteIp, Port = _settings.Port };
                }

                var conn = _peerManager?.GetOrCreateConnection(profile);
                conn?.AttachInboundSocket(client, stream);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppendStatus($"Inbound listener error: {ex.Message}");
            }
        }
    }

    private bool IsDuplicateMessage(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return false;
        }

        lock (_messageIdLock)
        {
            if (_recentMessageIds.Contains(messageId))
            {
                return true;
            }

            _recentMessageIds.Add(messageId);
            _recentMessageIdQueue.Enqueue(messageId);
            if (_recentMessageIdQueue.Count > 100)
            {
                var oldest = _recentMessageIdQueue.Dequeue();
                _recentMessageIds.Remove(oldest);
            }

            return false;
        }
    }

    private void AppendStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }

    public void Dispose()
    {
        Stop();
    }
}
