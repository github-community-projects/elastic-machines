################################################################
# CloudWatch log & metric configuration
################################################################

resource "aws_cloudwatch_log_group" "elastic_machines_ecs_container_cloudwatch_loggroup" {
  name = "${var.config_name}-loggroup"
}

resource "aws_cloudwatch_log_stream" "elastic_machines_ecs_container_cloudwatch_metricstream" {
  count          = var.service.cloud_watch.metrics_enabled ? 1 : 0
  name           = "${var.config_name}-metricstream"
  log_group_name = aws_cloudwatch_log_group.elastic_machines_ecs_container_cloudwatch_loggroup.name
}
