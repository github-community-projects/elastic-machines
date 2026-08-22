using System.Diagnostics;

namespace ElasticMachines.Utilities;

public static class TelemetryEnricher
{
    public static void AttachHeaders(Activity activity, HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
        {
            if (header.Key.Contains("-RateLimit-", StringComparison.OrdinalIgnoreCase))
            {
                activity.SetTag($"http.response.header.{header.Key}", header.Value.FirstOrDefault());
            }
        }
    }
}
