namespace ElasticMachines.Utilities;

public static class HashSetExtensions
{
    public static int ExceptWithCount<T>(this HashSet<T> source, Func<T, bool> predicate, int count)
    {
        var initial = source.Count;
        source.ExceptWith(source.Where(predicate).Take(count));

        return initial - source.Count;
    }
}
