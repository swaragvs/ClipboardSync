using System.Security.Cryptography;
using System.Text;
using ClipboardSyncApp.Core;

namespace ClipboardSyncApp.Platform.Windows;

public sealed class RemoteClipboardTracker
{
    private class TrackedEntry
    {
        public string ContentHash { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public MessageType Type { get; set; }
    }

    private readonly List<TrackedEntry> _entries = new();
    private readonly object _lock = new();
    private readonly TimeSpan _retentionWindow = TimeSpan.FromSeconds(5);

    public void RecordInjectedRemote(MessageType type, byte[] dataBytes, string messageId)
    {
        var hash = ComputeHash(dataBytes);
        lock (_lock)
        {
            PruneStale();
            _entries.Add(new TrackedEntry
            {
                ContentHash = hash,
                MessageId = messageId,
                TimestampUtc = DateTime.UtcNow,
                Type = type
            });
        }
    }

    public bool IsEcho(MessageType type, byte[] dataBytes)
    {
        var hash = ComputeHash(dataBytes);
        lock (_lock)
        {
            PruneStale();
            var match = _entries.FirstOrDefault(e => e.Type == type && string.Equals(e.ContentHash, hash, StringComparison.Ordinal));
            if (match != null)
            {
                _entries.Remove(match);
                return true;
            }
            return false;
        }
    }

    private void PruneStale()
    {
        var cutoff = DateTime.UtcNow - _retentionWindow;
        _entries.RemoveAll(e => e.TimestampUtc < cutoff);
    }

    public static string ComputeHash(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static string ComputeHash(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return ComputeHash(Encoding.UTF8.GetBytes(text));
    }
}
