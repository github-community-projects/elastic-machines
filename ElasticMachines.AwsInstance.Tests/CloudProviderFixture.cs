using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Extensions.NETCore.Setup;
using ElasticMachines.AwsInstance.Models;
using ElasticMachines.Tests.Core;
using ElasticMachines.Utilities;
using Microsoft.Extensions.Configuration;

namespace ElasticMachines.AwsInstance.Tests;

public class CloudProviderFixture : BaseCloudProviderFixture
{
    public string Region { get; }
    public IAmazonEC2 AwsEc2Client { get; }
    public AsyncExpiringLazy<Configuration> Configuration { get; }

    protected AWSOptions Options { get; }

    public CloudProviderFixture() : base("aws-instance")
    {
        Region = Settings["region"].GetValue();
        var regionEndpoint = RegionEndpoint.GetBySystemName(Region);
        Options = AppSettings.GetAWSOptions();
        Options.Region = regionEndpoint;
        AwsEc2Client = Options.CreateServiceClient<IAmazonEC2>();

        // GitHub requirement for all AWS deployments, see https://thehub.github.com/news/2022-12-20-aws-ownership-enforcement-and-cleanup/
        Tags.Add("catalog_service", "elastic-machines");

        // Propagate the named profile being used so the provider can pick it up
        Environment.SetEnvironmentVariable("AWS_PROFILE", Options.Profile);

        Configuration = new(GetConfiguration);
    }

    private async Task<Configuration> GetConfiguration(CancellationToken cancellationToken)
    {
        var imageId = await GetLatestImageId("ubuntu/images/hvm-ssd/ubuntu-jammy-22.04-amd64-server*");

        return new(Region, imageId, new(Settings["iamInstanceProfile:arn"].GetValue()), Settings["subnetId"].GetValue())
        {
            InstanceType = InstanceType.T2Micro,
            TagSpecifications = GetTagSpecifications(ResourceType.Instance),
            RoleArn = Settings["roleArn"],
        };
    }

    public List<Tag> GetEC2Tags(Dictionary<string, string>? additionalTags = null)
    {
        var tags = Tags.Select(t => new Tag(t.Key, t.Value)).ToList();

        if (additionalTags?.Count > 0)
        {
            tags.AddRange(additionalTags.Select(t => new Tag(t.Key, t.Value)));
        }

        return tags;
    }

    public List<TagSpecification> GetTagSpecifications(ResourceType resourceType, Dictionary<string, string>? additionalTags = null)
    {
        return new List<TagSpecification>()
        {
            new()
            {
                ResourceType = resourceType,
                Tags = GetEC2Tags(additionalTags)
            }
        };
    }

    public async Task<IEnumerable<CreateSecurityGroupResponse>> CreateTestSecurityGroups(int count = 1)
    {
        var tasks = Enumerable.Range(0, count).Select(i => AwsEc2Client.CreateSecurityGroupAsync(new CreateSecurityGroupRequest
        {
            GroupName = $"em-test-sg-{Guid.NewGuid().ToString().Substring(0, 8)}",
            Description = $"Elastic Machines test security group {i + 1}",
            TagSpecifications = GetTagSpecifications(ResourceType.SecurityGroup)
        }));

        return await Task.WhenAll(tasks);
    }

    public async Task<string> GetLatestImageId(string imageNameFilter, params Filter[] additionalFilters)
    {
        var describeImagesRequest = new DescribeImagesRequest
        {
            Owners = new() { "amazon" },
            Filters = new(additionalFilters)
            {
                new()
                {
                    Name = "name",
                    Values = new() { imageNameFilter }
                }
            }
        };

        var images = await AwsEc2Client.DescribeImagesAsync(describeImagesRequest);

        return images.Images.MaxBy(i => i.CreationDate)?.ImageId ?? throw new Exception($"No images matched name filter '{imageNameFilter}'");
    }
}
