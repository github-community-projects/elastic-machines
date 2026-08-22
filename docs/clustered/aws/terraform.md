# Terraform

[Terraform](https://www.terraform.io/) is an alternative way to deploy if you don't want to use [CloudFormation](README.md). Use the following example Terraform file to deploy a single Elastic Machines configuration or modify it to work in your environment (e.g. multiple configurations, modified resource limits, etc.):

```terraform
terraform {
  backend "s3" {
    bucket = "<bucket>"
    key    = "<key>"
    region = "us-west-2"
  }
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = "us-west-2"
}

variable "ghcr_token" {
  type      = string
  sensitive = true
}

variable "private_key_file" {
  type      = string
  sensitive = true
}

variable "configuration_file" {
  type = string
}

locals {
  name          = "<name>"
  configuration = file(var.configuration_file)
  private_key   = file(var.private_key_file)
}

data "aws_region" "current" {}

data "aws_caller_identity" "current" {}

resource "aws_secretsmanager_secret" "secret" {
  name = local.name
}

resource "aws_secretsmanager_secret_version" "secret_version" {
  secret_id = aws_secretsmanager_secret.secret.id
  secret_string = jsonencode({
    username    = "<username>"
    password    = var.ghcr_token
    PRIVATE_KEY = replace(local.private_key, "\n", "")
  })
}

resource "aws_iam_role" "execution_role" {
  name = "${local.name}-execution"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      },
    ]
  })
  managed_policy_arns = [
    "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
  ]
  inline_policy {
    name = "secrets"
    policy = jsonencode({
      Statement = [
        {
          Effect   = "Allow"
          Action   = "secretsmanager:GetSecretValue"
          Resource = aws_secretsmanager_secret.secret.arn
        },
      ]
    })
  }
}

resource "aws_iam_role" "task_role" {
  name = "${local.name}-task"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      },
    ]
  })
  managed_policy_arns = [
    "arn:aws:iam::aws:policy/AWSXrayWriteOnlyAccess"
  ]
  inline_policy {
    name = "orchestration"
    policy = jsonencode({
      Statement = [
        {
          Effect = "Allow"
          Action = [
            "ec2:TerminateInstances",
            "ec2:RunInstances",
            "autoscaling:TerminateInstanceInAutoScalingGroup",
            "autoscaling:AttachInstances",
          ]
          Resource = [
            "arn:aws:ec2:*::image/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:volume/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:subnet/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:security-group/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:network-interface/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:launch-template/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:key-pair/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:instance/*",
            "arn:aws:autoscaling:*:${data.aws_caller_identity.current.account_id}:autoScalingGroup:*:autoScalingGroupName/*",
          ]
        },
        {
          Effect = "Allow"
          Action = [
            "ssm:GetParameters",
            "ssm:GetCommandInvocation",
            "ssm:DescribeInstanceInformation",
            "logs:PutLogEvents",
            "logs:DescribeLogStreams",
            "logs:DescribeLogGroups",
            "logs:CreateLogStream",
            "logs:CreateLogGroup",
            "ec2:DescribeSubnets",
            "ec2:DescribeLaunchTemplateVersions",
            "ec2:DescribeInstances",
            "ec2:DescribeInstanceTypeOfferings",
            "ec2:DescribeImages",
            "ec2:DescribeHosts",
            "ec2:CreateTags",
            "autoscaling:DescribeAutoScalingInstances",
            "autoscaling:DescribeAutoScalingGroups",
          ]
          Resource = "*"
        },
        {
          Effect = "Allow"
          Action = [
            "ssm:SendCommand",
            "iam:PassRole",
          ]
          Resource = [
            "arn:aws:ssm:*::document/AWS-RunShellScript",
            "arn:aws:ssm:*::document/AWS-RunPowerShellScript",
            "arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/*",
            "arn:aws:ec2:*:${data.aws_caller_identity.current.account_id}:instance/*",
          ]
        },
      ]
    })
  }
}

resource "aws_iam_role" "instance_role" {
  name = "${local.name}-instance"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "ec2.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      },
    ]
  })
  managed_policy_arns = [
    "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
  ]
}

resource "aws_iam_instance_profile" "instance_profile" {
  name = "${local.name}-instance"
  role = aws_iam_role.instance_role.name
}

resource "aws_cloudwatch_log_group" "log_group" {
  name = local.name
}

resource "aws_cloudwatch_log_stream" "metrics_log_stream" {
  log_group_name = aws_cloudwatch_log_group.log_group.name
  name           = "metrics"
}

resource "aws_ecs_cluster" "cluster" {
  name = local.name
}

resource "aws_ecs_task_definition" "task_definition" {
  family                   = local.name
  cpu                      = 1024
  memory                   = 2048
  network_mode             = "awsvpc"
  execution_role_arn       = aws_iam_role.execution_role.arn
  task_role_arn            = aws_iam_role.task_role.arn
  requires_compatibilities = ["FARGATE"]
  container_definitions = jsonencode([
    {
      Name : yamldecode(local.configuration).name
      Image : "ghcr.io/elastic-machines/aws-instance:<version>"
      RepositoryCredentials : {
        credentialsParameter : aws_secretsmanager_secret_version.secret_version.arn
      }
      LogConfiguration : {
        LogDriver : "awslogs"
        Options : {
          "awslogs-group" : aws_cloudwatch_log_group.log_group.name
          "awslogs-region" : data.aws_region.current.name
          "awslogs-stream-prefix" : local.name
        }
      }
      Secrets : [
        {
          name : "PRIVATE_KEY"
          valueFrom : "${aws_secretsmanager_secret.secret.arn}:PRIVATE_KEY::"
        }
      ]
      Environment : [
        {
          name : "APP_ID"
          value : "<app-id>"
        },
        {
          name : "ENABLE_AWS_XRAY_TELEMETRY"
          value : "true"
        },
        {
          name : "CLOUDWATCH_METRICS_LOG_GROUP"
          value : aws_cloudwatch_log_group.log_group.name
        },
        {
          name : "CLOUDWATCH_METRICS_LOG_STREAM"
          value : aws_cloudwatch_log_stream.metrics_log_stream.name
        },
        {
          name : "CONFIGURATION"
          value : local.configuration
        },
      ]
    },
  ])
}

resource "aws_ecs_service" "service" {
  name            = local.name
  cluster         = aws_ecs_cluster.cluster.arn
  desired_count   = 1
  launch_type     = "FARGATE"
  task_definition = aws_ecs_task_definition.task_definition.arn
  network_configuration {
    subnets          = ["<subnet>"]
    security_groups  = ["<security-group>"]
    assign_public_ip = true
  }
}
```

Copy this template to a new `.tf` file and fill in any placeholders (denoted in `<brackets>`) with proper values for your environment. To deploy multiple Elastic Machines configurations, create more `aws_ecs_task_definition` and `aws_ecs_service` resources.

Deploy the Terraform template using the [typical planning and applying flow](https://developer.hashicorp.com/terraform/cli/run).

> [!WARNING]
> Consider setting up remote state before running the following commands. It's possible to deploy with local state by removing the `backend` block, but it will be harder to collaborate with others and deploy updates in the future.

1. Initialize the backend and providers:

   ```shell
   terraform init
   ```

2. Plan the deployment and make sure the changes look correct:

   ```shell
   terraform plan \
       --var ghcr_token=<ghcr-token> \
       --var private_key_file=<private-key>.pem \
       --var configuration_file=<configuration>.yml
   ```

3. Apply the changes once you are satisfied that the plan is correct:

   ```shell
   terraform apply \
       --var ghcr_token=<ghcr-token> \
       --var private_key_file=<private-key>.pem \
       --var configuration_file=<configuration>.yml
   ```

This example references an Elastic Machines configuration stored in a separate YAML file, but the configuration could also be passed in as a parameter or defined inline by using a [heredoc string](https://developer.hashicorp.com/terraform/language/expressions/strings#heredoc-strings):

```hcl
{
  name : "CONFIGURATION"
  value : <<EOT
    name: <name>
    url: <url>
    with:
      iamInstanceProfile:
        arn: arn:aws:iam::<account>:instance-profile/<instance-profile>
      imageId: <ami>
      region: us-west-2
      subnetId: <subnet-id>
      # ...
    EOT
},
```

Once deployed, refer to the main readme for more information about [supporting resources](README.md#supporting-resources) that the templates create.
