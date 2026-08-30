using System.Security.Cryptography;
using ClipboardSyncApp.Storage;

namespace ClipboardSyncApp.Core;

public sealed class ActiveFileTransfer
{
    public string TransferId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ExpectedSHA256 { get; set; } = string.Empty;
    public string PartialFilePath { get; set; } = string.Empty;
    public string FinalFilePath { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public FileStream? FileStream { get; set; }
}

public sealed class FileTransferService
{
    private const int ChunkSizeBytes = 64 * 1024; // 64KB chunks
    public const long MaxIncomingFileSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB limit

    private readonly Dictionary<string, ActiveFileTransfer> _activeTransfers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event EventHandler<FileTransferProgressEventArgs>? ProgressChanged;

    public string CreateFileOffer(string localFilePath, out ClipboardPayload offerPayload)
    {
        if (string.IsNullOrEmpty(localFilePath) || !File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local file not found.", localFilePath);
        }

        var fileInfo = new FileInfo(localFilePath);
        var transferId = Guid.NewGuid().ToString("N");
        var sha256 = ComputeFileSHA256(localFilePath);
        var safeFileName = Path.GetFileName(localFilePath);

        offerPayload = new ClipboardPayload
        {
            Type = MessageType.FileOffer,
            MessageId = Guid.NewGuid().ToString("N"),
            TransferId = transferId,
            FileName = safeFileName,
            FileSize = fileInfo.Length,
            SHA256 = sha256
        };

        return transferId;
    }

    public string PrepareIncomingTransfer(ClipboardPayload offerPayload)
    {
        if (offerPayload == null || string.IsNullOrWhiteSpace(offerPayload.FileName))
        {
            throw new ArgumentException("Invalid file offer payload.");
        }

        if (offerPayload.FileSize > MaxIncomingFileSizeBytes)
        {
            throw new InvalidOperationException($"File size {offerPayload.FileSize} bytes exceeds allowed maximum limit of {MaxIncomingFileSizeBytes} bytes.");
        }

        var transferId = offerPayload.TransferId ?? Guid.NewGuid().ToString("N");
        var safeFileName = Path.GetFileName(offerPayload.FileName);

        var receivedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync", "Received");
        Directory.CreateDirectory(receivedDir);

        var partialPath = Path.Combine(receivedDir, $"{transferId}.partial");
        var finalPath = GetUniqueFilePath(receivedDir, safeFileName);

        var transfer = new ActiveFileTransfer
        {
            TransferId = transferId,
            FileName = safeFileName,
            FileSize = offerPayload.FileSize,
            ExpectedSHA256 = offerPayload.SHA256 ?? string.Empty,
            PartialFilePath = partialPath,
            FinalFilePath = finalPath,
            CreatedUtc = DateTime.UtcNow,
            FileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None)
        };

        lock (_lock)
        {
            _activeTransfers[transferId] = transfer;
            CleanupStaleTransfersInternal();
        }

        return transferId;
    }

    public async Task StreamOutboundFileAsync(string localFilePath, string transferId, Func<ClipboardPayload, Task> sendFrameCallback, CancellationToken cancellationToken)
    {
        if (!File.Exists(localFilePath))
        {
            return;
        }

        var fileInfo = new FileInfo(localFilePath);
        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / ChunkSizeBytes);
        using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var buffer = new byte[ChunkSizeBytes];
        long chunkIndex = 0;
        int bytesRead;

        while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkData = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunkData, 0, bytesRead);

            var chunkPayload = new ClipboardPayload
            {
                Type = MessageType.FileChunk,
                MessageId = Guid.NewGuid().ToString("N"),
                TransferId = transferId,
                ChunkIndex = chunkIndex++,
                TotalChunks = totalChunks,
                ChunkData = chunkData
            };

            await sendFrameCallback(chunkPayload);
            ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(transferId, chunkIndex * ChunkSizeBytes, fileInfo.Length));
        }

        var completePayload = new ClipboardPayload
        {
            Type = MessageType.FileComplete,
            MessageId = Guid.NewGuid().ToString("N"),
            TransferId = transferId
        };
        await sendFrameCallback(completePayload);
    }

    public async Task ProcessIncomingChunkAsync(ClipboardPayload chunkPayload)
    {
        if (chunkPayload == null || string.IsNullOrEmpty(chunkPayload.TransferId) || chunkPayload.ChunkData == null)
        {
            return;
        }

        ActiveFileTransfer? transfer;
        lock (_lock)
        {
            _activeTransfers.TryGetValue(chunkPayload.TransferId, out transfer);
        }

        if (transfer == null || transfer.FileStream == null)
        {
            return;
        }

        await transfer.FileStream.WriteAsync(chunkPayload.ChunkData);
        transfer.BytesReceived += chunkPayload.ChunkData.Length;
        ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(transfer.TransferId, transfer.BytesReceived, transfer.FileSize));
    }

    public async Task<string?> CompleteIncomingTransferAsync(string transferId)
    {
        ActiveFileTransfer? transfer;
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out transfer))
            {
                _activeTransfers.Remove(transferId);
            }
        }

        if (transfer == null || transfer.FileStream == null)
        {
            return null;
        }

        await transfer.FileStream.FlushAsync();
        transfer.FileStream.Dispose();
        transfer.FileStream = null;

        // Verify SHA-256 checksum
        if (!string.IsNullOrEmpty(transfer.ExpectedSHA256))
        {
            var actualSHA256 = ComputeFileSHA256(transfer.PartialFilePath);
            if (!string.Equals(transfer.ExpectedSHA256, actualSHA256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(transfer.PartialFilePath);
                throw new InvalidDataException($"SHA256 checksum mismatch for transfer {transferId}. Partial file deleted.");
            }
        }

        // Atomic move to final path
        if (File.Exists(transfer.FinalFilePath))
        {
            File.Delete(transfer.FinalFilePath);
        }
        File.Move(transfer.PartialFilePath, transfer.FinalFilePath);

        return transfer.FinalFilePath;
    }

    public void CancelTransfer(string transferId)
    {
        ActiveFileTransfer? transfer;
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out transfer))
            {
                _activeTransfers.Remove(transferId);
            }
        }

        if (transfer != null)
        {
            transfer.FileStream?.Dispose();
            if (File.Exists(transfer.PartialFilePath))
            {
                try { File.Delete(transfer.PartialFilePath); } catch { }
            }
        }
    }

    private void CleanupStaleTransfersInternal()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var staleIds = _activeTransfers.Where(x => x.Value.CreatedUtc < cutoff).Select(x => x.Key).ToList();
        foreach (var id in staleIds)
        {
            CancelTransfer(id);
        }
    }

    public static string ComputeFileSHA256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string GetUniqueFilePath(string folder, string fileName)
    {
        var target = Path.Combine(folder, fileName);
        if (!File.Exists(target))
        {
            return target;
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        int counter = 1;

        while (File.Exists(target))
        {
            target = Path.Combine(folder, $"{nameWithoutExt} ({counter++}){ext}");
        }

        return target;
    }
}

public class FileTransferProgressEventArgs : EventArgs
{
    public string TransferId { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }

    public FileTransferProgressEventArgs(string transferId, long bytesTransferred, long totalBytes)
    {
        TransferId = transferId;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
    }
}
