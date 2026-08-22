using Amazon.EC2.Model;

namespace ElasticMachines.AwsInstance.Models;

public record Configuration(string Region, string ImageId, IamInstanceProfile IamInstanceProfile, string SubnetId)
{
    public string? RoleArn { get; init; }
    public string? InstanceType { get; init; }
    public List<string>? SecurityGroupIds { get; init; }
    public string? KeyName { get; init; }
    public List<BlockDeviceMapping>? BlockDeviceMappings { get; init; }
    public Placement? Placement { get; init; }
    public List<TagSpecification>? TagSpecifications { get; init; }
    public InstanceMarketOptionsRequest? InstanceMarketOptions { get; init; }
    public string? UserData { get; init; }
}
