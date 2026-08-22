using System.Runtime.CompilerServices;

namespace ElasticMachines.Utilities;

/// <summary>
/// Utility class to consistently get the path to this project ElasticMachines.Engine in any context
/// Usage: Directory.GetParent(ProjectSourcePath.Value)?.Parent?.FullName -> Gets elastic-machines root directory
/// Credit: https://stackoverflow.com/a/66285728
/// </summary>
public static class ProjectSourcePath
{
    private const string myRelativePath = nameof(ProjectSourcePath) + ".cs";
    private static string? lazyValue;
    public static string Value => lazyValue ??= calculatePath();

    private static string calculatePath()
    {
        string pathName = GetSourceFilePathName();
        return pathName.Substring(0, pathName.Length - myRelativePath.Length);
    }

    public static string GetSourceFilePathName([CallerFilePath] string? callerFilePath = null) => callerFilePath ?? "";
}
