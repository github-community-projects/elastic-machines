using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ElasticMachines.Monolith.Services;

public class ActivityService<T> : IActivityService<T>, IDisposable
{
    private readonly ActivitySource activitySource;

    public ActivityService()
    {
        activitySource = new(typeof(T).ToString(), typeof(T).Assembly.GetName().Version?.ToString());
    }

    public Activity? StartActivity([CallerMemberName] string name = "")
    {
        return activitySource.StartActivity(name);
    }

    public void Dispose()
    {
        activitySource.Dispose();
        GC.SuppressFinalize(this);
    }
}
