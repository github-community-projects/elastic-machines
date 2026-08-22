namespace ElasticMachines.AzureVirtualMachine.Models;

public record VirtualMachineExtension(string Name, string Publisher, string Version)
{
    public Dictionary<string, object>? Settings { get; set; }
    public Dictionary<string, object>? ProtectedSettings { get; set; }
}
