using Azure.Core;
using Azure.ResourceManager.Compute.Models;

namespace ElasticMachines.AzureVirtualMachine.Models;

public record Configuration(string ResourceGroup, string Image, VirtualMachineSizeType VmSku)
{
    public List<DiskDefinition>? DataDisks { get; init; }
    public List<VirtualMachineExtension>? Extensions { get; init; }
    public List<ResourceIdentifier>? Identities { get; init; }
    public string? LicenseType { get; init; }
    public AzureLocation? Location { get; init; }
    public DiskDefinition? OsDisk { get; init; }
    public bool PublicIpPerVm { get; init; }
    public SecurityProfile? SecurityProfile { get; init; }
    public double? SpotMaxPrice { get; init; }
    public List<ResourceIdentifier>? Subnets { get; init; }
    public string? Subscription { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public string? Zone { get; init; }
}
