using System.Diagnostics;

namespace ElasticMachines.Utilities;

public static class ActivitySourceExtensions
{
    public static Activity? StartActivity(this ActivitySource activitySource, string name, IEnumerable<KeyValuePair<string, object?>> tags)
    {
        return activitySource.StartActivity(ActivityKind.Internal, tags: tags, name: name);
    }
}
