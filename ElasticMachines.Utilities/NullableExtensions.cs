using System.Runtime.CompilerServices;

namespace ElasticMachines.Utilities;

public static class NullableExtensions
{
    public static T GetValue<T>(this T? argument, [CallerArgumentExpression(nameof(argument))] string? expression = null) where T : class =>
        argument ?? throw new NullReferenceException(expression);

    public static T GetValue<T>(this T? argument, [CallerArgumentExpression(nameof(argument))] string? expression = null) where T : struct =>
        argument ?? throw new NullReferenceException(expression);
}
