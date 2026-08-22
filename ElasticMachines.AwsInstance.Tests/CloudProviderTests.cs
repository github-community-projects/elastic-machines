using System.Net;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.Tests.Core;
using Xunit;

namespace ElasticMachines.AwsInstance.Tests;

[Timed]
public class CloudProviderTests : IClassFixture<CloudProviderFixture>
{
    readonly CloudProviderFixture fixture;

    public CloudProviderTests(CloudProviderFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DeserializeConfiguration()
    {
        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None);

        var yaml = $@"
        name: {fixture.TestSuiteName}
        with:
          roleArn: arn:aws:iam::012345678901:role/role
          region: {fixture.Region}
          instanceType: {configuration.InstanceType}
          imageId: {configuration.ImageId}
          iamInstanceProfile:
            arn: {configuration.IamInstanceProfile.Arn}
          subnetId: {configuration.SubnetId}
          securityGroupIds:
            - sg-0123456789abcdef0
          keyName: key-name
          userData: foo
          placement:
            tenancy: host
          blockDeviceMappings:
            - deviceName: /dev/sdb
              noDevice: ''
              virtualName: ephemeral0
              ebs:
                deleteOnTermination: true
                encrypted: true
                iops: 500
                kmsKeyId: 01234567-89ab-cdef-1234-56789abcdef0
                outpostArn: arn:aws:outposts:us-west-2:123456789012:outpost/op-0123456789abcdef0
                snapshotId: snap-0123456789abcdef0
                throughput: 125
                volumeSize: 16
                volumeType: io1
          tagSpecifications:
            - resourceType: instance
              tags:
                - key: foo
                  value: bar
          instanceMarketOptions:
            marketType: spot
            spotOptions:
              maxPrice: 0.012
        ";

        var config = fixture.DeserializeAsConfiguration<Models.Configuration>(yaml);

        Assert.Equal("arn:aws:iam::012345678901:role/role", config.RoleArn);
        Assert.Equal(fixture.Region, config.Region);
        Assert.Equal(configuration.InstanceType, config.InstanceType);
        Assert.Equal(configuration.ImageId, config.ImageId);
        Assert.Equal(configuration.IamInstanceProfile.Arn, config.IamInstanceProfile.Arn);
        Assert.Equal(configuration.SubnetId, config.SubnetId);
        Assert.Equal("sg-0123456789abcdef0", config.SecurityGroupIds?.Single());
        Assert.Equal("key-name", config.KeyName);
        Assert.Equal("foo", config.UserData);

        var bde = config.BlockDeviceMappings?.Single();
        Assert.Equal("/dev/sdb", bde?.DeviceName);
        Assert.Equal("", bde?.NoDevice);
        Assert.Equal("ephemeral0", bde?.VirtualName);
        Assert.True(bde?.Ebs.DeleteOnTermination);
        Assert.True(bde?.Ebs.Encrypted);
        Assert.Equal(500, bde?.Ebs.Iops);
        Assert.Equal("01234567-89ab-cdef-1234-56789abcdef0", bde?.Ebs.KmsKeyId);
        Assert.Equal("arn:aws:outposts:us-west-2:123456789012:outpost/op-0123456789abcdef0", bde?.Ebs.OutpostArn);
        Assert.Equal("snap-0123456789abcdef0", bde?.Ebs.SnapshotId);
        Assert.Equal(125, bde?.Ebs.Throughput);
        Assert.Equal(16, bde?.Ebs.VolumeSize);
        Assert.Equal(VolumeType.Io1, bde?.Ebs.VolumeType);

        var tags = config.TagSpecifications?.Single();
        Assert.Equal(ResourceType.Instance, tags?.ResourceType);
        Assert.Equal("foo", tags?.Tags.Single().Key);
        Assert.Equal("bar", tags?.Tags.Single().Value);

        var instanceMarketOptions = config.InstanceMarketOptions;
        Assert.Equal(MarketType.Spot, instanceMarketOptions?.MarketType);
        Assert.Equal("0.012", instanceMarketOptions?.SpotOptions?.MaxPrice);

        // Make sure no properties are untested
        foreach (var prop in typeof(Models.Configuration).GetProperties())
        {
            Assert.NotNull(prop.GetValue(config));
        }
    }

    [Fact]
    public async Task FilterByTags()
    {
        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None);

        var providers = new[]
        {
            new CloudProvider(configuration, new(fixture.Tags) { { "provider", "1" } }),
            new CloudProvider(configuration, new(fixture.Tags) { { "provider", "2" } }),
        };

        var created = await Task.WhenAll(providers.Select(p => p.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None)));
        var fetched = await Task.WhenAll(providers.Select(p => p.GetMachinesAsync(CancellationToken.None)));

        Assert.Equal(created[0], fetched[0].Single());
        Assert.Equal(created[1], fetched[1].Single());

        await Task.WhenAll(providers.Zip(created).Select(pair => pair.First.DeleteMachineAsync(pair.Second, CancellationToken.None)));
    }

    [Theory]
    [InlineData("al2023-ami-2023*x86_64")]
    [InlineData("Windows_Server-2022-English-Core-Base*")]
    public async Task CreateDeleteRunner(string imageNameFilter)
    {
        var imageId = await fixture.GetLatestImageId(imageNameFilter);
        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with { ImageId = imageId, InstanceType = InstanceType.T2Small };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var instanceName = await fixture.CreateRunnerAsync(provider);

        await provider.DeleteMachineAsync(instanceName, CancellationToken.None);
    }

    [Fact]
    public async Task SpecifySecurityGroups()
    {
        var securityGroupResponses = await fixture.CreateTestSecurityGroups(2);

        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with { SecurityGroupIds = securityGroupResponses.Select(r => r.GroupId).ToList() };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var instances = await provider.GetInstancesAsync(CancellationToken.None);
        var instance = instances.Single(i => i.Tags.Any(t => t.Key == "Name" && t.Value == name));
        var securityGroups = instance?.SecurityGroups.Select(sg => sg.GroupId).OrderDescending().ToList() ?? new List<string>();
        var testSecurityGroupIds = securityGroupResponses.Select(r => r.GroupId).OrderDescending().ToList();

        Assert.Equal(2, securityGroups.Count);
        Assert.Equal(testSecurityGroupIds, securityGroups);

        await provider.DeleteMachineAsync(name, CancellationToken.None);
        var deleteSecurityGroupRequests = securityGroupResponses.Select(r => new DeleteSecurityGroupRequest { GroupId = r.GroupId });
        await Task.WhenAll(deleteSecurityGroupRequests.Select(r => fixture.AwsEc2Client.DeleteSecurityGroupAsync(r)));
    }

    [Fact]
    public async Task SpecifyKeyPair()
    {
        var createKeyPairRequest = new CreateKeyPairRequest
        {
            KeyName = $"{fixture.TestSuiteName}-key-name",
            KeyFormat = KeyFormat.Pem,
            KeyType = KeyType.Rsa,
            TagSpecifications = fixture.GetTagSpecifications(ResourceType.KeyPair)
        };

        var createKeyPairResponse = await fixture.AwsEc2Client.CreateKeyPairAsync(createKeyPairRequest);

        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with { KeyName = createKeyPairResponse.KeyPair.KeyName };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var instances = await provider.GetInstancesAsync(CancellationToken.None);
        var instance = instances.Single(i => i.Tags.Any(t => t.Key == "Name" && t.Value == name));

        Assert.Equal(instance?.KeyName, createKeyPairResponse.KeyPair.KeyName);

        await provider.DeleteMachineAsync(name, CancellationToken.None);

        var deleteKeyPairRequest = new DeleteKeyPairRequest
        {
            KeyName = createKeyPairResponse.KeyPair.KeyName
        };

        await fixture.AwsEc2Client.DeleteKeyPairAsync(deleteKeyPairRequest);
    }

    [Fact]
    public async Task SpecifyBlockDeviceMappings()
    {
        // ! Edge cases
        //   - Throughput can only be specified with certain DeviceName types
        //   - Iops to VolumeSize ratio can be no more than 50:1
        //   - Options not tested: KmsKeyId, OutpostArn, SnapshotId, Throughput
        var deviceBlockMapping = new BlockDeviceMapping
        {
            DeviceName = "/dev/sdb",
            NoDevice = "",
            VirtualName = "ephemeral0",
            Ebs = new EbsBlockDevice
            {
                DeleteOnTermination = true,
                Encrypted = false,
                Iops = 500,
                VolumeSize = 16,
                VolumeType = VolumeType.Io1
            }
        };

        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with { BlockDeviceMappings = new List<BlockDeviceMapping> { deviceBlockMapping } };
        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var instances = await provider.GetInstancesAsync(CancellationToken.None);
        var instance = instances.Single(i => i.Tags.Any(t => t.Key == "Name" && t.Value == name));
        var blockDeviceMapping = instance.BlockDeviceMappings.First(bdm => bdm.DeviceName == deviceBlockMapping.DeviceName);

        Assert.Equal(deviceBlockMapping.DeviceName, blockDeviceMapping?.DeviceName);
        Assert.Equal(deviceBlockMapping.Ebs.DeleteOnTermination, blockDeviceMapping?.Ebs.DeleteOnTermination);

        await provider.DeleteMachineAsync(name, CancellationToken.None);
    }

    [Fact]
    public async Task SpecifyTagSpecifications()
    {
        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with
        {
            TagSpecifications = fixture.GetTagSpecifications(ResourceType.Instance, new()
            {
                { "em-instance-tag-1", "foo" },
                { "em-instance-tag-2", "bar" },
                // Reserved tags passed to the provider shouldn't be overwritten
                { "test-suite-name", "ignore me" }
            })
        };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var instances = await provider.GetInstancesAsync(CancellationToken.None);
        var instance = instances.Single(i => i.Tags.Any(t => t.Key == "Name" && t.Value == name));

        Assert.Equal("elastic-machines", instance.Tags.Find(t => t.Key == "catalog_service")?.Value);
        Assert.Equal("foo", instance.Tags.Find(t => t.Key == "em-instance-tag-1")?.Value);
        Assert.Equal("bar", instance.Tags.Find(t => t.Key == "em-instance-tag-2")?.Value);
        Assert.NotEqual("ignore me", instance.Tags.Find(t => t.Key == "test-suite-name")?.Value);

        await provider.DeleteMachineAsync(name, CancellationToken.None);
    }

    [Fact]
    public async Task SpecifySpotInstance()
    {
        var configuration = await fixture.Configuration.GetValueAsync(CancellationToken.None) with { InstanceMarketOptions = new() { MarketType = MarketType.Spot } };

        var provider = new CloudProvider(configuration, fixture.Tags);

        var name = await provider.CreateMachineAsync(fixture.GenerateUniqueName(), CancellationToken.None);

        var instances = await provider.GetInstancesAsync(CancellationToken.None);
        var instance = instances.Single(i => i.Tags.Any(t => t.Key == "Name" && t.Value == name));

        Assert.Equal(InstanceLifecycleType.Spot, instance?.InstanceLifecycle);
        Assert.NotNull(instance?.SpotInstanceRequestId);

        await provider.DeleteMachineAsync(name, CancellationToken.None);
    }

    [Fact]
    public void ReThrowCapacityExceptions()
    {
        var exception = new AmazonEC2Exception("An error occurred (InsufficientInstanceCapacity) when calling the RunInstances operation (reached max retries: 4). We currently do not have sufficient capacity in the Availability Zone you requested.", new(), ErrorType.Sender, "InsufficientInstanceCapacity", "1", HttpStatusCode.BadRequest);

        var formattedException = CloudProvider.FormatCustomException(exception);

        Assert.IsType<CapacityException>(formattedException);
        Assert.Null(((CapacityException)formattedException).Capacity);
    }

    [Fact]
    public async Task DeleteNonexistentMachine()
    {
        var provider = new CloudProvider(await fixture.Configuration.GetValueAsync(CancellationToken.None), fixture.Tags);

        await provider.DeleteMachineAsync(Guid.NewGuid().ToString(), CancellationToken.None);
    }
}
