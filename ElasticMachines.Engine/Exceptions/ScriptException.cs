namespace ElasticMachines.Engine.Exceptions;

public class ScriptException : Exception
{
    public string Stdout { get; }
    public string? Stderr { get; }

    public ScriptException(string message, string stdout, string? stderr = null)
        : base($"{message}\nstdout:\n{stdout}\nstderr:\n{stderr}")
    {
        Stdout = stdout;
        Stderr = stderr;
    }
}
