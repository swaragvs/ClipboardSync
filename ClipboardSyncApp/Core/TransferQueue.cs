namespace ClipboardSyncApp.Core;

public sealed class TransferQueue
{
    private readonly Queue<string> _queue = new();
    private readonly int _maxDepth;

    public TransferQueue(int maxDepth = 20)
    {
        _maxDepth = Math.Max(1, maxDepth);
    }

    public bool Enqueue(string item)
    {
        if (string.IsNullOrEmpty(item))
        {
            return false;
        }

        if (_queue.Count >= _maxDepth)
        {
            _queue.Dequeue();
        }

        _queue.Enqueue(item);
        return true;
    }

    public string? Dequeue()
    {
        return _queue.Count > 0 ? _queue.Dequeue() : null;
    }

    public int Count => _queue.Count;
}
