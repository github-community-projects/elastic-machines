################################################################
# Locals
################################################################

locals {
  # A map of env vars that will be passed to the container
  env = { for name, value in merge({
    ENABLE_AWS_XRAY_TELEMETRY = var.service.xray.traces_enabled
    }, var.service.cloud_watch.metrics_enabled ? {
    CLOUDWATCH_METRICS_LOG_GROUP  = aws_cloudwatch_log_group.elastic_machines_ecs_container_cloudwatch_loggroup.name
    CLOUDWATCH_METRICS_LOG_STREAM = one(aws_cloudwatch_log_stream.elastic_machines_ecs_container_cloudwatch_metricstream).name
    } : {}, {
    # Try to resolve the value as a file, then a literal value in either form
    for envName, envValue in var.container.environment : envName => try(
      chomp(file(envValue.file)),
      envValue.value,
      envValue
    )
    if !can(envValue.secret)
  }) : name => tostring(value) if value != null }

  secrets = {
    for envName, envValue in var.container.environment : envName => format("%s%s", envValue.secret, join("", [for _ in range(length(split(":", envValue.secret)), 10) : ":"]))
    if can(envValue.secret)
  }

  image      = "${var.container.registry}/${var.container.image}:${var.container.version}"
  image_name = split("/", var.container.image)[1]
}

################################################################
# ECS Cluster, Task Definition, and Service definitions
################################################################

resource "aws_ecs_cluster" "elastic_machines_ecs_cluster" {
  name = "${var.config_name}-cluster"
}

resource "aws_ecs_task_definition" "elastic_machines_ecs_task_definition" {
  family                   = "${var.config_name}-ECS-task-def"
  task_role_arn            = aws_iam_role.elastic_machines_ecs_task_role.arn
  execution_role_arn       = aws_iam_role.elastic_machines_ecs_task_execution_role.arn
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "512"
  memory                   = "1024"
  container_definitions = templatefile("${path.module}/container.tmpl", {
    name       = local.image_name,
    log_group  = aws_cloudwatch_log_group.elastic_machines_ecs_container_cloudwatch_loggroup.name,
    region     = var.service.region,
    ghcr_image = local.image,
    ghcr_arn   = aws_secretsmanager_secret_version.GHCR.arn,
    env        = local.env,
    secrets    = local.secrets
  })
}

resource "aws_ecs_service" "elastic_machines_service" {
  name            = "${var.config_name}-ECS-svc"
  cluster         = aws_ecs_cluster.elastic_machines_ecs_cluster.id
  task_definition = aws_ecs_task_definition.elastic_machines_ecs_task_definition.arn

  launch_type          = "FARGATE"
  desired_count        = 1
  force_new_deployment = true # ensures task will be redeployed and latest container image pulled, even if using ':latest'

  network_configuration {
    security_groups  = [aws_security_group.elastic_machines_ecs_tasks_security_group.id]
    subnets          = local.subnets
    assign_public_ip = true # required to properly retrieve secrets upon task/service launch (https://stackoverflow.com/a/66802973/812415)
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.elastic_machines_alb_target_group.arn
    container_name   = local.image_name
    container_port   = 80
  }
}
