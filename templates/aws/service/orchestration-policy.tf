################################################################
# Local vars
################################################################

locals {
  account_id = data.aws_caller_identity.current.account_id
}

data "aws_caller_identity" "current" {}

################################################################
# Create a policy allowing management of EC2 resources, auto scaling groups, logging, etc.
################################################################

data "aws_iam_policy_document" "elastic_machines_orchestration_policy" {
  statement {
    actions = [
      "sts:AssumeRole"
    ]
    resources = [
      "arn:aws:iam::*:role/*"
    ]
  }

  statement {
    actions = [
      "ec2:TerminateInstances",
      "autoscaling:AttachInstances",
      "ec2:RunInstances",
      "autoscaling:TerminateInstanceInAutoScalingGroup"
    ]
    resources = [
      "arn:aws:autoscaling:*:${local.account_id}:autoScalingGroup:*:autoScalingGroupName/*",
      "arn:aws:ec2:*::image/*",
      "arn:aws:ec2:*:${local.account_id}:instance/*",
      "arn:aws:ec2:*:${local.account_id}:key-pair/*",
      "arn:aws:ec2:*:${local.account_id}:launch-template/*",
      "arn:aws:ec2:*:${local.account_id}:network-interface/*",
      "arn:aws:ec2:*:${local.account_id}:security-group/*",
      "arn:aws:ec2:*:${local.account_id}:subnet/*",
      "arn:aws:ec2:*:${local.account_id}:volume/*"
    ]
  }

  statement {
    actions = [
      "autoscaling:DescribeAutoScalingGroups",
      "autoscaling:DescribeAutoScalingInstances",
      "ec2:CreateTags",
      "ec2:DescribeImages",
      "ec2:DescribeInstances",
      "ec2:DescribeInstanceTypeOfferings",
      "ec2:DescribeLaunchTemplateVersions",
      "ec2:DescribeSubnets",
      "ec2:DescribeHosts",
      "ssm:GetParameters",
      "ssm:GetCommandInvocation",
      "ssm:DescribeInstanceInformation",
      "logs:CreateLogGroup",
      "logs:CreateLogStream",
      "logs:PutLogEvents",
      "logs:DescribeLogGroups",
      "logs:DescribeLogStreams"
    ]
    resources = ["*"]
  }

  statement {
    actions = [
      "ssm:SendCommand",
      "iam:PassRole"
    ]
    resources = [
      "arn:aws:ssm:*::document/AWS-RunShellScript",
      "arn:aws:ssm:*::document/AWS-RunPowerShellScript",
      "arn:aws:ec2:*:${local.account_id}:instance/*",
      "arn:aws:iam::${local.account_id}:role/*"
    ]
  }
}

resource "aws_iam_policy" "elastic_machines_orchestration_policy" {
  name        = "${var.config_name}-orchestration-policy"
  description = "This policy allows Elastic Machines to orchestrate resources needed to create and manage runners in AWS."
  policy      = data.aws_iam_policy_document.elastic_machines_orchestration_policy.json
}
