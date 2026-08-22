using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ElasticMachines.Monolith.Services;

public interface IActivityService<out T>
{
    public Activity? StartActivity([CallerMemberName] string name = "");
}
