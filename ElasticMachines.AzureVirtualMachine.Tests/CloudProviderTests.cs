using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Resources;
using ElasticMachines.AzureVirtualMachine.Models;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.Tests.Core;
using Moq;
using Moq.Protected;
using Xunit;

namespace ElasticMachines.AzureVirtualMachine.Tests;

[Timed]
public class CloudProviderTests(CloudProviderFixture fixture) : IClassFixture<CloudProviderFixture>
{
    private readonly CloudProviderFixture fixture = fixture;

    [Fact]
    public void DeserializeConfiguration()
    {
        var yaml = $@"
        name: azure-virtual-machine
        with:
          subscription: {fixture.SubscriptionId}
          resourceGroup: {fixture.ResourceGroupName}
          location: {fixture.Location}
          zone: 1
          image: Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest
          vmSku: Standard_DS1_v2
          publicIpPerVm: true
          spotMaxPrice: 0.12
          subnets:
            - /subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.Network/virtualNetworks/vnet/subnets/subnet
          identities:
            - /subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/identity
          osDisk:
            sizeGb: 128
            sku: Premium_LRS
            caching: ReadWrite
            ephemeralPlacement: CacheDisk
          dataDisks:
            - sizeGb: 100
              sku: Standard_LRS
              caching: ReadOnly
          extensions:
            - name: CustomScript
              publisher: Microsoft.Azure.Extensions
              version: 2.1
              settings:
                timestamp: 123456789
                commandToExecute: echo 'Hello World'
              protectedSettings:
                fileUris:
                  - https://example.com/script.sh
                managedIdentity:
                  clientId: /subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/identity
          tags:
            foo: bar
          licenseType: Windows_Client
          securityProfile:
            encryptionAtHost: true
        ";

        var config = fixture.DeserializeAsConfiguration<Models.Configuration>(yaml);

        // Verify the config was passed through as expected
        Assert.Equal(fixture.SubscriptionId, config.Subscription);
        Assert.Equal(fixture.ResourceGroupName, config.ResourceGroup);
        Assert.Equal(fixture.Location, config.Location);
        Assert.Equal("1", config.Zone);
        Assert.Equal("Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest", config.Image);
        Assert.Equal(VirtualMachineSizeType.StandardDS1V2, config.VmSku);
        Assert.True(config.PublicIpPerVm);
        Assert.Equal(0.12, config.SpotMaxPrice);

        Assert.Equal(new($"/subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.Network/virtualNetworks/vnet/subnets/subnet"), config.Subnets?.Single());

        Assert.Equal(new($"/subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/identity"), config.Identities?.Single());

        Assert.Equal(128, config.OsDisk?.SizeGb);
        Assert.Equal(StorageAccountType.PremiumLrs, config.OsDisk?.Sku);
        Assert.Equal(CachingType.ReadWrite, config.OsDisk?.Caching);
        Assert.Equal(DiffDiskPlacement.CacheDisk, config.OsDisk?.EphemeralPlacement);

        Assert.Equal(100, config.DataDisks?.Single().SizeGb);
        Assert.Equal(StorageAccountType.StandardLrs, config.DataDisks?.Single().Sku);
        Assert.Equal(CachingType.ReadOnly, config.DataDisks?.Single().Caching);

        Assert.Equal("CustomScript", config.Extensions?.Single().Name);
        Assert.Equal("Microsoft.Azure.Extensions", config.Extensions?.Single().Publisher);
        Assert.Equal("2.1", config.Extensions?.Single().Version);
        Assert.Equal("123456789", config.Extensions?.Single().Settings?["timestamp"]);
        Assert.Equal("echo 'Hello World'", config.Extensions?.Single().Settings?["commandToExecute"]);
        Assert.Equal(new[] { "https://example.com/script.sh" }, config.Extensions?.Single().ProtectedSettings?["fileUris"]);
        Assert.Equal(new Dictionary<string, string> { { "clientId", $"/subscriptions/{fixture.SubscriptionId}/resourceGroups/{fixture.ResourceGroupName}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/identity" } }, config.Extensions?.Single().ProtectedSettings?["managedIdentity"]);

        Assert.Equal(new Dictionary<string, string> { { "foo", "bar" } }, config.Tags);

        // Make sure no properties are untested
        foreach (var prop in typeof(Models.Configuration).GetProperties())
        {
            Assert.NotNull(prop.GetValue(config));
        }
    }

    [Fact]
    public async Task InitializeWithoutResourceGroup()
    {
        var configuration = fixture.Configuration with { ResourceGroup = $"{fixture.ResourceGroupName}-new" };

        try
        {
            var provider = new CloudProvider(configuration, fixture.Tags);
            var machines = await provider.GetMachinesAsync(CancellationToken.None);
        }
        finally
        {
            await fixture.ArmClient.GetResourceGroupResource(ResourceGroupResource.CreateResourceIdentifier(fixture.SubscriptionId, configuration.ResourceGroup)).DeleteAsync(WaitUntil.Started);
        }
    }

    [Fact]
    public async Task FindSubscriptionByName()
    {
        var configuration = fixture.Configuration with { Subscription = fixture.Subscription?.Data.DisplayName };
        var provider = new CloudProvider(configuration, fixture.Tags);
    }

    [Fact]
    public async Task UseResourceGroupLocationByDefault()
    {
        var configuration = fixture.Configuration with { Location = null };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        Assert.Equal(fixture.Configuration.Location, vm.Value.Data.Location);
    }

    [Fact]
    public async Task FilterByTags()
    {
        var providers = new[]
        {
            new CloudProvider(fixture.Configuration, new(fixture.Tags) { { "provider", "1" } }),
            new CloudProvider(fixture.Configuration, new(fixture.Tags) { { "provider", "2" } }),
        };

        var created = await Task.WhenAll(providers.Select(p => p.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None)));
        var fetched = await Task.WhenAll(providers.Select(p => p.GetMachinesAsync(CancellationToken.None)));

        Assert.Equal(created[0], fetched[0].Single());
        Assert.Equal(created[1], fetched[1].Single());
    }

    [Fact]
    public async Task CreateDelete()
    {
        var provider = new CloudProvider(fixture.Configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        Assert.Contains(id, await provider.GetMachinesAsync(CancellationToken.None));

        await provider.DeleteMachineAsync(id, CancellationToken.None);
        Assert.DoesNotContain(id, await provider.GetMachinesAsync(CancellationToken.None));
    }

    [Theory]
    // Consider reverting to Debian:debian-11:11-backports-gen2:latest once RunCommandLinux stops timing out
    [InlineData("Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest", "Standard_DS1_v2")]
    [InlineData("Canonical:0001-com-ubuntu-server-jammy:22_04-lts-arm64:latest", "Standard_D2ps_v5")]
    [InlineData("MicrosoftWindowsServer:WindowsServer:2022-Datacenter:latest", "Standard_DS1_v2")]
    public async Task CreateRunner(string image, string vmSku)
    {
        var configuration = fixture.Configuration with { Image = image, VmSku = new VirtualMachineSizeType(vmSku) };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await fixture.CreateRunnerAsync(provider);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        var vmImage = vm.Value.Data.StorageProfile.ImageReference;
        Assert.Equal(image[..image.LastIndexOf(':')], $"{vmImage.Publisher}:{vmImage.Offer}:{vmImage.Sku}");
    }

    [Theory]
    [InlineData("windows-generalized", "1.0.0")]
    [InlineData("windows-deployer", "1.0.1")]
    [InlineData("specialized-image-from-paid-image", "1.0.0")]
    [InlineData("windows-generalized-data-disks", "1.0.0")]
    public async Task CreateFromGalleryImage(string imageName, string imageVersion)
    {
        var imageId = GalleryImageVersionResource.CreateResourceIdentifier(fixture.SubscriptionId, fixture.ImageGalleryResourceGroup, fixture.ImageGalleryName, imageName, imageVersion);
        var configuration = fixture.Configuration with { Image = imageId.ToString() };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        Assert.Equal(imageId, vm.Value.Data.StorageProfile.ImageReference.Id, true);
    }

    [Fact]
    public async Task CreateFromGalleryImageNoVersion()
    {
        var versionLessImageId = GalleryImageResource.CreateResourceIdentifier(fixture.SubscriptionId, fixture.ImageGalleryResourceGroup, fixture.ImageGalleryName, "windows-deployer");
        var configuration = fixture.Configuration with { Image = versionLessImageId.ToString() };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal($"{versionLessImageId}/versions/1.0.1", vm.Value.Data.StorageProfile.ImageReference.Id, true);
    }

    [Fact]
    public async Task SpecifySubnetsAndPublicIpPerVm()
    {
        var virtualNetworkData = new VirtualNetworkData()
        {
            Location = fixture.Configuration.Location,
            AddressPrefixes = { "10.1.0.0/16" },
            Subnets =
            {
                new()
                {
                    Name = "subnet-1",
                    AddressPrefix = "10.1.0.0/24"
                },
                                new()
                {
                    Name = "subnet-2",
                    AddressPrefix = "10.1.1.0/24"
                }
            }
        };

        var virtualNetwork = await fixture.SidecarResourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, "sidecar-vnet", virtualNetworkData);
        var subnets = await virtualNetwork.Value.GetSubnets().GetAllAsync().Select(s => s.Id).ToListAsync();

        var configuration = fixture.Configuration with { PublicIpPerVm = true, Subnets = subnets };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        var networkInterfaces = await Task.WhenAll(vm.Value.Data.NetworkProfile.NetworkInterfaces.Select(n => fixture.ArmClient.GetNetworkInterfaceResource(n.Id).GetAsync()));
        var ipConfigurations = networkInterfaces.SelectMany(n => n.Value.Data.IPConfigurations).ToList();

        Assert.Equal(subnets, ipConfigurations.Select(i => i.Subnet.Id));
        Assert.All(ipConfigurations.Select(i => i.PublicIPAddress), Assert.NotNull);
    }

    [Fact]
    public async Task SpecifyZone()
    {
        var configuration = fixture.Configuration with { Zone = "2" };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        Assert.Equal(configuration.Zone, vm.Value.Data.Zones.Single());
    }

    [Fact]
    public async Task SpecifyExtension()
    {
        var configuration = fixture.Configuration with
        {
            Extensions =
            [
                new VirtualMachineExtension(
                    Name: "CustomScript",
                    Publisher: "Microsoft.Azure.Extensions",
                    Version: "2.1")
                {
                    Settings = new Dictionary<string, object>()
                    {
                        { "timestamp", 123456789 },
                        { "commandToExecute", "echo 'Hello World!'" }
                    },
                    ProtectedSettings = new Dictionary<string, object>()
                    {
                        { "fileUris", new[] { "https://aka.ms/InstallAzureCli" } },
                        { "managedIdentity", new Dictionary<string, string>
                            {
                                { "clientId", "cc9d8186-0a81-464d-8e01-badc1fdb5d96" }
                            }
                        }
                    }
                }
            ]
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        var extensions = vm.Value.GetVirtualMachineExtensions();
        var installedExtension = extensions.Single();
        var settings = installedExtension.Data.Settings.ToObjectFromJson<Dictionary<string, object>>();

        Assert.Equal(configuration.Extensions[0].Name, installedExtension.Data.ExtensionType);
        Assert.Equal(configuration.Extensions[0].Publisher, installedExtension.Data.Publisher);
        Assert.Equal(configuration.Extensions[0].Version, installedExtension.Data.TypeHandlerVersion);
        Assert.Equal(configuration.Extensions[0].Settings?["timestamp"], int.Parse(settings?["timestamp"].ToString() ?? ""));
        Assert.Equal(configuration.Extensions[0].Settings?["commandToExecute"], settings?["commandToExecute"].ToString());
    }

    [Fact]
    public async Task SpecifyDataDisk()
    {
        var configuration = fixture.Configuration with
        {
            DataDisks =
            [
                new DiskDefinition()
                {
                    SizeGb = 100,
                    Sku = StorageAccountType.StandardLrs,
                    Caching = CachingType.ReadOnly
                }
            ]
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        var disk = vm.Value.Data.StorageProfile.DataDisks.Single();

        Assert.Equal(configuration.DataDisks[0].SizeGb, disk.DiskSizeGB);
        Assert.Equal(configuration.DataDisks[0].Sku, disk.ManagedDisk.StorageAccountType);
        Assert.Equal(configuration.DataDisks[0].Caching, disk.Caching);
        Assert.Equal(DiskDeleteOptionType.Delete, disk.DeleteOption);
    }

    [Fact]
    public async Task SpecifyUltraDataDisk()
    {
        var configuration = fixture.Configuration with
        {
            VmSku = VirtualMachineSizeType.StandardD2SV3, // Ultra SSD compatible VM SKU
            DataDisks =
            [
                new DiskDefinition()
                {
                    SizeGb = 100,
                    Sku = StorageAccountType.UltraSsdLrs,
                }
            ],
            Zone = "1" // Ultra SSD requires a zone
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        var disk = vm.Value.Data.StorageProfile.DataDisks.Single();

        Assert.Equal(configuration.DataDisks[0].Sku, disk.ManagedDisk.StorageAccountType);
    }

    [Fact]
    public async Task SpecifyOsDiskOverrides()
    {
        var configuration = fixture.Configuration with
        {
            OsDisk = new DiskDefinition
            {
                SizeGb = 108,
                Sku = StorageAccountType.StandardLrs,
                Caching = CachingType.None
            }
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal(configuration.OsDisk.SizeGb, vm.Value.Data.StorageProfile.OSDisk.DiskSizeGB);
        Assert.Equal(configuration.OsDisk.Sku, vm.Value.Data.StorageProfile.OSDisk.ManagedDisk.StorageAccountType);
        Assert.Equal(configuration.OsDisk.Caching, vm.Value.Data.StorageProfile.OSDisk.Caching);
    }

    [Fact]
    public async Task SpecifyEphemeralOsDisk()
    {
        var configuration = fixture.Configuration with
        {
            OsDisk = new DiskDefinition
            {
                EphemeralPlacement = DiffDiskPlacement.CacheDisk,
                Caching = CachingType.ReadWrite // should be changed to ReadOnly since we're using an ephemeral disk
            }
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal(DiffDiskOption.Local, vm.Value.Data.StorageProfile.OSDisk.DiffDiskSettings.Option);
        Assert.Equal(configuration.OsDisk.EphemeralPlacement, vm.Value.Data.StorageProfile.OSDisk.DiffDiskSettings.Placement);
        Assert.Equal(CachingType.ReadOnly, vm.Value.Data.StorageProfile.OSDisk.Caching);
    }

    [Fact]
    public async Task SpecifyIdentities()
    {
        var sidecarIdentity = await fixture.CreateUserAssignedIdentity();
        var localIdentity = await fixture.CreateUserAssignedIdentity(fixture.ResourceGroup, "local-identity");

        var configuration = fixture.Configuration with
        {
            Identities =
            [
                sidecarIdentity.Id,
                localIdentity.Id
            ]
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal(2, vm.Value.Data.Identity.UserAssignedIdentities.Count);
        Assert.Single(vm.Value.Data.Identity.UserAssignedIdentities, i => i.Key == sidecarIdentity.Id);
        Assert.Single(vm.Value.Data.Identity.UserAssignedIdentities, i => i.Key == localIdentity.Id);
    }

    [Fact]
    public async Task SpecifySpotInstance()
    {
        var configuration = fixture.Configuration with { SpotMaxPrice = 0.1 };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal(0.1, vm.Value.Data.BillingMaxPrice);
        Assert.Equal(VirtualMachineEvictionPolicyType.Delete, vm.Value.Data.EvictionPolicy);
        Assert.Equal(VirtualMachinePriorityType.Spot, vm.Value.Data.Priority);
    }

    [Fact]
    public async Task SpecifySpotInstanceAnyPrice()
    {
        var configuration = fixture.Configuration with { SpotMaxPrice = -1 };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal(-1, vm.Value.Data.BillingMaxPrice);
        Assert.Equal(VirtualMachineEvictionPolicyType.Delete, vm.Value.Data.EvictionPolicy);
        Assert.Equal(VirtualMachinePriorityType.Spot, vm.Value.Data.Priority);
    }

    [Fact]
    public async Task SpecifyTags()
    {
        var configuration = fixture.Configuration with
        {
            Tags = new Dictionary<string, string>
            {
                { "foo", "bar" },
                // Reserved tags passed to the provider shouldn't be overwritten
                { "test-suite-name", "ignore me" }
            }
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);
        var tags = vm.Value.Data.Tags;

        Assert.Equal("bar", tags["foo"]);
        Assert.Equal(fixture.TestSuiteName, tags["test-suite-name"]);
    }

    [Fact]
    public async Task SpecifyLicenseType()
    {
        var configuration = fixture.Configuration with
        {
            Image = "MicrosoftWindowsServer:WindowsServer:2022-Datacenter:latest",
            LicenseType = "Windows_Client"
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.Equal("Windows_Client", vm.Value.Data.LicenseType);
    }

    [Fact]
    public async Task SpecifySecurityProfile()
    {
        var configuration = fixture.Configuration with
        {
            SecurityProfile = new Models.SecurityProfile { EncryptionAtHost = true }
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var id = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);
        var vm = await fixture.ResourceGroup.GetVirtualMachines().GetAsync(id);

        Assert.True(vm.Value.Data.SecurityProfile.EncryptionAtHost);
    }

    [Fact]
    public async Task DeleteNonexistentMachine()
    {
        var provider = new CloudProvider(fixture.Configuration, fixture.Tags);

        await provider.DeleteMachineAsync(Guid.NewGuid().ToString(), CancellationToken.None);
    }

    [Fact]
    public void HandleRateLimitingException()
    {
        var mockResponse = new Mock<Response>() { CallBase = true };

        mockResponse.SetupGet(m => m.Status).Returns(429);
        mockResponse.SetupGet(m => m.ReasonPhrase).Returns("Too many requests");

        mockResponse.Protected().As<IResponseMock>().Setup(m => m.TryGetHeader(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns(new TryGetHeaderCallback((string name, ref string value) =>
            {
                name = "Retry-After";
                value = "30";
                return true;
            })
        );

        var exception = CloudProvider.FormatCustomException(new RequestFailedException(mockResponse.Object));

        Assert.IsType<ThrottleException>(exception);
        Assert.Equal(TimeSpan.FromSeconds(30), ((ThrottleException)exception).RetryAfter);
    }

    [Fact]
    public void HandleRateLimitingExceptionMissingRetryHeader()
    {
        var mockResponse = new Mock<Response>() { CallBase = true };

        mockResponse.SetupGet(m => m.Status).Returns(429);
        mockResponse.SetupGet(m => m.ReasonPhrase).Returns("Too many requests");

        var exception = CloudProvider.FormatCustomException(new RequestFailedException(mockResponse.Object));

        Assert.IsType<RequestFailedException>(exception);
        Assert.Equal(429, ((RequestFailedException)exception).GetRawResponse()?.Status);
        Assert.Equal("Too many requests", ((RequestFailedException)exception).GetRawResponse()?.ReasonPhrase);
    }

    [Theory]
    [InlineData("Standard_ND6s", @"^The requested VM size .+? is not available in the current region\.")] // Our subscription has no capacity for this sku
    [InlineData("Standard_D2s_v3", @"^The requested VM size for resource '.+?' is currently not available in location '\w+'\.")] // Our subscription has capacity restrictions for this sku
    public async Task HandleCapacityException(string sku, string regex)
    {
        var configuration = fixture.Configuration with { VmSku = new VirtualMachineSizeType(sku), Location = AzureLocation.AustraliaCentral };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var exception = await Assert.ThrowsAsync<CapacityException>(async () => await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None));

        Assert.Equal(0, exception.Capacity);
        Assert.Matches(regex, exception.Message);
    }
}

// This is needed for a Moq workaround https://github.com/Azure/azure-sdk-for-net/issues/18863 to mock a protected method

internal interface IResponseMock
{
    bool TryGetHeader(string name, out string value);
}

internal delegate bool TryGetHeaderCallback(string name, ref string value);
