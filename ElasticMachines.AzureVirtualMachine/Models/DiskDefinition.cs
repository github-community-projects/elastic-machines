using Azure.ResourceManager.Compute.Models;

namespace ElasticMachines.AzureVirtualMachine.Models;

public record DiskDefinition
{
    public int? SizeGb { get; init; }
    public StorageAccountType? Sku { get; init; }
    public CachingType? Caching { get; init; }
    public DiffDiskPlacement? EphemeralPlacement { get; init; } // for ephemeral OS disks only
}
