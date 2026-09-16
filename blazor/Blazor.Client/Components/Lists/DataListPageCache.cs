// The server-page cache behind every DataList. One instance per WebAssembly application, holding the pages of one
// identity at a time: the scope is the tenant and user the pages were fetched for, and a different scope clears
// everything. An entry is keyed by list, the query (every filter and the sort), page size and page offset, and holds a
// completed successful page only; a failed or cancelled fetch leaves nothing behind.
//
// Bound: at most MaxEntries pages across all lists; storing one more evicts the least recently used page.
// Invalidation: a mutation invalidates a whole list, every filter and sort variant of it. Each fetch carries the
// generation it started in, and a fetch that completes after an invalidation or a scope change is not stored.

namespace Blazor.Client.Components.Lists;

public sealed record DataListPage<TItem>(IReadOnlyList<TItem> Items, int TotalCount);

public readonly record struct DataListCacheToken(string Scope, long Epoch, long ListGeneration);

public sealed class DataListPageCache
{
    public const int MaxEntries = 40;

    private readonly Dictionary<Key, LinkedListNode<(Key Key, object Page)>> _entries = new();
    private readonly Dictionary<string, long> _listGenerations = new(StringComparer.Ordinal);
    private readonly LinkedList<(Key Key, object Page)> _recency = new();
    private long _epoch;
    private string? _scope;

    public int Count => _entries.Count;

    public DataListCacheToken BeginFetch(string scope, string listId)
    {
        EnsureScope(scope);
        return new DataListCacheToken(scope, _epoch, _listGenerations.GetValueOrDefault(listId));
    }

    public bool TryGet<TItem>(string scope, string listId, string queryKey, int pageSize, int pageOffset, out DataListPage<TItem> page)
    {
        EnsureScope(scope);
        if (_entries.TryGetValue(new Key(listId, queryKey, pageSize, pageOffset), out var node) && node.Value.Page is DataListPage<TItem> cached)
        {
            _recency.Remove(node);
            _recency.AddFirst(node);
            page = cached;
            return true;
        }

        page = null!;
        return false;
    }

    // Returns false when the token is stale: the list was invalidated or the scope changed after the fetch started
    public bool Store<TItem>(DataListCacheToken token, string listId, string queryKey, int pageSize, int pageOffset, DataListPage<TItem> page)
    {
        if (token.Scope != _scope || token.Epoch != _epoch || token.ListGeneration != _listGenerations.GetValueOrDefault(listId)) return false;

        var key = new Key(listId, queryKey, pageSize, pageOffset);
        if (_entries.Remove(key, out var existing)) _recency.Remove(existing);
        _entries[key] = _recency.AddFirst((key, page));
        while (_entries.Count > MaxEntries)
        {
            var leastRecent = _recency.Last!;
            _recency.RemoveLast();
            _entries.Remove(leastRecent.Value.Key);
        }

        return true;
    }

    public void Invalidate(string listId)
    {
        _listGenerations[listId] = _listGenerations.GetValueOrDefault(listId) + 1;
        foreach (var node in _entries.Values.Where(node => node.Value.Key.ListId == listId).ToArray())
        {
            _entries.Remove(node.Value.Key);
            _recency.Remove(node);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _recency.Clear();
        _listGenerations.Clear();
        _scope = null;
        _epoch++;
    }

    private void EnsureScope(string scope)
    {
        if (_scope == scope) return;
        Clear();
        _scope = scope;
    }

    private readonly record struct Key(string ListId, string QueryKey, int PageSize, int PageOffset);
}
