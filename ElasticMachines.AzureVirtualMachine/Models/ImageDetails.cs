using Azure.ResourceManager.Compute.Models;

namespace ElasticMachines.AzureVirtualMachine.Models
{
    internal class ImageDetails
    {
        public OperatingSystemStateType? OSState { get; set; }
        public SupportedOperatingSystemType? OSType { get; set; }
        public ImageReference? ImageReference { get; set; }
        public IList<VirtualMachineDataDisk>? DataDisks { get; set; }
        public ComputePlan? Plan { get; set; }
        public ArchitectureType? Architecture { get; set; }
    }
}
