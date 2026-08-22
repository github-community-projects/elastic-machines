# AWS EC2 Instance Provider

The AWS EC2 instance provider allows you to scale runners on EC2 instances without deploying any additional infrastructure (e.g. Auto Scaling groups). All instance configuration lives in the Elastic Machines configuration file described below.

## YAML Configuration

To use this provider, specify `aws-instance@v1` as the `provider`. A minimal configuration should include the following properties:

```yaml
- name: <name>
  provider: aws-instance@v1
  with:
    # The name or Amazon Resource Name (ARN) of an IAM instance profile.
    iamInstanceProfile:
      # The Amazon Resource Name (ARN) of the instance profile.
      arn: arn:aws:iam::<account>:instance-profile/<instance-profile>
    # The ID of the AMI.
    imageId: <ami>
    # The AWS Region to send requests to.
    region: us-west-2
    # The ID of the subnet to launch the instance into.
    subnetId: <subnet-id>
```

> [!NOTE]
> The `iamInstanceProfile` must have the `AmazonSSMManagedInstanceCore` policy attached so EC2 instances can connect to AWS Systems Manager. Refer to the [Systems Manager](#systems-manager) section for more information.

Use the optional properties documented below to customize the configuration further. Refer to the AWS API's [RunInstances documentation](https://docs.aws.amazon.com/AWSEC2/latest/APIReference/API_RunInstances.html) for more information on each property (except for the `roleArn` property, which is specific to Elastic Machines and is explained in more detail in the [multiple accounts](#multiple-accounts) section below).

```yaml
- name: <name>
  provider: aws-instance@v1
  with:
    iamInstanceProfile:
      arn: arn:aws:iam::<account>:instance-profile/<instance-profile>
    imageId: <image-id>
    region: us-west-2
    subnetId: <subnet-id>

    # The block device mapping, which defines the EBS volumes and instance store volumes to attach to the instance at launch.
    blockDeviceMappings:
      # The device name (for example, /dev/sdh or xvdh).
      - deviceName: <device-name>
        # Parameters used to automatically set up EBS volumes when the instance is launched.
        ebs:
          # Indicates whether the EBS volume is deleted on instance termination.
          deleteOnTermination: true
          # Indicates whether the encryption state of an EBS volume is changed while being restored from a backing snapshot.
          # The effect of setting the encryption state to true depends on the volume origin (new or from a snapshot), starting encryption state, ownership, and whether encryption by default is enabled.
          encrypted: true
          # The number of I/O operations per second (IOPS).
          # For gp3, io1, and io2 volumes, this represents the number of IOPS that are provisioned for the volume.
          # For gp2 volumes, this represents the baseline performance of the volume and the rate at which the volume accumulates I/O credits for bursting.
          iops: 123
          # Identifier (key ID, key alias, ID ARN, or alias ARN) for a customer managed CMK under which the EBS volume is encrypted.
          kmsKeyId: <kms-key-id>
          # The ARN of the Outpost on which the snapshot is stored.
          outpostArn: arn:aws:outposts:<region>:<account>:outpost/<outpost>
          # The ID of the snapshot.
          snapshotId: <snapshot-id>
          # The throughput that the volume supports, in MiB/s.
          throughput: 123
          # The size of the volume, in GiBs.
          # You must specify either a snapshot ID or a volume size.
          # If you specify a snapshot, the default is the snapshot size.
          # You can specify a volume size that is equal to or larger than the snapshot size.
          volumeSize: 123
          # The volume type.
          volumeType: standard # Options: standard, io1, io2, gp2, sc1, st1, gp3
        # To omit the device from the block device mapping, specify an empty string.
        # When this property is specified, the device is removed from the block device mapping regardless of the assigned value.
        noDevice: <no-device>
        # The virtual device name (ephemeralN).
        # Instance store volumes are numbered starting from 0.
        # An instance type with 2 available instance store volumes can specify mappings for ephemeral0 and ephemeral1.
        #The number of available instance store volumes depends on the instance type.
        #After you connect to the instance, you must mount the volume.
        virtualName: <virtual-name>
    # The market (purchasing) option for the instances.
    instanceMarketOptions:
      # The market type.
      marketType: spot # Options: spot, capacity-block
      # The options for Spot Instances.
      spotOptions:
        # The maximum hourly price that you're willing to pay for a Spot Instance.
        # We do not recommend using this parameter because it can lead to increased interruptions.
        # If you do not specify this parameter, you will pay the current Spot price.
        maxPrice: 0.12
    # The instance type.
    instanceType: m1.small
    # The name of the key pair.
    keyName: <key-name>
    # The placement for the instance.
    placement:
      # The tenancy of the instance. An instance with a tenancy of dedicated runs on single-tenant hardware.
      tenancy: default # Options: default, dedicated, host
    # The Amazon Resource Name (ARN) of the role to assume.
    roleArn: arn:aws:iam::<account>:role/<role>
    # The IDs of the security groups.
    securityGroupIds:
      - <security-group-id>
    # The tags to apply to the resources that are created during instance launch.
    tagSpecifications:
      # The type of resource to tag on creation.
      - resourceType: instance # Options: instance, volume
        # The tags to apply to the resource.
        tags:
          # The key of the tag.
          - key: <key>
            # The value of the tag.
            value: <value>
    # The user data to make available to the instance.
    # User data must be base64-encoded.
    userData: <user-data>

# Additional provider blocks as needed
- name: <name>
  provider: aws-instance@v1
  with:
    # ...
```

## Authentication

Terraform automatically configures authentication information when deploying Elastic Machines to AWS with the provided templates. Terraform creates an ECS task role (with the `-ECS-task-role` suffix) and gives it all necessary permissions. Manual setup should only be required in rare circumstances (e.g. when using [multiple accounts](#multiple-accounts)).

However, to manually run Elastic Machines outside of AWS, use environment variables for authentication.

* `AWS_ACCESS_KEY_ID`: IAM user's access key ID
* `AWS_SECRET_ACCESS_KEY`: IAM users's secret access key

The IAM user used for manual authentication must have the following permissions (fill in the account ID where appropriate):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "ec2:RunInstances",
        "ec2:TerminateInstances"
      ],
      "Resource": [
        "arn:aws:ec2:*::image/*",
        "arn:aws:ec2:*:<account>:instance/*",
        "arn:aws:ec2:*:<account>:key-pair/*",
        "arn:aws:ec2:*:<account>:launch-template/*",
        "arn:aws:ec2:*:<account>:network-interface/*",
        "arn:aws:ec2:*:<account>:security-group/*",
        "arn:aws:ec2:*:<account>:subnet/*",
        "arn:aws:ec2:*:<account>:volume/*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "ec2:CreateTags",
        "ec2:DescribeHosts",
        "ec2:DescribeImages",
        "ec2:DescribeInstances",
        "ec2:DescribeInstanceTypeOfferings",
        "ec2:DescribeLaunchTemplateVersions",
        "ec2:DescribeSubnets",
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:DescribeLogGroups",
        "logs:DescribeLogStreams",
        "logs:PutLogEvents",
        "ssm:DescribeInstanceInformation",
        "ssm:GetCommandInvocation",
        "ssm:GetParameters",
        "sts:AssumeRole"
      ],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "iam:PassRole",
        "ssm:SendCommand"
      ],
      "Resource": [
        "arn:aws:ec2:*:<account>:instance/*",
        "arn:aws:iam::<account>:role/*",
        "arn:aws:ssm:*:*:document/AWS-RunPowerShellScript",
        "arn:aws:ssm:*:*:document/AWS-RunShellScript"
      ]
    }
  ]
}
```

## Systems Manager

Elastic Machines requires an IAM instance profile with the `AmazonSSMManagedInstanceCore` policy attached and EC2 instances must also be able to communicate with AWS Systems Manager after they start up. Elastic Machines will time out waiting for instances that take longer than 10 minutes to connect and will retry with new instances. Take the following troubleshooting steps if Elastic Machines is consistently timing out (especially when using a custom subnet):

* Ensure the [SSM agent](https://docs.aws.amazon.com/systems-manager/latest/userguide/prereqs-ssm-agent.html) is installed in your AMI (most AMIs provided by AWS have the agent preinstalled)

* Follow the steps in this [troubleshooting guide](https://aws.amazon.com/premiumsupport/knowledge-center/systems-manager-ec2-instance-not-appear/) to ensure instances can communicate with AWS Systems Manager

* Use the [VPC Reachability Analyzer](https://docs.aws.amazon.com/vpc/latest/reachability/getting-started.html) to ensure instances can reach the internet through the VPC's internet gateway

* Check that the **Auto-assign public IPv4 address** setting is enabled on the subnet and enable it if needed

  ```shell
  aws ec2 modify-subnet-attribute --subnet-id <subnet-id> --map-public-ip-on-launch
  ```

## Multiple accounts

Elastic Machines can manage instances in other accounts by assuming a role in the target account that has the required permissions. Assuming a role requires two pieces of configuration:

1. The role assigned to Elastic Machines must have permission to assume the target role. Ensure Elastic Machines's role has the `sts:AssumeRole` permission (Terraform will automatically add this permission to a role with the `-ECS-task-role` suffix when deployed with the provided templates):

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Action": "sts:AssumeRole",
         "Resource": "*"
       }
     ]
   }
   ```

2. The target role must have a [trust relationship](https://aws.amazon.com/blogs/security/how-to-use-trust-policies-with-iam-roles/) that allows Elastic Machines to assume it. Create a trust relationship in the target role using the following template (fill in the account ID and Elastic Machines role name):

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Action": "sts:AssumeRole",
         "Principal": {
           "AWS": "arn:aws:iam::<account>:role/<role>"
         }
       }
     ]
   }
   ```

Make sure Elastic Machines can manage resources in the target subscription by giving the target role the same [permissions](#authentication) as Elastic Machines's original role. Once Elastic Machines assumes the new role, none of its previous permissions will cary over. Refer to AWS's [tutorial on delegating access across accounts](https://docs.aws.amazon.com/IAM/latest/UserGuide/tutorial_cross-account-with-roles.html) for more information on assuming roles.
