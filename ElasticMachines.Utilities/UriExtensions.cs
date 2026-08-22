namespace ElasticMachines.Utilities;

public static class UriExtensions
{
    public static Uri Combine(this Uri baseUri, string relativeUri)
    {
        var (pathAndQuery, fragment) = relativeUri.Split('#', 2) switch
        {
            [var p] => (p, null),
            [var p, var f] => (p, f),
            _ => throw new ArgumentException("Invalid relative URI")
        };

        var (path, query) = pathAndQuery.Split('?', 2) switch
        {
            [var p] => (p, null),
            [var p, var q] => (p, q),
            _ => throw new ArgumentException("Invalid relative URI")
        };

        var builder = new UriBuilder(baseUri);

        if (fragment != null)
        {
            builder.Fragment = fragment;
        }

        if (query != null)
        {
            var baseQuery = System.Web.HttpUtility.ParseQueryString(baseUri.Query);
            var relativeQuery = System.Web.HttpUtility.ParseQueryString(query);

            foreach (var key in relativeQuery.AllKeys)
            {
                baseQuery[key] = relativeQuery[key];
            }

            builder.Query = baseQuery.ToString();
        }

        if (!string.IsNullOrEmpty(path))
        {
            builder.Path = path.StartsWith('/') ? path : $"{builder.Path.TrimEnd('/')}/{path}";
        }

        return builder.Uri;
    }
}
