using System.Security.Cryptography;

namespace ClipboardSyncApp.Core;

public sealed class FileTransferService
{
    private const int ChunkSize = 64 * 1024; // 64KB chunks

    public event EventHandler<FileTransferProgressEventArgs>? ProgressChanged;

    public async Task SendFileAsync(string filePath, Func<byte[], Task> sendChunkAsync, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        var totalBytes = fileInfo.Length;
        var sentBytes = 0L;

        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var buffer = new byte[ChunkSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesRead = await stream.ReadAsync(buffer, 0, ChunkSize, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                // Update hash
                sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);

                // Send chunk
                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);
                await sendChunkAsync(chunk);

                sentBytes += bytesRead;
                ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(sentBytes, totalBytes));
            }

            sha256.TransformFinalBlock(buffer, 0, 0);
            var checksum = sha256.Hash != null ? Convert.ToBase64String(sha256.Hash) : "";
            ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(totalBytes, totalBytes, checksum));
        }
    }

    public async Task ReceiveFileAsync(string outputPath, Func<Task<byte[]?>> receiveChunkAsync, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        using (var sha256 = SHA256.Create())
        using (var stream = File.Create(outputPath))
        {
            var totalBytes = 0L;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = await receiveChunkAsync();
                if (chunk == null || chunk.Length == 0)
                {
                    break;
                }

                sha256.TransformBlock(chunk, 0, chunk.Length, chunk, 0);
                await stream.WriteAsync(chunk, 0, chunk.Length, cancellationToken);
                totalBytes += chunk.Length;

                ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(totalBytes, -1));
            }

            sha256.TransformFinalBlock(new byte[0], 0, 0);
            var checksum = sha256.Hash != null ? Convert.ToBase64String(sha256.Hash) : "";
            ProgressChanged?.Invoke(this, new FileTransferProgressEventArgs(totalBytes, totalBytes, checksum));
        }
    }
}

public sealed class FileTransferProgressEventArgs : EventArgs
{
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public string? Checksum { get; }

    public FileTransferProgressEventArgs(long bytesTransferred, long totalBytes, string? checksum = null)
    {
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        Checksum = checksum;
    }

    public double ProgressPercentage => TotalBytes > 0 ? (BytesTransferred * 100.0) / TotalBytes : 0;
}
