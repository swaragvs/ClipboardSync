using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ClipboardSyncApp.Core;

public sealed class PeerConnection : IDisposable
{
    private const byte ProtocolVersion = 1;

    private readonly TcpClient _client = new();
    private readonly string _peerIp;
    private readonly int _port;
    private readonly string _instanceId;
    private readonly FileTransferService _fileTransferService = new();

    public PeerConnection(string peerIp, int port, string instanceId)
    {
        _peerIp = peerIp;
        _port = port;
        _instanceId = instanceId;
    }

    public event EventHandler<FileTransferProgressEventArgs>? FileTransferProgressChanged
    {
        add => _fileTransferService.ProgressChanged += value;
        remove => _fileTransferService.ProgressChanged -= value;
    }

    public async Task ConnectAndSendTextAsync(string text)
    {
        try
        {
            await _client.ConnectAsync(_peerIp, _port);
            using var stream = _client.GetStream();
            var payload = new ClipboardPayload
            {
                Kind = ClipboardPayloadKind.Text,
                SessionId = _instanceId,
                MessageId = Guid.NewGuid().ToString("N"),
                Text = text
            };

            var json = JsonSerializer.Serialize(payload);
            var body = Encoding.UTF8.GetBytes(json);
            await WriteFrameAsync(stream, body);
            await stream.FlushAsync();
        }
        catch
        {
            // Connection attempts are expected to fail when peers are unavailable.
        }
    }

    public async Task SendPayloadAsync(ClipboardPayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ConnectAsync(_peerIp, _port);
            using var stream = _client.GetStream();

            if (payload.Kind == ClipboardPayloadKind.FileRef && !string.IsNullOrEmpty(payload.LocalPath))
            {
                await SendFilePayloadAsync(stream, payload, cancellationToken);
            }
            else
            {
                var json = JsonSerializer.Serialize(payload);
                var body = Encoding.UTF8.GetBytes(json);
                await WriteFrameAsync(stream, body);
                await stream.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Transfer was cancelled
        }
        catch
        {
            // Connection attempts are expected to fail when peers are unavailable.
        }
    }

    private async Task SendFilePayloadAsync(NetworkStream stream, ClipboardPayload payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(payload.LocalPath))
        {
            return;
        }

        // Send metadata first
        var metaJson = JsonSerializer.Serialize(payload);
        var metaBody = Encoding.UTF8.GetBytes(metaJson);
        await WriteFrameAsync(stream, metaBody);

        // Send file chunks
        await _fileTransferService.SendFileAsync(payload.LocalPath, async chunk =>
        {
            await WriteFrameAsync(stream, chunk);
        }, cancellationToken);

        // Send completion marker (empty chunk)
        await WriteFrameAsync(stream, new byte[0]);
        await stream.FlushAsync();
    }

    private static async Task WriteFrameAsync(NetworkStream stream, byte[] payload)
    {
        var header = new byte[5];
        header[0] = ProtocolVersion;
        header[1] = (byte)((payload.Length >> 24) & 0xFF);
        header[2] = (byte)((payload.Length >> 16) & 0xFF);
        header[3] = (byte)((payload.Length >> 8) & 0xFF);
        header[4] = (byte)(payload.Length & 0xFF);

        await stream.WriteAsync(header, 0, header.Length);
        await stream.WriteAsync(payload, 0, payload.Length);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
