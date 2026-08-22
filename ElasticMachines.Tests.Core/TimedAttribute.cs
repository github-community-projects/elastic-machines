using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace ElasticMachines.Tests.Core;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class TimedAttribute : BeforeAfterTestAttribute
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public override void Before(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} started at {DateTime.UtcNow}");
    }

    public override void After(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} completed in {stopwatch.Elapsed}");
    }
}
