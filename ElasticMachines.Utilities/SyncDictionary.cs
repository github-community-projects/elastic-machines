using System.Collections.Concurrent;

namespace ElasticMachines.Utilities;

public class SyncDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Func<TKey, TValue> factory;

    public SyncDictionary(IEqualityComparer<TKey> comparer, Func<TKey, TValue> factory) : base(comparer)
    {
        this.factory = factory;
    }

    public TValue GetOrAdd(TKey key)
    {
        return GetOrAdd(key, factory);
    }

    public void Sync(IEnumerable<TKey> keys)
    {
        var updatedKeys = keys as HashSet<TKey> ?? keys.ToHashSet(Comparer);

        foreach (var key in Keys)
        {
            if (!updatedKeys.Contains(key))
            {
                TryRemove(key, out _);
            }
        }

        foreach (var key in updatedKeys)
        {
            GetOrAdd(key, factory);
        }
    }

    public async Task SyncAsync(IAsyncEnumerable<TKey> keys, CancellationToken cancellationToken)
    {
        Sync(await keys.ToHashSetAsync(Comparer, cancellationToken).ConfigureAwait(false));
    }
}
