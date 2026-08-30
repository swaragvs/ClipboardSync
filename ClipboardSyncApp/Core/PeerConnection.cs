using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardSyncApp.Core.Security;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public enum PeerConnectionState
{
    Disconnected,
    Connecting,
    HandshakeSent,
    Connected,
    Authenticated,
    Failed
}

public sealed class PeerConnection : IDisposable
{
    private const byte ProtocolVersion = 2;
    public const int MaxControlFrameSize = 1 * 1024 * 1024; // 1MB
    public const int MaxImageFrameSize = 25 * 1024 * 1024; // 25MB
    public const int MaxChunkFrameSize = 64 * 1024; // 64KB

    private readonly ConnectionProfile _profile;
    private readonly string _localPeerId;
    private readonly string _instanceId;
    private readonly PayloadQueue _queue;
    private readonly FileTransferService _fileTransferService = new();

    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _sessionCts;
    private Task? _sendWorkerTask;
    private Task? _receiveLoopTask;
    private Task? _heartbeatTask;

    private ulong _outboundSequenceNumber;
    private ulong _lastAcceptedSequenceNumber;
    private byte[]? _sessionKey;
    private byte[]? _myChallenge;
    private DateTime _lastTrafficUtc = DateTime.UtcNow;

    public ConnectionProfile Profile => _profile;
    public PeerConnectionState State { get; private set; } = PeerConnectionState.Disconnected;
    public bool IsOnline => State == PeerConnectionState.Authenticated || State == PeerConnectionState.Connected;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<ClipboardPayload>? PayloadReceived;
    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgressChanged
    {
        add => _fileTransferService.ProgressChanged += value;
        remove => _fileTransferService.ProgressChanged -= value;
    }

    public PeerConnection(ConnectionProfile profile, string instanceId, string? localPeerId = null, int maxQueueDepth = 20)
    {
        _profile = profile;
        _instanceId = instanceId;
        _localPeerId = localPeerId ?? DeviceIdentity.GetOrCreatePeerId();
        _queue = new PayloadQueue(maxQueueDepth);
    }

    public void Start()
    {
        if (_sessionCts != null)
        {
            return;
        }

        _sessionCts = new CancellationTokenSource();
        _sendWorkerTask = Task.Run(() => PersistentSessionLoopAsync(_sessionCts.Token));
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_sessionCts.Token));
    }

    public void EnqueuePayload(ClipboardPayload payload)
    {
        if (State != PeerConnectionState.Authenticated && payload.Type != MessageType.Handshake && payload.Type != MessageType.HandshakeAck)
        {
            return;
        }

        _queue.Enqueue(payload, out _);
    }

    public void AttachInboundSocket(TcpClient socket, NetworkStream stream)
    {
        lock (this)
        {
            DisconnectSocket();
            _tcpClient = socket;
            _networkStream = stream;
            State = PeerConnectionState.Connected;
        }

        _sessionCts ??= new CancellationTokenSource();
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_sessionCts.Token));
    }

    private async Task PersistentSessionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (State != PeerConnectionState.Authenticated && State != PeerConnectionState.Connected)
                {
                    await ConnectAndAuthenticateAsync(cancellationToken);
                }

                if (State == PeerConnectionState.Authenticated || State == PeerConnectionState.Connected)
                {
                    if (_queue.TryDequeue(out var queuedItem) && queuedItem != null)
                    {
                        var success = await SendPayloadInternalAsync(queuedItem.Payload, cancellationToken);
                        if (success)
                        {
                            _queue.MarkCompleted(queuedItem);
                        }
                        else
                        {
                            _queue.MarkFailed(queuedItem);
                            DisconnectSocket();
                        }
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                else
                {
                    await Task.Delay(5000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(PeerConnectionState.Failed, ex.Message);
                DisconnectSocket();
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ConnectAndAuthenticateAsync(CancellationToken cancellationToken)
    {
        SetState(PeerConnectionState.Connecting, "Connecting...");
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(_profile.TailscaleIp, _profile.Port, cancellationToken);
            var stream = client.GetStream();

            lock (this)
            {
                _tcpClient = client;
                _networkStream = stream;
                _myChallenge = RandomNumberGenerator.GetBytes(32);
            }

            var sharedKey = FrameCipher.UnprotectSecret(_profile.SharedKey ?? string.Empty);
            byte[]? authHMAC = null;
            if (!string.IsNullOrEmpty(sharedKey))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedKey));
                authHMAC = hmac.ComputeHash(_myChallenge);
            }

            var handshakePayload = new ClipboardPayload
            {
                Type = MessageType.Handshake,
                SessionId = _instanceId,
                PeerId = _localPeerId,
                OriginPeerId = _localPeerId,
                OriginSessionId = _instanceId,
                MessageId = Guid.NewGuid().ToString("N"),
                Challenge = _myChallenge,
                Authenticator = authHMAC
            };

            SetState(PeerConnectionState.HandshakeSent, "Handshake Sent");
            await WriteFrameAsync(stream, MessageType.Handshake, handshakePayload, cancellationToken);
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(cancellationToken));

            SetState(PeerConnectionState.Authenticated, "Connected & Authenticated");
            _profile.LastConnectedUtc = DateTime.UtcNow;
            _profile.IsOnline = true;
            _profile.LastError = null;
        }
        catch (Exception ex)
        {
            SetState(PeerConnectionState.Failed, ex.Message);
            DisconnectSocket();
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15000, cancellationToken);
                if (State == PeerConnectionState.Authenticated && _networkStream != null)
                {
                    if (DateTime.UtcNow - _lastTrafficUtc > TimeSpan.FromSeconds(15))
                    {
                        var pingPayload = new ClipboardPayload
                        {
                            Type = MessageType.Ping,
                            SessionId = _instanceId,
                            PeerId = _localPeerId,
                            MessageId = Guid.NewGuid().ToString("N")
                        };
                        await WriteFrameAsync(_networkStream, MessageType.Ping, pingPayload, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                DisconnectSocket();
            }
        }
    }

    private async Task<bool> SendPayloadInternalAsync(ClipboardPayload payload, CancellationToken cancellationToken)
    {
        if (_networkStream == null || (State != PeerConnectionState.Authenticated && State != PeerConnectionState.Connected))
        {
            return false;
        }

        try
        {
            await WriteFrameAsync(_networkStream, payload.Type, payload, cancellationToken);
            _lastTrafficUtc = DateTime.UtcNow;
            StatusChanged?.Invoke(this, $"Sent {payload.Type} to {_profile.Name} ({_profile.TailscaleIp}:{_profile.Port}).");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Send error to {_profile.Name}: {ex.Message}");
            return false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _networkStream != null)
        {
            try
            {
                var (msgType, payload) = await ReadFrameAsync(_networkStream, cancellationToken);
                _lastTrafficUtc = DateTime.UtcNow;

                if (payload == null)
                {
                    break;
                }

                // Enforce authentication gate: reject non-handshake frames if not authenticated
                if (State != PeerConnectionState.Authenticated && msgType != MessageType.Handshake && msgType != MessageType.HandshakeAck)
                {
                    StatusChanged?.Invoke(this, $"Rejected unauthenticated frame {msgType} from {_profile.Name}. Disconnecting.");
                    break;
                }

                if (payload.Type == MessageType.Handshake)
                {
                    var sharedKey = FrameCipher.UnprotectSecret(_profile.SharedKey ?? string.Empty);
                    if (!string.IsNullOrEmpty(sharedKey) && payload.Challenge != null)
                    {
                        var myServerChallenge = RandomNumberGenerator.GetBytes(32);
                        _sessionKey = FrameCipher.DeriveSessionKey(sharedKey, payload.Challenge, myServerChallenge);
                    }

                    var ack = new ClipboardPayload
                    {
                        Type = MessageType.HandshakeAck,
                        SessionId = _instanceId,
                        PeerId = _localPeerId,
                        OriginPeerId = _localPeerId,
                        OriginSessionId = _instanceId,
                        MessageId = Guid.NewGuid().ToString("N")
                    };
                    if (_networkStream != null)
                    {
                        await WriteFrameAsync(_networkStream, MessageType.HandshakeAck, ack, cancellationToken);
                    }
                    SetState(PeerConnectionState.Authenticated, "Authenticated");
                    continue;
                }

                if (payload.Type == MessageType.Ping)
                {
                    var pong = new ClipboardPayload
                    {
                        Type = MessageType.Pong,
                        SessionId = _instanceId,
                        PeerId = _localPeerId,
                        MessageId = Guid.NewGuid().ToString("N")
                    };
                    if (_networkStream != null)
                    {
                        await WriteFrameAsync(_networkStream, MessageType.Pong, pong, cancellationToken);
                    }
                    continue;
                }

                PayloadReceived?.Invoke(this, payload);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Receive error from {_profile.Name}: {ex.Message}");
                break;
            }
        }

        DisconnectSocket();
    }

    private async Task WriteFrameAsync(NetworkStream stream, MessageType messageType, ClipboardPayload payload, CancellationToken cancellationToken)
    {
        _outboundSequenceNumber++;
        payload.SequenceNumber = _outboundSequenceNumber;
        payload.PeerId = _localPeerId;
        if (string.IsNullOrEmpty(payload.OriginPeerId))
        {
            payload.OriginPeerId = _localPeerId;
        }
        if (string.IsNullOrEmpty(payload.OriginSessionId))
        {
            payload.OriginSessionId = _instanceId;
        }

        var json = JsonSerializer.Serialize(payload);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        var payloadBytes = FrameCipher.EncryptFrame(jsonBytes, _sessionKey, _outboundSequenceNumber, ProtocolVersion, (byte)messageType, out var header);

        var maxAllowed = messageType switch
        {
            MessageType.ClipboardImage => MaxImageFrameSize,
            MessageType.FileChunk => MaxChunkFrameSize + 1024,
            _ => MaxControlFrameSize
        };

        if (payloadBytes.Length > maxAllowed)
        {
            throw new InvalidOperationException($"Outbound payload size {payloadBytes.Length} exceeds allowed {maxAllowed} bytes for type {messageType}.");
        }

        await stream.WriteAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (payloadBytes.Length > 0)
        {
            await stream.WriteAsync(payloadBytes.AsMemory(0, payloadBytes.Length), cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<(MessageType MsgType, ClipboardPayload? Payload)> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[FrameCipher.HeaderSizeBytes];
        var headerRead = await ReadExactlyAsync(stream, header, FrameCipher.HeaderSizeBytes, cancellationToken);
        if (headerRead < FrameCipher.HeaderSizeBytes)
        {
            return (0, null);
        }

        var version = header[0];
        if (version != ProtocolVersion && version != 1)
        {
            throw new InvalidOperationException($"Protocol version mismatch: expected {ProtocolVersion}, received {version}.");
        }

        var payloadLength = FrameCipher.BinaryPrimitives_ReadInt32BigEndian(header.AsSpan(1, 4));
        var messageType = (MessageType)header[5];
        var sequenceNumber = FrameCipher.BinaryPrimitives_ReadUInt64BigEndian(header.AsSpan(6, 8));

        var maxAllowed = messageType switch
        {
            MessageType.ClipboardImage => MaxImageFrameSize,
            MessageType.FileChunk => MaxChunkFrameSize + 1024,
            _ => MaxControlFrameSize
        };

        if (payloadLength < 0 || payloadLength > maxAllowed)
        {
            throw new InvalidOperationException($"Frame payload length {payloadLength} exceeds max limit of {maxAllowed} for {messageType}.");
        }

        var payloadBytes = new byte[payloadLength];
        if (payloadLength > 0)
        {
            var payloadRead = await ReadExactlyAsync(stream, payloadBytes, payloadLength, cancellationToken);
            if (payloadRead < payloadLength)
            {
                return (messageType, null);
            }
        }

        if (_sessionKey != null)
        {
            if (sequenceNumber <= _lastAcceptedSequenceNumber && sequenceNumber != 0)
            {
                throw new InvalidDataException($"Replayed or out-of-order sequence number {sequenceNumber} (last accepted: {_lastAcceptedSequenceNumber}).");
            }
            _lastAcceptedSequenceNumber = sequenceNumber;
            payloadBytes = FrameCipher.DecryptFrame(payloadBytes, _sessionKey, header);
        }

        var json = Encoding.UTF8.GetString(payloadBytes);
        var payload = JsonSerializer.Deserialize<ClipboardPayload>(json);
        return (messageType, payload);
    }

    private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }

    private void SetState(PeerConnectionState state, string message)
    {
        State = state;
        _profile.IsOnline = (state == PeerConnectionState.Authenticated || state == PeerConnectionState.Connected);
        if (state == PeerConnectionState.Failed)
        {
            _profile.LastError = message;
        }
        StatusChanged?.Invoke(this, $"Peer {_profile.Name} state: {state} ({message})");
    }

    private void DisconnectSocket()
    {
        lock (this)
        {
            _networkStream?.Dispose();
            _tcpClient?.Dispose();
            _networkStream = null;
            _tcpClient = null;
            _sessionKey = null;
            if (State != PeerConnectionState.Disconnected)
            {
                SetState(PeerConnectionState.Disconnected, "Socket closed");
            }
        }
    }

    public void Stop()
    {
        _sessionCts?.Cancel();
        DisconnectSocket();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _queue.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}
