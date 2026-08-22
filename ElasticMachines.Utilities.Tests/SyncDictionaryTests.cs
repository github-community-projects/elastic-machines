using Xunit;

namespace ElasticMachines.Utilities.Tests;

public class SyncDictionaryTests
{
    public SyncDictionaryTests()
    {
    }

    [Fact]
    public void AddItems()
    {
        var dictionary = new SyncDictionary<string, string>(StringComparer.Ordinal, k => k);
        var items = new[] { "a", "b", "c" };

        dictionary.Sync(items);

        Assert.Equal(items, dictionary.Keys.OrderBy(k => k));
    }

    [Fact]
    public void RemoveItems()
    {
        var dictionary = new SyncDictionary<string, string>(StringComparer.Ordinal, k => k);
        var original = new[] { "a", "b", "c" };
        var updated = new[] { "a" };

        dictionary.Sync(original);
        dictionary.Sync(updated);

        Assert.Equal(updated, dictionary.Keys.OrderBy(k => k));
    }

    [Fact]
    public void KeepOriginals()
    {
        var dictionary = new SyncDictionary<string, string>(StringComparer.OrdinalIgnoreCase, k => k);
        var original = new[] { "a", "b", "c" };
        var updated = new[] { "A", "B", "C" };

        dictionary.Sync(original);
        dictionary.Sync(updated);

        Assert.Equal(original, dictionary.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task SyncAsync()
    {
        var dictionary = new SyncDictionary<string, string>(StringComparer.Ordinal, k => k);
        var items = new[] { "a", "b", "c" };

        await dictionary.SyncAsync(items.ToAsyncEnumerable(), CancellationToken.None);

        Assert.Equal(items, dictionary.Keys.OrderBy(k => k));
    }
}
