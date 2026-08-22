using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.ManagedServiceIdentities;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using ElasticMachines.AzureVirtualMachine.Models;
using ElasticMachines.Tests.Core;
using ElasticMachines.Utilities;

namespace ElasticMachines.AzureVirtualMachine.Tests;

public class CloudProviderFixture : BaseCloudProviderFixture
{
    public string? SubscriptionId => configuredSubscriptionId ?? Subscription?.Data.SubscriptionId;
    public AzureLocation Location { get; }
    public SubscriptionResource? Subscription { get; private set; }
    public string ResourceGroupName { get; private set; }
    public ResourceGroupResource? ResourceGroup { get; private set; }
    public string SidecarResourceGroupName { get; private set; }
    public ResourceGroupResource? SidecarResourceGroup { get; private set; }
    public ArmClient ArmClient { get; }
    public string ImageGalleryResourceGroup { get; }
    public string ImageGalleryName { get; }
    public Configuration Configuration { get; }

    private readonly string? configuredSubscriptionId;

    public CloudProviderFixture() : base("azure-virtual-machine")
    {
        configuredSubscriptionId = Settings["subscription"];
        Location = Settings["location"].GetValue();
        ResourceGroupName = $"{TestSuiteName}-azure-virtual-machine";
        SidecarResourceGroupName = $"{TestSuiteName}-azure-virtual-machine-sidecar";
        ArmClient = new ArmClient(new ChainedTokenCredential(new EnvironmentCredential(), new AzureCliCredential()));

        Configuration = new(ResourceGroup: ResourceGroupName, Image: "Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest", VmSku: VirtualMachineSizeType.StandardDS1V2)
        {
            Subscription = SubscriptionId,
            Location = Location
        };

        ImageGalleryResourceGroup = Settings["imageGalleryResourceGroup"] ?? "elastic-machines-dev";
        ImageGalleryName = Settings["imageGallery"] ?? "elastic_machines_gallery";
    }

    public override async Task InitializeAsync()
    {
        var baseTask = base.InitializeAsync();

        Subscription = configuredSubscriptionId == null
            ? await ArmClient.GetDefaultSubscriptionAsync()
            : await ArmClient.GetSubscriptions().GetAsync(configuredSubscriptionId);

        var resourceGroups = Subscription.GetResourceGroups();
        var resourceGroupTask = resourceGroups.CreateOrUpdateAsync(WaitUntil.Completed, ResourceGroupName, new ResourceGroupData(Location));
        var sidecarTask = resourceGroups.CreateOrUpdateAsync(WaitUntil.Completed, SidecarResourceGroupName, new ResourceGroupData(Location));

        ResourceGroup = (await resourceGroupTask).Value;
        SidecarResourceGroup = (await sidecarTask).Value;

        await baseTask;
    }

    public async Task<VirtualNetworkResource> CreateVirtualNetwork()
    {
        var publicIPAddressData = new PublicIPAddressData()
        {
            Location = Location,
            Sku = new() { Name = "Standard" },
            PublicIPAllocationMethod = NetworkIPAllocationMethod.Static
        };

        var publicIPAddress = await SidecarResourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, "sidecar-ip", publicIPAddressData).ConfigureAwait(false);

        var natGatewayData = new NatGatewayData()
        {
            Location = Location,
            SkuName = NatGatewaySkuName.Standard,
            PublicIPAddresses =
                {
                    new() { Id = publicIPAddress.Value.Id }
                }
        };

        var natGateway = await SidecarResourceGroup.GetNatGateways().CreateOrUpdateAsync(WaitUntil.Completed, "sidecar-gateway", natGatewayData).ConfigureAwait(false);

        var networkSecurityGroupData = new NetworkSecurityGroupData() { Location = Location };
        var networkSecurityGroup = await SidecarResourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, "sidecar-nsg", networkSecurityGroupData).ConfigureAwait(false);

        var virtualNetworkData = new VirtualNetworkData()
        {
            Location = Location,
            AddressPrefixes = { "10.1.0.0/16" },
            Subnets =
            {
                new()
                {
                    Name = "default",
                    AddressPrefix = "10.1.0.0/24",
                    NatGatewayId = natGateway.Value.Id,
                    NetworkSecurityGroup = new() { Id = networkSecurityGroup.Value.Id }
                },
                new()
                {
                    Name = "second-subnet",
                    AddressPrefix = "10.1.1.0/24",
                    NatGatewayId = natGateway.Value.Id,
                    NetworkSecurityGroup = new() { Id = networkSecurityGroup.Value.Id }
                }
            }
        };

        var virtualNetwork = await SidecarResourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, "sidecar-vnet", virtualNetworkData);

        return virtualNetwork.Value;
    }

    public async Task<UserAssignedIdentityResource> CreateUserAssignedIdentity(ResourceGroupResource? resourceGroup = null, string? name = null)
    {
        var sidecarIdentity = await (resourceGroup ?? SidecarResourceGroup)
            .GetUserAssignedIdentities()
            .CreateOrUpdateAsync(WaitUntil.Completed, name ?? "sidecar-identity", new UserAssignedIdentityData(Location));

        return sidecarIdentity.Value;
    }

    public override async Task DisposeAsync()
    {
        var tasks = new[]
        {
            base.DisposeAsync(),
            ResourceGroup?.DeleteAsync(WaitUntil.Started),
            SidecarResourceGroup?.DeleteAsync(WaitUntil.Started)
        };

        await Task.WhenAll(tasks.OfType<Task>());
    }
}
