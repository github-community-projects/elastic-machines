# AWS ECS

We recommend using Amazon Elastic Container Service when running an Elastic Machines cluster in AWS. Use the following example CloudFormation file to deploy a single Elastic Machines configuration or modify it to work in your environment (e.g. multiple configurations, higher resource limits, etc.):

```yaml
Transform: AWS::LanguageExtensions

Parameters:
  GhcrToken:
    Type: String
    NoEcho: true

  PrivateKey:
    Type: String
    NoEcho: true

Resources:
  Secret:
    Type: AWS::SecretsManager::Secret
    Properties:
      Name: !Ref AWS::StackName
      SecretString:
        Fn::ToJsonString:
          username: <username>
          password: !Ref GhcrToken
          PRIVATE_KEY: !Join ["", !Split ["\n", !Ref PrivateKey]]

  ExecutionRole:
    Type: AWS::IAM::Role
    Properties:
      RoleName: !Sub ${AWS::StackName}-execution
      AssumeRolePolicyDocument:
        Statement:
          - Effect: Allow
            Principal:
              Service: ecs-tasks.amazonaws.com
            Action: sts:AssumeRole
      ManagedPolicyArns:
        - arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy
      Policies:
        - PolicyName: secrets
          PolicyDocument:
            Statement:
              - Effect: Allow
                Action: secretsmanager:GetSecretValue
                Resource: !Ref Secret

  TaskRole:
    Type: AWS::IAM::Role
    Properties:
      RoleName: !Sub ${AWS::StackName}-task
      AssumeRolePolicyDocument:
        Statement:
          - Effect: Allow
            Principal:
              Service: ecs-tasks.amazonaws.com
            Action: sts:AssumeRole
      ManagedPolicyArns:
        - arn:aws:iam::aws:policy/AWSXrayWriteOnlyAccess
      Policies:
        - PolicyName: orchestration
          PolicyDocument:
            Statement:
              - Effect: Allow
                Action:
                  - ec2:TerminateInstances
                  - ec2:RunInstances
                  - autoscaling:TerminateInstanceInAutoScalingGroup
                  - autoscaling:AttachInstances
                Resource:
                  - arn:aws:ec2:*::image/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:volume/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:subnet/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:security-group/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:network-interface/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:launch-template/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:key-pair/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:instance/*
                  - !Sub arn:aws:autoscaling:*:${AWS::AccountId}:autoScalingGroup:*:autoScalingGroupName/*
              - Effect: Allow
                Action:
                  - ssm:GetParameters
                  - ssm:GetCommandInvocation
                  - ssm:DescribeInstanceInformation
                  - logs:PutLogEvents
                  - logs:DescribeLogStreams
                  - logs:DescribeLogGroups
                  - logs:CreateLogStream
                  - logs:CreateLogGroup
                  - ec2:DescribeSubnets
                  - ec2:DescribeLaunchTemplateVersions
                  - ec2:DescribeInstances
                  - ec2:DescribeInstanceTypeOfferings
                  - ec2:DescribeImages
                  - ec2:DescribeHosts
                  - ec2:CreateTags
                  - autoscaling:DescribeAutoScalingInstances
                  - autoscaling:DescribeAutoScalingGroups
                Resource: "*"
              - Effect: Allow
                Action:
                  - ssm:SendCommand
                  - iam:PassRole
                Resource:
                  - arn:aws:ssm:*::document/AWS-RunShellScript
                  - arn:aws:ssm:*::document/AWS-RunPowerShellScript
                  - !Sub arn:aws:iam::${AWS::AccountId}:role/*
                  - !Sub arn:aws:ec2:*:${AWS::AccountId}:instance/*

  InstanceRole:
    Type: AWS::IAM::Role
    Properties:
      RoleName: !Sub ${AWS::StackName}-instance
      AssumeRolePolicyDocument:
        Statement:
          - Effect: Allow
            Principal:
              Service: ec2.amazonaws.com
            Action: sts:AssumeRole
      ManagedPolicyArns:
        - arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore

  InstanceProfile:
    Type: AWS::IAM::InstanceProfile
    Properties:
      InstanceProfileName: !Ref InstanceRole
      Roles:
        - !Ref InstanceRole

  LogGroup:
    Type: AWS::Logs::LogGroup
    Properties:
      LogGroupName: !Ref AWS::StackName

  MetricsLogStream:
    Type: AWS::Logs::LogStream
    Properties:
      LogGroupName: !Ref LogGroup
      LogStreamName: metrics

  Cluster:
    Type: AWS::ECS::Cluster
    Properties:
      ClusterName: !Ref AWS::StackName
      CapacityProviders:
        - FARGATE

  TaskDefinition:
    Type: AWS::ECS::TaskDefinition
    Properties:
      Family: !Ref AWS::StackName
      cpu: 1024
      memory: 2048
      NetworkMode: awsvpc
      ExecutionRoleArn: !Ref ExecutionRole
      TaskRoleArn: !Ref TaskRole
      ContainerDefinitions:
        - Name: <name>
          Image: ghcr.io/elastic-machines/aws-instance:<version>
          repositoryCredentials:
            credentialsParameter: !Ref Secret
          LogConfiguration:
            LogDriver: awslogs
            Options:
              awslogs-group: !Ref LogGroup
              awslogs-region: !Ref AWS::Region
              awslogs-stream-prefix: !Ref AWS::StackName
          Secrets:
            - name: PRIVATE_KEY
              valueFrom: !Sub "${Secret}:PRIVATE_KEY::"
          Environment:
            - name: APP_ID
              value: <app-id>
            - name: ENABLE_AWS_XRAY_TELEMETRY
              value: true
            - name: CLOUDWATCH_METRICS_LOG_GROUP
              value: !Ref LogGroup
            - name: CLOUDWATCH_METRICS_LOG_STREAM
              value: !Ref MetricsLogStream
            - name: CONFIGURATION
              value: !Sub |
                name: <name>
                url: <url>
                with:
                  iamInstanceProfile:
                    arn: ${InstanceProfile.Arn}
                  imageId: <ami>
                  region: us-west-2
                  subnetId: <subnet-id>
                  # ...

  Service:
    Type: AWS::ECS::Service
    Properties:
      Cluster: !Ref Cluster
      DesiredCount: 1
      LaunchType: FARGATE
      TaskDefinition: !Ref TaskDefinition
      NetworkConfiguration:
        AwsvpcConfiguration:
          Subnets:
            - <subnet>
          SecurityGroups:
            - <security-group>
          AssignPublicIp: ENABLED
```

Copy this template to a new `.yml` file and fill in any placeholders (denoted in `<brackets>`) with proper values for your environment. To deploy multiple Elastic Machines configurations, duplicate the `TaskDefinition` and `Service` resources for each one.

Deploy the CloudFormation file with the following [command](https://awscli.amazonaws.com/v2/documentation/api/latest/reference/cloudformation/deploy/index.html):

```shell
aws cloudformation deploy \
    --stack-name <stack-name> \
    --template-file <template-file>.yml \
    --capabilities CAPABILITY_NAMED_IAM \
    --parameter-overrides GhcrToken=<ghcr-token> PrivateKey=<private-key>
```

Elastic Machines should start running as soon as the CloudFormation stack finishes deploying. To modify the deployment or add Elastic Machines configurations, update the YAML file and redeploy using the same command. CloudFormation will calculate the delta and deploy any required changes.

## Supporting resources

This CloudFormation stack includes quite a few supporting resources. Here is a brief overview of each resource:

* `Secret`: The secret stores the container registry login credentials and the private key used for authentication with GitHub
* `ExecutionRole`: The execution role is used to fetch the secret, pull the Elastic Machines container, and launch the ECS task
* `TaskRole`: The task role is used by the Elastic Machines container to authenticate against AWS APIs once the ECS task starts up
* `InstanceRole`: The instance role is used by EC2 instances so that they can connect to AWS Systems Manager (SSM) to receive commands
* `InstanceProfile`: The instance profile is used to attach the instance role to EC2 instances
* `LogGroup`: The log group stores logs and metrics from the Elastic Machines container
* `MetricsLogStream`: The metrics log stream stores metrics from the Elastic Machines container
* `Cluster`: The ECS cluster runs the Elastic Machines container tasks
* `TaskDefinition`: The task definition specifies the Elastic Machines container image and configuration
* `Service`: The ECS service runs the Elastic Machines container tasks in the ECS cluster
