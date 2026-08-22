################################################################
# AWS Service Module Outputs
################################################################

output "deployment" {
  description = "The details of the deployed Elastic Machines configuration."
  value = {
    config_name = var.config_name
    region      = var.service.region
    container = {
      registry    = var.container.registry
      image       = var.container.image
      version     = var.container.version
      full_image  = local.image
      environment = merge(local.env, local.secrets)
    }
    tags = var.tags
  }
  sensitive = true
}

output "credentials_secret" {
  description = "The details of the created Secret Manager secrets."
  value       = aws_secretsmanager_secret.elastic_machines_secret_ghcr.arn
}

output "telemetry" {
  description = "The details of the deployed Elastic Machines telemetry configuration."
  value = {
    cloudwatch_log_group    = aws_cloudwatch_log_group.elastic_machines_ecs_container_cloudwatch_loggroup.name
    metrics_enabled         = var.service.cloud_watch.metrics_enabled
    metrics_log_stream_name = var.service.cloud_watch.metrics_enabled ? one(aws_cloudwatch_log_stream.elastic_machines_ecs_container_cloudwatch_metricstream).name : ""
    metrics_log_stream_arn  = var.service.cloud_watch.metrics_enabled ? one(aws_cloudwatch_log_stream.elastic_machines_ecs_container_cloudwatch_metricstream).id : ""
    xray_enabled            = var.service.xray.traces_enabled
  }
}

output "service" {
  description = "The details of the deployed Elastic Machines telemetry configuration."
  value = {
    name           = aws_ecs_service.elastic_machines_service.name
    cluster        = aws_ecs_cluster.elastic_machines_ecs_cluster.name
    task           = aws_ecs_task_definition.elastic_machines_ecs_task_definition.family
    host_name      = var.service.domain.name
    vpc_id         = local.vpc_id
    subnets        = local.subnets
    security_group = aws_security_group.elastic_machines_ecs_tasks_security_group.id
  }
}

output "alb" {
  description = "The details of the deployed Elastic Machines Application Load Balancer."
  value = {
    arn             = aws_lb.elastic_machines_load_balancer.arn
    vpc_id          = local.vpc_id
    subnets         = local.alb_subnets
    security_groups = local.alb_security_groups
  }
}
