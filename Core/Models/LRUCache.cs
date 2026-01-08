using System.Collections.Generic;

namespace UnitySerializationBridge.Core;

// Will be useful where there's excessive amounts of items to cache
internal class LRUCache<TKey, TValue>(int maxSize)
{
    private readonly int _maxSize = maxSize;
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache = [];
    private readonly LinkedList<CacheItem> _history = new();

    // Currently not needed
    // public TValue this[TKey key]
    // {
    //     get
    //     {
    //         if (TryGetValue(key, out TValue value))
    //             return value;
    //         throw new KeyNotFoundException($"{key} not found in LRUCache.");
    //     }
    // }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_cache.TryGetValue(key, out var node))
        {
            // Move to front (Most Recently Used)
            _history.Remove(node);
            _history.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        if (_cache.Count >= _maxSize && !_cache.ContainsKey(key))
        {
            // Remove the oldest (Last in the list)
            var last = _history.Last;
            _cache.Remove(last.Value.Key);
            _history.RemoveLast();
        }

        var newNode = new LinkedListNode<CacheItem>(new CacheItem(key, value));
        _history.AddFirst(newNode);
        _cache[key] = newNode;
    }

    private record struct CacheItem(TKey Key, TValue Value);
}