using System.Diagnostics.CodeAnalysis;

namespace ElasticMachines.Utilities;

public class UniqueQueue<T>
{
    private readonly Queue<T> queue = new();
    private readonly HashSet<T> hashSet = new();

    public int Count => Math.Max(queue.Count, hashSet.Count);

    public UniqueQueue()
    {
    }

    public void Enqueue(T item)
    {
        if (hashSet.Add(item))
        {
            queue.Enqueue(item);
        }
    }

    public void EnqueueRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Enqueue(item);
        }
    }

    public bool TryDequeue([MaybeNullWhen(false)] out T item)
    {
        return queue.TryDequeue(out item) && hashSet.Remove(item);
    }

    public IEnumerable<T> DequeueRange(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!TryDequeue(out var item))
                yield break;

            yield return item;
        }
    }
}
