namespace ClipboardSyncApp.Core;

public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private class LruItem
    {
        public TKey Key { get; }
        public TValue Value { get; set; }

        public LruItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<LruItem>> _map;
    private readonly LinkedList<LruItem> _list;
    private readonly object _lock = new();

    public BoundedLruCache(int capacity = 100)
    {
        _capacity = Math.Max(1, capacity);
        _map = new Dictionary<TKey, LinkedListNode<LruItem>>(_capacity);
        _list = new LinkedList<LruItem>();
    }

    public bool ContainsKey(TKey key)
    {
        lock (_lock)
        {
            return _map.ContainsKey(key);
        }
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existingNode))
            {
                _list.Remove(existingNode);
                existingNode.Value.Value = value;
                _list.AddFirst(existingNode);
                return;
            }

            if (_map.Count >= _capacity)
            {
                var lastNode = _list.Last;
                if (lastNode != null)
                {
                    _map.Remove(lastNode.Value.Key);
                    _list.RemoveLast();
                }
            }

            var item = new LruItem(key, value);
            var newNode = new LinkedListNode<LruItem>(item);
            _list.AddFirst(newNode);
            _map[key] = newNode;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _list.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }
}
