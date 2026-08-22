using System.Net;
using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using ElasticMachines.AwsInstance.Models;
using ElasticMachines.Engine;
using ElasticMachines.Engine.Exceptions;
using ElasticMachines.Engine.Models;
using ElasticMachines.Utilities;

namespace ElasticMachines.AwsInstance;

[CloudProvider("aws-instance")]
[CloudProvider("aws-instance@v0")]
[CloudProvider("aws-instance@v1")]
public class CloudProvider : CloudProvider<Models.Configuration>, IAsyncDisposable
{
    private const int timeoutMinutes = 10;

    protected RegionEndpoint Region { get; }
    protected AWSCredentials Credentials { get; }
    protected AmazonEC2Client Ec2Client { get; }
    protected AmazonSimpleSystemsManagementClient SsmClient { get; }
    protected AsyncExpiringLazy<Image> LazyImage { get; }

    public CloudProvider(Models.Configuration configuration, Dictionary<string, string> tags) : base(configuration, tags)
    {
        Region = RegionEndpoint.GetBySystemName(configuration.Region);
        Credentials = FallbackCredentialsFactory.GetCredentials();

        if (configuration.RoleArn != null)
        {
            Credentials = new AssumeRoleAWSCredentials(Credentials, configuration.RoleArn, "elastic-machines");
        }

        Ec2Client = new AmazonEC2Client(Credentials, Region);
        SsmClient = new AmazonSimpleSystemsManagementClient(Credentials, Region);
        LazyImage = new(GetImageDetailsAsync);
    }

    public override async Task<string> CreateMachineAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var runInstanceRequest = new RunInstancesRequest
            {
                ImageId = Configuration.ImageId,
                InstanceType = Configuration.InstanceType,
                MaxCount = 1,
                MinCount = 1,
                IamInstanceProfile = new IamInstanceProfileSpecification
                {
                    Arn = Configuration.IamInstanceProfile.Arn
                },
                TagSpecifications = GetTagSpecifications(name),
                SubnetId = Configuration.SubnetId,
                SecurityGroupIds = Configuration.SecurityGroupIds,
                KeyName = Configuration.KeyName,
                BlockDeviceMappings = Configuration.BlockDeviceMappings,
                Placement = new Amazon.EC2.Model.Placement
                {
                    Tenancy = Configuration.Placement?.Tenancy ?? Tenancy.Default
                },
                InstanceMarketOptions = new()
                {
                    MarketType = Configuration.InstanceMarketOptions?.MarketType,
                    SpotOptions = new()
                    {
                        MaxPrice = Configuration.InstanceMarketOptions?.SpotOptions?.MaxPrice
                    }
                },
                UserData = Configuration.UserData,
            };

            var runInstanceResponse = await Ec2Client.RunInstancesAsync(runInstanceRequest, cancellationToken).ConfigureAwait(false);
            var instanceName = runInstanceResponse.Reservation.Instances[0].Tags.Single(t => t.Key == "Name").Value;

            var describeInstancesRequest = new DescribeInstancesRequest
            {
                InstanceIds = { runInstanceResponse.Reservation.Instances[0].InstanceId }
            };

            await PollAsync(describeInstancesRequest, Ec2Client.DescribeInstancesAsync, r => r.Reservations.SelectMany(r => r.Instances).All(i => i.State.Name == "running"), cancellationToken: cancellationToken).ConfigureAwait(false);

            return instanceName;
        }
        catch (AmazonServiceException exception)
        {
            throw FormatCustomException(exception);
        }
    }

    public override async Task ConfigureMachineAsRunnerAsync(string name, string jitConfiguration, GetRunnerDownloadAsync getRunnerDownloadAsync, CancellationToken cancellationToken)
    {
        var image = await LazyImage.GetValueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        (var operatingSystem, var architecture) = (image.PlatformDetails, image.Architecture.Value) switch
        {
            ("Linux/UNIX", "arm64") => (Engine.Models.OperatingSystem.Linux, Architecture.Arm64),
            ("Linux/UNIX", "x86_64") => (Engine.Models.OperatingSystem.Linux, Architecture.X64),
            ("Linux/UNIX", "arm64_mac") => (Engine.Models.OperatingSystem.Macos, Architecture.Arm64),
            ("Linux/UNIX", "x86_64_mac") => (Engine.Models.OperatingSystem.Macos, Architecture.X64),
            ("Ubuntu Pro Linux", "x86_64") => (Engine.Models.OperatingSystem.Linux, Architecture.X64),
            ("Ubuntu Pro Linux", "arm64") => (Engine.Models.OperatingSystem.Linux, Architecture.Arm64),
            ("Windows", "arm64") => (Engine.Models.OperatingSystem.Windows, Architecture.Arm64),
            ("Windows", "x86_64") => (Engine.Models.OperatingSystem.Windows, Architecture.X64),
            _ => throw new NotSupportedException($"Unsupported platform '{image.PlatformDetails}' and architecture '{image.Architecture}'")
        };

        var (url, token) = await getRunnerDownloadAsync(operatingSystem, architecture, cancellationToken).ConfigureAwait(false);

        var (stdout, stderr) = operatingSystem switch
        {
            Engine.Models.OperatingSystem.Linux => await RunShellScriptOnMachineAsync(name, Scripts.GetLinuxScript(jitConfiguration, url.AbsoluteUri, token), cancellationToken).ConfigureAwait(false),
            Engine.Models.OperatingSystem.Macos => await RunShellScriptOnMachineAsync(name, Scripts.GetMacosScript(jitConfiguration, url.AbsoluteUri, token), cancellationToken).ConfigureAwait(false),
            Engine.Models.OperatingSystem.Windows => await RunPowershellScriptOnMachineAsync(name, Scripts.GetWindowsScript(jitConfiguration, url.AbsoluteUri, token), cancellationToken).ConfigureAwait(false),
            _ => throw new Exception($"Unsupported operating system '{operatingSystem}'")
        };

        if (!stdout.TrimEnd().EndsWith($"Started runner in background"))
        {
            throw new ScriptException("Script didn't start runner", stdout, stderr);
        }
    }

    public override async Task DeleteMachineAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var instances = await GetInstancesAsync(name, cancellationToken).ConfigureAwait(false);
            var instanceIds = instances.Select(i => i.InstanceId).ToList();

            if (instanceIds.Count == 0)
                return;

            var request = new TerminateInstancesRequest
            {
                InstanceIds = instanceIds
            };

            await Ec2Client.TerminateInstancesAsync(request, cancellationToken).ConfigureAwait(false);

            var describeInstancesRequest = new DescribeInstancesRequest
            {
                InstanceIds = instanceIds
            };

            await PollAsync(describeInstancesRequest, Ec2Client.DescribeInstancesAsync, r => r.Reservations.SelectMany(r => r.Instances).All(i => i.State.Name == "terminated"), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonServiceException exception)
        {
            throw FormatCustomException(exception);
        }
    }

    public override async Task<List<string>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        var instances = await GetInstancesAsync(null, cancellationToken).ConfigureAwait(false);

        return instances
            .Select(i => i.Tags.FirstOrDefault(t => t.Key == "Name")?.Value)
            .OfType<string>()
            .ToList();
    }

    public async Task<List<Instance>> GetInstancesAsync(CancellationToken cancellationToken)
    {
        return await GetInstancesAsync(null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string stdout, string stderr)> RunPowershellScriptOnMachineAsync(string name, string script, CancellationToken cancellationToken)
    {
        var instances = await GetInstancesAsync(name, cancellationToken).ConfigureAwait(false);
        var instanceId = instances.Select(i => i.InstanceId).SingleOrDefault() ?? throw new Exception($"No instance found with name '{name}'");

        return await RunCommandAsync(instanceId, "AWS-RunPowerShellScript", script, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string stdout, string stderr)> RunShellScriptOnMachineAsync(string name, string script, CancellationToken cancellationToken)
    {
        var instances = await GetInstancesAsync(name, cancellationToken).ConfigureAwait(false);
        var instanceId = instances.Select(i => i.InstanceId).SingleOrDefault() ?? throw new Exception($"No instance found with name '{name}'");

        return await RunCommandAsync(instanceId, "AWS-RunShellScript", script, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Instance>> GetInstancesAsync(string? name, CancellationToken cancellationToken)
    {
        var request = new DescribeInstancesRequest
        {
            Filters = Tags.Select(t => new Filter { Name = $"tag:{t.Key}", Values = { t.Value } }).ToList()
        };

        if (name != null)
        {
            request.Filters.Add(new() { Name = $"tag:Name", Values = { name } });
        }

        var response = await Ec2Client.DescribeInstancesAsync(request, cancellationToken).ConfigureAwait(false);

        return response.Reservations.SelectMany(r => r.Instances).Where(i => i.State.Name != "terminated").ToList();
    }

    private List<TagSpecification> GetTagSpecifications(string name)
    {
        // Globally defined tags and internal Elastic Machines tracking tags
        var tags = new List<TagSpecification>()
        {
            new()
            {
                ResourceType = Amazon.EC2.ResourceType.Instance,
                Tags = Tags.Select(t => new Amazon.EC2.Model.Tag(t.Key, t.Value)).Append(new("Name", name)).ToList()
            }
        };

        // Attach any user configured tags that don't conflict with elastic-machines internal tracking tags
        if (Configuration.TagSpecifications != null)
        {
            foreach (var tagSpecification in Configuration.TagSpecifications.Where(ts => ts.ResourceType == Amazon.EC2.ResourceType.Instance))
            {
                foreach (var tag in tagSpecification.Tags)
                {
                    if (!Tags.ContainsKey(tag.Key))
                    {
                        tags[0].Tags.Add(tag);
                    }
                }
            }

            // All non-instance tagSpecifications
            var subResourceTags = Configuration.TagSpecifications.Where(ts => ts.ResourceType != Amazon.EC2.ResourceType.Instance);
            tags.AddRange(subResourceTags);
        }

        return tags;
    }

    private async Task<Image> GetImageDetailsAsync(CancellationToken cancellationToken)
    {
        var imageId = Configuration.ImageId;

        var describeImagesRequest = new DescribeImagesRequest
        {
            ImageIds = new List<string> { imageId }
        };

        var describeImagesResponse = await Ec2Client.DescribeImagesAsync(describeImagesRequest, cancellationToken).ConfigureAwait(false);

        if (describeImagesResponse.Images.Count != 1)
            throw new Exception($"Expected a single image, but found {describeImagesResponse.Images.Count} matching ID '{imageId}'");

        var image = describeImagesResponse.Images.SingleOrDefault();

        if (image == null)
            throw new Exception($"No image found matching ID '{imageId}'");

        return image;
    }

    private async Task<(string stdout, string stderr)> RunCommandAsync(string id, string command, string script, CancellationToken cancellationToken)
    {
        try
        {

            var describeInstanceInformationRequest = new DescribeInstanceInformationRequest
            {
                Filters = new()
            {
                new()
                {
                    Key = "InstanceIds",
                    Values = new() { id }
                }
            }
            };

            await PollAsync(describeInstanceInformationRequest, SsmClient.DescribeInstanceInformationAsync, r => r.InstanceInformationList.Any(), cancellationToken).ConfigureAwait(false);

            var sendCommandRequest = new SendCommandRequest
            {
                InstanceIds = new() { id },
                DocumentName = command,
                Parameters = new()
                {
                    ["commands"] = script.Split('\n').ToList(),
                    // Subtract one minute to provide a buffer before polling times out
                    ["executionTimeout"] = new() { TimeSpan.FromMinutes(timeoutMinutes - 1).TotalSeconds.ToString() },
                }
            };

            var sendCommandResponse = await SsmClient.SendCommandAsync(sendCommandRequest, cancellationToken).ConfigureAwait(false);

            var getCommandInvocationRequest = new GetCommandInvocationRequest
            {
                CommandId = sendCommandResponse.Command.CommandId,
                InstanceId = id
            };

            static bool checkCommandStatus(GetCommandInvocationResponse response)
            {
                if (response.Status == CommandInvocationStatus.Cancelled ||
                    response.Status == CommandInvocationStatus.Failed ||
                    response.Status == CommandInvocationStatus.TimedOut)
                    throw new Exception($"Command failed with status '{response.Status}' and stderr:\n{response.StandardErrorContent}");

                return response.Status == CommandInvocationStatus.Success;
            }

            var getCommandInvocationResponse = await PollAsync(getCommandInvocationRequest, SsmClient.GetCommandInvocationAsync, checkCommandStatus, cancellationToken).ConfigureAwait(false);

            return (getCommandInvocationResponse.StandardOutputContent, getCommandInvocationResponse.StandardErrorContent);
        }
        catch (AmazonServiceException exception)
        {
            throw FormatCustomException(exception);
        }
    }

    private static async Task<TResponse> PollAsync<TRequest, TResponse>(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> action, Func<TResponse, bool> predicate, CancellationToken cancellationToken)
    {
        var timeoutEnd = DateTime.Now + TimeSpan.FromMinutes(timeoutMinutes);

        while (DateTime.Now < timeoutEnd)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            var response = await action(request, cancellationToken).ConfigureAwait(false);

            if (predicate(response))
                return response;
        }

        throw new Exception($"Timed out while polling for '{typeof(TResponse)}'");
    }


    public static Exception FormatCustomException(AmazonServiceException exception)
    {
        var capacityExceptionVariants = new HashSet<string>
        {
            "InsufficientInstanceCapacity",
            "InsufficientHostCapacity",
            "InstanceLimitExceeded",
            "SpotMaxPriceTooLow"
        };

        return exception.StatusCode switch
        {
            HttpStatusCode.BadRequest when capacityExceptionVariants.Contains(exception.ErrorCode) =>
                new CapacityException(exception),
            _ => exception,
        };
    }

    public virtual async ValueTask DisposeAsync()
    {
        await LazyImage.DisposeAsync().ConfigureAwait(false);
    }
}
