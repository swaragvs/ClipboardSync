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
    private readonly TimeSpan _cooldownWindow = TimeSpan.FromSeconds(2.0);

    private DateTime _lastInjectedUtc = DateTime.MinValue;
    private string _lastInjectedHash = string.Empty;

    private DateTime _lastSentUtc = DateTime.MinValue;
    private string _lastSentHash = string.Empty;

    public void RecordInjectedRemote(MessageType type, byte[] dataBytes, string messageId)
    {
        var hash = ComputeHash(dataBytes);
        lock (_lock)
        {
            _lastInjectedUtc = DateTime.UtcNow;
            _lastInjectedHash = hash;
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

    public void RecordSentLocal(MessageType type, byte[] dataBytes)
    {
        var hash = ComputeHash(dataBytes);
        lock (_lock)
        {
            _lastSentUtc = DateTime.UtcNow;
            _lastSentHash = hash;
            PruneStale();
            _entries.Add(new TrackedEntry
            {
                ContentHash = hash,
                MessageId = "LOCAL_SENT",
                TimestampUtc = DateTime.UtcNow,
                Type = type
            });
        }
    }

    public bool ShouldSuppressLocalChange(MessageType type, byte[] dataBytes)
    {
        var hash = ComputeHash(dataBytes);
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            PruneStale();

            // 1. Break rule: If a remote update was injected within the last 2 seconds,
            // suppress all local watcher updates during this 2-second cooldown window to break feedback loops.
            if (now - _lastInjectedUtc < _cooldownWindow)
            {
                return true;
            }

            // 2. If the same content was sent locally within the last 2 seconds, suppress re-sending.
            if (now - _lastSentUtc < _cooldownWindow && string.Equals(_lastSentHash, hash, StringComparison.Ordinal))
            {
                return true;
            }

            // 3. Match any known tracked entry in retention window
            var match = _entries.FirstOrDefault(e => e.Type == type && string.Equals(e.ContentHash, hash, StringComparison.Ordinal));
            return match != null;
        }
    }

    public bool IsEcho(MessageType type, byte[] dataBytes)
    {
        return ShouldSuppressLocalChange(type, dataBytes);
    }

    private void PruneStale()
    {
        var cutoff = DateTime.UtcNow - _retentionWindow;
        _entries.RemoveAll(e => e.TimestampUtc < cutoff);
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\0');
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
        return ComputeHash(Encoding.UTF8.GetBytes(NormalizeText(text)));
    }
}
