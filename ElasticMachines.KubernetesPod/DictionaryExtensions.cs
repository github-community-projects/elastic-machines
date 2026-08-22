namespace ElasticMachines.KubernetesPod;

public static class DictionaryExtensions
{
    public static void MergeIn<TKey, TVal>(this IDictionary<TKey, TVal> source, params IDictionary<TKey, TVal>[] other)
    {
        foreach (var dict in other)
        {
            foreach (var (key, value) in dict)
            {
                source[key] = value;
            }
        }
    }

    public static string FormatAsSelectorString(this IDictionary<string, string> labels)
    {
        return string.Join(',', labels.Select(s => $"{s.Key}={s.Value}"));
    }
}
