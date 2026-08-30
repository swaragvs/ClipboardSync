namespace ClipboardSyncApp.Core;

public enum QueueItemState
{
    Queued,
    InFlight,
    Completed,
    Failed,
    Cancelled
}

public sealed class PayloadQueue
{
    private readonly List<QueuedPayload> _items = new();
    private readonly int _maxDepth;
    private readonly object _lock = new();

    public class QueuedPayload
    {
        public ClipboardPayload Payload { get; set; } = new();
        public QueueItemState State { get; set; } = QueueItemState.Queued;
        public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    }

    public PayloadQueue(int maxDepth = 20)
    {
        _maxDepth = Math.Max(1, maxDepth);
    }

    public bool Enqueue(ClipboardPayload payload, out CancellationToken cancellationToken)
    {
        if (payload == null)
        {
            cancellationToken = CancellationToken.None;
            return false;
        }

        lock (_lock)
        {
            // Coalesce: if the last QUEUED item is text and new payload is text, replace it
            if (payload.Type == MessageType.ClipboardText)
            {
                var lastQueuedText = _items.LastOrDefault(x => x.State == QueueItemState.Queued && x.Payload.Type == MessageType.ClipboardText);
                if (lastQueuedText != null)
                {
                    _items.Remove(lastQueuedText);
                    lastQueuedText.CancellationTokenSource.Cancel();
                    lastQueuedText.CancellationTokenSource.Dispose();
                }
            }

            // Enforce max depth: drop oldest Queued text item
            var queuedCount = _items.Count(x => x.State == QueueItemState.Queued);
            if (queuedCount >= _maxDepth)
            {
                var oldestQueuedText = _items.FirstOrDefault(x => x.State == QueueItemState.Queued && x.Payload.Type == MessageType.ClipboardText);
                if (oldestQueuedText != null)
                {
                    _items.Remove(oldestQueuedText);
                    oldestQueuedText.CancellationTokenSource.Cancel();
                    oldestQueuedText.CancellationTokenSource.Dispose();
                }
                else if (_items.Count(x => x.State == QueueItemState.Queued) >= _maxDepth)
                {
                    cancellationToken = CancellationToken.None;
                    return false;
                }
            }

            var cts = new CancellationTokenSource();
            var item = new QueuedPayload { Payload = payload, State = QueueItemState.Queued, CancellationTokenSource = cts };
            _items.Add(item);
            cancellationToken = cts.Token;
            return true;
        }
    }

    public bool TryDequeue(out QueuedPayload? queuedItem)
    {
        queuedItem = null;
        lock (_lock)
        {
            var next = _items.FirstOrDefault(x => x.State == QueueItemState.Queued);
            if (next == null)
            {
                return false;
            }

            next.State = QueueItemState.InFlight;
            queuedItem = next;
            return true;
        }
    }

    public void MarkCompleted(QueuedPayload item)
    {
        lock (_lock)
        {
            item.State = QueueItemState.Completed;
            _items.Remove(item);
            item.CancellationTokenSource.Dispose();
        }
    }

    public void MarkFailed(QueuedPayload item)
    {
        lock (_lock)
        {
            item.State = QueueItemState.Failed;
            _items.Remove(item);
            item.CancellationTokenSource.Dispose();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count(x => x.State == QueueItemState.Queued);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var item in _items)
            {
                item.CancellationTokenSource.Cancel();
                item.CancellationTokenSource.Dispose();
            }
            _items.Clear();
        }
    }
}
