using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using ElasticMachines.AzureVirtualMachine.Models;
using ElasticMachines.Engine;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.Engine.Models;
using ElasticMachines.Utilities;

namespace ElasticMachines.AzureVirtualMachine;

[CloudProvider("azure-virtual-machine")]
[CloudProvider("azure-virtual-machine@v0")]
[CloudProvider("azure-virtual-machine@v1")]
public class CloudProvider : CloudProvider<Models.Configuration>, IAsyncDisposable
{
    protected static ArmClient ArmClient { get; } = new
    (
        new ChainedTokenCredential(new EnvironmentCredential(), new AzureCliCredential(), new ManagedIdentityCredential(new DefaultAzureCredentialOptions().ManagedIdentityClientId)),
        null,
        new()
        {
            Environment = Environment.GetEnvironmentVariable("AZURE_RESOURCE_MANAGER_ENDPOINT") is string endpoint
                ? new ArmEnvironment(new Uri(endpoint), endpoint)
                : ArmEnvironment.AzurePublicCloud,
        }
    );

    private readonly AsyncExpiringLazy<SubscriptionResource> lazySubscription;
    private readonly AsyncExpiringLazy<ResourceGroupResource> lazyResourceGroup;
    private readonly AsyncExpiringLazy<List<VirtualMachineExtensionData>> lazyExtensionData;
    private readonly AsyncExpiringLazy<AzureLocation> lazyLocation;
    private readonly AsyncExpiringLazy<List<SubnetResource>> lazySubnets;
    private readonly AsyncExpiringLazy<ImageDetails> lazyImageDetails;

    public CloudProvider(Models.Configuration configuration, Dictionary<string, string> tags) : base(configuration, tags)
    {
        lazySubscription = new(GetSubscriptionAsync);
        lazyResourceGroup = new(GetResourceGroupAsync);
        lazyExtensionData = new(GetExtensionDataAsync);
        lazyLocation = new(GetLocationAsync);
        lazySubnets = new(GetSubnetsAsync);
        lazyImageDetails = new(GetImageDetailsAsync, TimeSpan.FromHours(1));
    }

    public override async Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            (name, var virtualMachineData) = await GetVirtualMachineDataAsync(name, cancellationToken).ConfigureAwait(false);

            var subscription = await lazySubscription.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var resourceGroup = await lazyResourceGroup.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var operation = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Started, name, virtualMachineData, cancellationToken).ConfigureAwait(false);
            var response = await operation.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            var extensions = await lazyExtensionData.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(extensions.Select(e => response.Value.GetVirtualMachineExtensions().CreateOrUpdateAsync(WaitUntil.Completed, e.ExtensionType, e, cancellationToken))).ConfigureAwait(false);

            return response.Value.Data.Name;
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    public override async Task DeleteMachineAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var vmId = await GetVirtualMachineResourceIdentifierAsync(id, cancellationToken).ConfigureAwait(false);
            await ArmClient.GetVirtualMachineResource(vmId).DeleteAsync(WaitUntil.Completed, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    public override async Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken)
    {
        try
        {
            var (_, virtualMachineData) = await GetVirtualMachineDataAsync("", cancellationToken).ConfigureAwait(false);
            var imageDetails = await lazyImageDetails.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var architecture = imageDetails.Architecture.GetValueOrDefault() == ArchitectureType.Arm64 ? Architecture.Arm64 : Architecture.X64;

            var operatingSystem = virtualMachineData.StorageProfile.OSDisk.OSType switch
            {
                SupportedOperatingSystemType.Linux => Engine.Models.OperatingSystem.Linux,
                SupportedOperatingSystemType.Windows => Engine.Models.OperatingSystem.Windows,
                _ => throw new Exception($"Unsupported operating system '{virtualMachineData.StorageProfile.OSDisk.OSType}'")
            };

            var (url, token) = await getRunnerDownloadAsync(operatingSystem, architecture, cancellationToken).ConfigureAwait(false);

            var (stdout, stderr) = operatingSystem switch
            {
                Engine.Models.OperatingSystem.Linux => await RunShellScriptOnMachineAsync(name, Scripts.GetLinuxScript(jitConfiguration, url.AbsoluteUri, token), cancellationToken).ConfigureAwait(false),
                Engine.Models.OperatingSystem.Windows => await RunPowershellScriptOnMachineAsync(name, Scripts.GetWindowsScript(jitConfiguration, url.AbsoluteUri, token), cancellationToken).ConfigureAwait(false),
                _ => throw new Exception($"Unsupported operating system '{operatingSystem}'")
            };

            if (!stdout.TrimEnd().EndsWith($"Started runner in background"))
            {
                throw new ScriptException("Script didn't start runner", stdout, stderr);
            }
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    public override async Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var resourceGroup = await lazyResourceGroup.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var virtualMachines = await resourceGroup
                .GetVirtualMachines()
                .GetAllAsync(cancellationToken: cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return virtualMachines
                .Where(vm => Tags.All(t => vm.Data.Tags.TryGetValue(t.Key, out var value) && value == t.Value))
                .Select(vm => vm.Data.Name)
                .ToList();
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    public async Task<(string name, VirtualMachineData virtualMachineData)> GetVirtualMachineDataAsync(string name, CancellationToken cancellationToken)
    {
        var location = await lazyLocation.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var subnets = await lazySubnets.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var imageDetails = await lazyImageDetails.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var virtualMachineData = new VirtualMachineData(location)
        {
            HardwareProfile = new() { VmSize = Configuration.VmSku },
            Identity = GetManagedIdentity(),
            LicenseType = Configuration.LicenseType,
            NetworkProfile = new() { NetworkApiVersion = NetworkApiVersion.TwoThousandTwenty1101 },
            Plan = imageDetails.Plan,
            StorageProfile = new()
            {
                ImageReference = imageDetails.ImageReference,
                OSDisk = GetOSDisk(imageDetails)
            },
        };

        if (imageDetails.OSState == OperatingSystemStateType.Generalized)
        {
            virtualMachineData.OSProfile = new()
            {
                AdminPassword = Guid.NewGuid().ToString(),
                AdminUsername = "azureuser",
                ComputerName = imageDetails.OSType == SupportedOperatingSystemType.Windows ? "runner" : name.Replace("_", "-"),
                WindowsConfiguration = imageDetails.OSType == SupportedOperatingSystemType.Windows ? new() { EnableAutomaticUpdates = false } : null
            };
        }

        foreach (var tag in Tags)
        {
            virtualMachineData.Tags.Add(tag);
        }

        // Attach any user configured tags that don't conflict with Elastic Machines internal tracking tags
        foreach (var tag in Configuration.Tags ?? [])
        {
            virtualMachineData.Tags.TryAdd(tag.Key, tag.Value);
        }

        var dataDisks = GetDataDisks(imageDetails.DataDisks);

        foreach (var dataDisk in dataDisks)
        {
            virtualMachineData.StorageProfile.DataDisks.Add(dataDisk);
        }

        if (dataDisks.Any(d => d.ManagedDisk?.StorageAccountType == StorageAccountType.UltraSsdLrs))
        {
            virtualMachineData.AdditionalCapabilities = new() { UltraSsdEnabled = true };
        }

        for (var i = 0; i < subnets.Count; i++)
        {
            virtualMachineData.NetworkProfile.NetworkInterfaceConfigurations.Add(new($"{name}-{i + 1}")
            {
                Primary = i == 0,
                IPConfigurations =
                {
                    new($"{name}-{i + 1}")
                    {
                        SubnetId = subnets[i].Id,
                        PublicIPAddressConfiguration = Configuration.PublicIpPerVm ? new($"{name}-{i + 1}") : null,
                    }
                }
            });
        }

        if (Configuration.Zone != null)
        {
            virtualMachineData.Zones.Add(Configuration.Zone);
        }

        if (Configuration.SpotMaxPrice != null)
        {
            virtualMachineData.BillingMaxPrice = Configuration.SpotMaxPrice;
            virtualMachineData.Priority = VirtualMachinePriorityType.Spot;
            virtualMachineData.EvictionPolicy = VirtualMachineEvictionPolicyType.Delete;
        }

        if (Configuration.SecurityProfile != null)
        {
            virtualMachineData.SecurityProfile = new() { EncryptionAtHost = Configuration.SecurityProfile.EncryptionAtHost };
        }

        return (name, virtualMachineData);
    }

    private async Task<List<VirtualMachineExtensionData>> GetExtensionDataAsync(CancellationToken cancellationToken)
    {
        var location = await lazyLocation.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var extensions = Configuration.Extensions ?? Enumerable.Empty<VirtualMachineExtension>();

        return extensions.Select(e => new VirtualMachineExtensionData(location)
        {
            ExtensionType = e.Name,
            Publisher = e.Publisher,
            TypeHandlerVersion = e.Version,
            Settings = e.Settings != null ? new(e.Settings) : null,
            ProtectedSettings = e.ProtectedSettings != null ? new(e.ProtectedSettings) : null
        }).ToList();
    }

    private async Task<SubscriptionResource> GetSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (Configuration.Subscription == null)
        {
            return await ArmClient.GetDefaultSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        }

        var subscription = await ArmClient.GetSubscriptions().FirstOrDefaultAsync(s => s.Data.SubscriptionId == Configuration.Subscription || s.Data.DisplayName == Configuration.Subscription, cancellationToken).ConfigureAwait(false);

        if (subscription == null)
        {
            throw new Exception($"Subscription '{Configuration.Subscription}' did not match any accessible subscription names or IDs using the provided azure credentials");
        }

        return subscription;
    }

    private async Task<ResourceGroupResource> GetResourceGroupAsync(CancellationToken cancellationToken)
    {
        var subscription = await lazySubscription.GetValueAsync(cancellationToken).ConfigureAwait(false);

        if (await subscription.GetResourceGroups().ExistsAsync(Configuration.ResourceGroup, cancellationToken).ConfigureAwait(false))
        {
            return await subscription.GetResourceGroups().GetAsync(Configuration.ResourceGroup, cancellationToken).ConfigureAwait(false);
        }
        else if (Configuration.Location.HasValue)
        {
            return (await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, Configuration.ResourceGroup, new(Configuration.Location.Value), cancellationToken).ConfigureAwait(false)).Value;
        }
        else
        {
            throw new Exception($"Resource group '{Configuration.ResourceGroup}' does not exist and cannot be created because no location specified");
        }
    }

    private async Task<AzureLocation> GetLocationAsync(CancellationToken cancellationToken)
    {
        return Configuration.Location ?? (await lazyResourceGroup.GetValueAsync(cancellationToken).ConfigureAwait(false)).Data.Location;
    }

    private async Task<List<SubnetResource>> GetSubnetsAsync(CancellationToken cancellationToken)
    {
        if (Configuration.Subnets is not null && Configuration.Subnets.Count > 0)
        {
            var configuredSubnets = await Task.WhenAll(Configuration.Subnets.Select(s => ArmClient.GetSubnetResource(s).GetAsync())).ConfigureAwait(false);

            return configuredSubnets.Select(s => s.Value).ToList();
        }

        var resourceGroup = await lazyResourceGroup.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var location = await lazyLocation.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var name = $"{resourceGroup.Data.Name}-{location}";

        var publicIPAddressData = new PublicIPAddressData()
        {
            Location = location,
            Sku = new() { Name = "Standard" },
            PublicIPAllocationMethod = NetworkIPAllocationMethod.Static,
        };

        var publicIPAddress = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, name, publicIPAddressData, cancellationToken).ConfigureAwait(false);

        var natGatewayData = new NatGatewayData()
        {
            Location = location,
            SkuName = NatGatewaySkuName.Standard,
            PublicIPAddresses =
            {
                new() { Id = publicIPAddress.Value.Id }
            },
        };

        var natGateway = await resourceGroup.GetNatGateways().CreateOrUpdateAsync(WaitUntil.Completed, name, natGatewayData, cancellationToken).ConfigureAwait(false);

        var networkSecurityGroupData = new NetworkSecurityGroupData() { Location = location };
        var networkSecurityGroup = await resourceGroup.GetNetworkSecurityGroups().CreateOrUpdateAsync(WaitUntil.Completed, name, networkSecurityGroupData, cancellationToken).ConfigureAwait(false);

        var virtualNetworkData = new VirtualNetworkData()
        {
            Location = location,
            AddressPrefixes = { "10.1.0.0/16" },
            Subnets =
            {
                new()
                {
                    Name = "default",
                    AddressPrefix = "10.1.0.0/24",
                    NatGatewayId = natGateway.Value.Id,
                    NetworkSecurityGroup = new() { Id = networkSecurityGroup.Value.Id }
                }
            }
        };

        var virtualNetwork = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, name, virtualNetworkData, cancellationToken).ConfigureAwait(false);
        var subnets = await Task.WhenAll(virtualNetwork.Value.Data.Subnets.Select(s => ArmClient.GetSubnetResource(s.Id).GetAsync())).ConfigureAwait(false);

        return subnets.Select(s => s.Value).ToList();
    }

    private async Task<ImageDetails> GetImageDetailsAsync(CancellationToken cancellationToken)
    {
        return Configuration.Image switch
        {
            var s when s.Split(':') is { Length: 4 } terms => await GetMarketplaceImageDetailsAsync(terms[0], terms[1], terms[2], terms[3], cancellationToken).ConfigureAwait(false),
            var s when s.StartsWith("/subscriptions/") => await GetGalleryImageDetailsAsync(new ResourceIdentifier(s)).ConfigureAwait(false),
            _ => throw new Exception($"Unknown type of image '{Configuration.Image}'"),
        };
    }

    private ManagedServiceIdentity? GetManagedIdentity()
    {
        if (Configuration.Identities == null)
        {
            return null;
        }

        var managedServiceIdentity = new ManagedServiceIdentity(ManagedServiceIdentityType.UserAssigned);

        foreach (var identity in Configuration.Identities)
        {
            managedServiceIdentity.UserAssignedIdentities.Add(identity, new UserAssignedIdentity());
        }

        return managedServiceIdentity;
    }

    private async Task<ImageDetails> GetMarketplaceImageDetailsAsync(string publisher, string offer, string sku, string version, CancellationToken cancellationToken)
    {
        var subscription = await lazySubscription.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var location = await lazyLocation.GetValueAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            var images = subscription.GetVirtualMachineImagesAsync(location, publisher, offer, sku, top: 1, orderby: "name desc", cancellationToken: cancellationToken);
            version = await images.Select(i => i.Name).SingleAsync(cancellationToken).ConfigureAwait(false);
        }

        var image = await subscription.GetVirtualMachineImageAsync(location, publisher, offer, sku, version, cancellationToken).ConfigureAwait(false);

        var details = new ImageDetails()
        {
            OSState = OperatingSystemStateType.Generalized,
            OSType = image.Value.OSDiskImageOperatingSystem,
            ImageReference = new()
            {
                Publisher = publisher,
                Offer = offer,
                Sku = sku,
                Version = version
            },
            DataDisks = image.Value.DataDiskImages.Select(i => MapDataDisk(i.Lun)).ToList(),
            Architecture = image.Value.Architecture
        };

        if (image.Value.Plan != null)
        {
            details.Plan = new()
            {
                Name = image.Value.Plan.Name,
                Publisher = image.Value.Plan.Publisher,
                Product = image.Value.Plan.Product
            };
        }

        return details;
    }

    private static async Task<ImageDetails> GetGalleryImageDetailsAsync(ResourceIdentifier imageId)
    {
        GalleryImageResource? image = null;
        GalleryImageVersionResource? version = null;
        // No version specified, get the latest version and use that as imageId
        if (imageId.ResourceType == "Microsoft.Compute/galleries/images")
        {
            image = await ArmClient.GetGalleryImageResource(imageId).GetAsync().ConfigureAwait(false);
            var latestVersion = await image.GetGalleryImageVersions()
                .GetAllAsync()
                .Where(v => v.Data.ProvisioningState == GalleryProvisioningState.Succeeded && v.Data.PublishingProfile.ExcludeFromLatest == false)
                .OrderByDescending(v => new Version(v.Data.Name))
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            version = latestVersion;
            imageId = latestVersion is null ? imageId : latestVersion.Id;
        }

        if (imageId.ResourceType != "Microsoft.Compute/galleries/images/versions")
            throw new Exception("Image ID must be a gallery image version");

        image ??= await ArmClient.GetGalleryImageResource(imageId.Parent).GetAsync().ConfigureAwait(false);
        version ??= await ArmClient.GetGalleryImageVersionResource(imageId).GetAsync().ConfigureAwait(false);

        var details = new ImageDetails()
        {
            OSState = image.Data.OSState,
            OSType = image.Data.OSType,
            ImageReference = new() { Id = version.Data.Id },
            DataDisks = version.Data.StorageProfile.DataDiskImages.Select(i => MapDataDisk(i.Lun)).ToList(),
            Architecture = image.Data.Architecture
        };

        if (image.Data.PurchasePlan != null)
        {
            details.Plan = new()
            {
                Name = image.Data.PurchasePlan.Name,
                Publisher = image.Data.PurchasePlan.Publisher,
                Product = image.Data.PurchasePlan.Product
            };
        }

        return details;
    }

    private VirtualMachineOSDisk GetOSDisk(ImageDetails imageDetails)
    {
        var osDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
        {
            OSType = imageDetails.OSType,
            DeleteOption = DiskDeleteOptionType.Delete,
            DiskSizeGB = Configuration.OsDisk?.SizeGb,
            Caching = Configuration.OsDisk?.Caching
        };

        if (Configuration.OsDisk?.Sku != null)
        {
            osDisk.ManagedDisk = new VirtualMachineManagedDisk
            {
                StorageAccountType = Configuration.OsDisk.Sku.Value
            };
        }

        if (Configuration.OsDisk?.EphemeralPlacement.HasValue ?? false)
        {
            osDisk.Caching = CachingType.ReadOnly; // required for ephemeral disks

            osDisk.DiffDiskSettings = new DiffDiskSettings
            {
                Option = DiffDiskOption.Local,
                Placement = Configuration.OsDisk?.EphemeralPlacement
            };
        }

        return osDisk;
    }

    private List<VirtualMachineDataDisk> GetDataDisks(IList<VirtualMachineDataDisk>? imageDisks)
    {
        var disks = new List<VirtualMachineDataDisk>(imageDisks ?? Enumerable.Empty<VirtualMachineDataDisk>());
        var startingLun = disks.Select(d => d.Lun).DefaultIfEmpty(-1).Max() + 1;

        disks.AddRange((Configuration.DataDisks ?? Enumerable.Empty<DiskDefinition>())
            .Select(d => new VirtualMachineDataDisk(startingLun++, DiskCreateOptionType.Empty)
            {
                DiskSizeGB = d.SizeGb,
                ManagedDisk = d.Sku != null ? new VirtualMachineManagedDisk { StorageAccountType = d.Sku } : null,
                Caching = d.Caching,
                DeleteOption = DiskDeleteOptionType.Delete
            })
        );

        return disks;
    }

    private static VirtualMachineDataDisk MapDataDisk(int? lun)
    {
        if (!lun.HasValue)
            throw new Exception("Data disk image has no LUN");

        return new VirtualMachineDataDisk(lun.Value, DiskCreateOptionType.FromImage)
        {
            DeleteOption = DiskDeleteOptionType.Delete
        };
    }

    private async Task<(string stdout, string stderr)> RunPowershellScriptOnMachineAsync(string id, string script, CancellationToken cancellationToken)
    {
        try
        {
            var command = new RunCommandInput("RunPowerShellScript");

            foreach (var line in script.Split('\n'))
            {
                command.Script.Add(line);
            }

            var vmId = await GetVirtualMachineResourceIdentifierAsync(id, cancellationToken).ConfigureAwait(false);
            var result = await ArmClient.GetVirtualMachineResource(vmId).RunCommandAsync(WaitUntil.Completed, command, cancellationToken).ConfigureAwait(false);

            var stderr = result.Value.Value.Single(r => r.Code == "ComponentStatus/StdErr/succeeded").Message;
            var stdout = result.Value.Value.Single(r => r.Code == "ComponentStatus/StdOut/succeeded").Message;

            return (stdout, stderr);
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    private async Task<(string stdout, string stderr)> RunShellScriptOnMachineAsync(string id, string script, CancellationToken cancellationToken)
    {
        try
        {
            var command = new RunCommandInput("RunShellScript");

            foreach (var line in script.Split('\n'))
            {
                command.Script.Add(line);
            }

            var vmId = await GetVirtualMachineResourceIdentifierAsync(id, cancellationToken).ConfigureAwait(false);
            var result = await ArmClient.GetVirtualMachineResource(vmId).RunCommandAsync(WaitUntil.Completed, command, cancellationToken).ConfigureAwait(false);

            var output = result.Value.Value.Single(r => r.Code == "ProvisioningState/succeeded").Message;
            var stdoutStart = output.IndexOf("[stdout]");
            var stderrStart = output.IndexOf("[stderr]");
            var stdoutEnd = stdoutStart + "[stdout]".Length;
            var stderrEnd = stderrStart + "[stderr]".Length;
            var stdout = output[stdoutEnd..stderrStart].Trim();
            var stderr = output[stderrEnd..].Trim();

            return (stdout, stderr);
        }
        catch (RequestFailedException ex)
        {
            throw FormatCustomException(ex);
        }
    }

    public static Exception FormatCustomException(RequestFailedException exception)
    {
        // Rethrow as a ThrottleException if the Retry-After header is present or a CapacityException if the VM size is not available
        return exception.Status switch
        {
            429 when exception.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var header) == true && int.TryParse(header, out var retryAfter) =>
                new ThrottleException(TimeSpan.FromSeconds(retryAfter), exception),
            400 when Regex.IsMatch(exception.Message, @"^The requested VM size .+? is not available in the current region\.") =>
                new CapacityException(exception, 0),
            409 when Regex.IsMatch(exception.Message, @"^The requested VM size for resource '.+?' is currently not available in location '\w+'\.") =>
                new CapacityException(exception, 0),
            409 when Regex.Match(exception.Message, @"^Operation could not be completed as it results in exceeding approved .+? Cores quota\. Additional details - Deployment Model: Resource Manager, Location: \w+, Current Limit: (\d+), Current Usage: \d+, Additional Required: (\d+), \(Minimum\) New Limit Required: \d+\.").Groups is [_, var limit, var required] =>
                new CapacityException(exception, int.Parse(limit.Value) / int.Parse(required.Value)),
            _ => exception,
        };
    }

    private async Task<ResourceIdentifier> GetVirtualMachineResourceIdentifierAsync(string id, CancellationToken cancellationToken)
    {
        var subscription = await lazySubscription.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var resourceGroup = await lazyResourceGroup.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return VirtualMachineResource.CreateResourceIdentifier(subscription.Data.SubscriptionId, resourceGroup.Data.Name, id);
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            lazySubscription.DisposeAsync().AsTask(),
            lazyResourceGroup.DisposeAsync().AsTask(),
            lazyExtensionData.DisposeAsync().AsTask(),
            lazyLocation.DisposeAsync().AsTask(),
            lazySubnets.DisposeAsync().AsTask(),
            lazyImageDetails.DisposeAsync().AsTask()
        ).ConfigureAwait(false);
    }
}
