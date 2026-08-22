locals {
  policies = (
    var.service.xray.traces_enabled ?
    ["arn:aws:iam::aws:policy/AWSXrayWriteOnlyAccess", aws_iam_policy.elastic_machines_orchestration_policy.arn] :
    [aws_iam_policy.elastic_machines_orchestration_policy.arn]
  )
}

################################################################
# ECS Task Assume Role
################################################################

data "aws_iam_policy_document" "assume_role_policy" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

################################################################
# ECS Task Execution role + policy (for container access to secrets)
################################################################

data "aws_iam_policy_document" "elastic_machines_secrets_policy" {
  statement {
    actions = ["secretsmanager:GetSecretValue"]
    resources = concat([
      aws_secretsmanager_secret_version.GHCR.arn
    ], local.secret_arns)
  }
}

resource "aws_iam_role" "elastic_machines_ecs_task_execution_role" {
  name                = "${var.config_name}-ECS-task-exec-role"
  assume_role_policy  = data.aws_iam_policy_document.assume_role_policy.json
  managed_policy_arns = ["arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"]

  inline_policy {
    name   = "elastic-machines-secrets-policy"
    policy = data.aws_iam_policy_document.elastic_machines_secrets_policy.json
  }
}

################################################################
# ECS Task (Container) role
################################################################

resource "aws_iam_role" "elastic_machines_ecs_task_role" {
  name                = "${var.config_name}-ECS-task-role"
  assume_role_policy  = data.aws_iam_policy_document.assume_role_policy.json
  managed_policy_arns = local.policies
}
