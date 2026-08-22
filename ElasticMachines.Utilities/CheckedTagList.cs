using System.Collections;

namespace ElasticMachines.Utilities;

public class CheckedTagList : IEnumerable
{
    private readonly Dictionary<string, object?> tags = new();

    public CheckedTagList()
    {
    }

    public CheckedTagList(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            Add(tag.Key, tag.Value);
        }
    }

    public void Add(string key, object? value)
    {
        if (value != null)
        {
            tags.Add(key, value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return tags.GetEnumerator();
    }

    public static implicit operator ReadOnlySpan<KeyValuePair<string, object?>>(CheckedTagList checkedTagList)
    {
        return checkedTagList.tags.ToArray();
    }
}
