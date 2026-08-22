using System.Text.Json;

namespace ElasticMachines.Engine;

public static class Scripts
{
    private static readonly Lazy<string> linuxScript = new(() => GetManifestResource("linux.sh"));
    private static readonly Lazy<string> macosScript = new(() => GetManifestResource("macos.sh"));
    private static readonly Lazy<string> windowsScript = new(() => GetManifestResource("windows.ps1"));

    public static string GetLinuxScript(string jitConfiguration, string downloadUrl, string? downloadToken)
    {
        return PopulateScript(linuxScript.Value, jitConfiguration, downloadUrl, downloadToken);
    }

    public static string GetMacosScript(string jitConfiguration, string downloadUrl, string? downloadToken)
    {
        return PopulateScript(macosScript.Value, jitConfiguration, downloadUrl, downloadToken);
    }

    public static string GetWindowsScript(string jitConfiguration, string downloadUrl, string? downloadToken)
    {
        return PopulateScript(windowsScript.Value, jitConfiguration, downloadUrl, downloadToken);
    }

    private static string PopulateScript(string script, string jitConfiguration, string downloadUrl, string? downloadToken)
    {
        // Temporarily handle JIT configurations manually to support older runners
        // Split the JIT configuration into separate file contents to avoid dealing with JSON in the start script
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(Convert.FromBase64String(jitConfiguration)) ?? throw new Exception("Invalid JIT configuration");

        return script
            .Replace("{{DOWNLOAD_URL}}", downloadUrl)
            .Replace("{{DOWNLOAD_TOKEN}}", downloadToken)
            .Replace("{{DOT_RUNNER_FILE}}", files[".runner"])
            .Replace("{{DOT_CREDENTIALS_FILE}}", files[".credentials"])
            .Replace("{{DOT_CREDENTIALS_RSAPARAMS_FILE}}", files[".credentials_rsaparams"]);
    }

    private static string GetManifestResource(string filename)
    {
        var assembly = typeof(Scripts).Assembly;
        var resourceName = string.Join('.', assembly.GetName().Name, "Resources", filename);

        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new Exception($"Failed to load manifest resource stream for '{filename}'.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
