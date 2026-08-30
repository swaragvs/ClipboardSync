namespace ClipboardSyncApp.Core;

public sealed class PayloadQueue
{
    private readonly Queue<QueuedPayload> _queue = new();
    private readonly int _maxDepth;
    private readonly object _lock = new();

    private class QueuedPayload
    {
        public ClipboardPayload Payload { get; set; } = new();
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
            // Coalesce: if the last queued item is text and new one is text, replace it
            if (payload.Kind == ClipboardPayloadKind.Text && _queue.Count > 0)
            {
                var last = _queue.Peek();
                if (last.Payload.Kind == ClipboardPayloadKind.Text && !last.CancellationTokenSource.Token.IsCancellationRequested)
                {
                    _queue.Dequeue();
                    last.CancellationTokenSource.Cancel();
                    last.CancellationTokenSource.Dispose();
                }
            }

            // Enforce max depth: drop oldest non-file item
            if (_queue.Count >= _maxDepth)
            {
                var oldestNonFile = _queue.FirstOrDefault(q => q.Payload.Kind == ClipboardPayloadKind.Text);
                if (oldestNonFile != null)
                {
                    var temp = new Queue<QueuedPayload>(_queue.Where(q => q != oldestNonFile));
                    while (_queue.Count > 0)
                    {
                        _queue.Dequeue();
                    }
                    foreach (var item in temp)
                    {
                        _queue.Enqueue(item);
                    }
                    oldestNonFile.CancellationTokenSource.Dispose();
                }
                else if (_queue.Count >= _maxDepth)
                {
                    // If all are files and we're at max, reject
                    cancellationToken = CancellationToken.None;
                    return false;
                }
            }

            var cts = new CancellationTokenSource();
            _queue.Enqueue(new QueuedPayload { Payload = payload, CancellationTokenSource = cts });
            cancellationToken = cts.Token;
            return true;
        }
    }

    public bool TryDequeue(out ClipboardPayload? payload, out CancellationToken cancellationToken)
    {
        payload = null;
        cancellationToken = CancellationToken.None;

        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                return false;
            }

            var queued = _queue.Dequeue();
            payload = queued.Payload;
            cancellationToken = queued.CancellationTokenSource.Token;
            return true;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var item in _queue)
            {
                item.CancellationTokenSource.Cancel();
                item.CancellationTokenSource.Dispose();
            }

            _queue.Clear();
        }
    }
}
