#!/usr/bin/env sh

# The following script will import existing AWS resources into Terraform state.
# Note that you will need to replace the values in this script with ARNs, IDs or resource names from your AWS account.
# We have provided some commands using the `aws cli` to help you find these values, but you may need to use the AWS console to find some of them.
# For more information on importing resources into Terraform state, see https://registry.terraform.io/providers/hashicorp/aws/latest/docs and search
# for the particular resource you are importing, and navigate to the "Import" section of the docs.

# ------------------
# Network resources
# ------------------

# Hosted Zone listed with `aws route53 list-hosted-zones --query 'HostedZones[].Id'` (This returns array of "/hostedzone/<hosted-zone-id>")
terraform import 'module.service.aws_route53_zone.primary[0]' "<hosted-zone-id>"

# DNS records listed with `aws route53 list-resource-record-sets --hosted-zone-id <hosted-zone-id>` (This returns array containing <dns-name> and <type>)
terraform import 'module.service.aws_route53_record.alias_route53_record' "<hosted-zone-id>_<dns-name>_A"

# Security groups listed with `aws ec2 describe-security-groups --query 'SecurityGroups[].GroupId'` (This returns array of <security-group-id>)
terraform import 'module.service.aws_security_group.elastic_machines_lb_security_group[0]' "<security-group-id>"
terraform import 'module.service.aws_security_group.elastic_machines_ecs_tasks_security_group' "<security-group-id>"

# Load balancer listed with `aws elbv2 describe-load-balancers --query 'LoadBalancers[].LoadBalancerArn'` (This returns array of <load-balancer-arn>)
terraform import 'module.service.aws_lb.elastic_machines_load_balancer' "<load-balancer-arn>"

# Load balancer target groups listed with `aws elbv2 describe-target-groups --query 'TargetGroups[].TargetGroupArn'` (This returns array of <load-balancer-target-group-arn>)
terraform import 'module.service.aws_lb_target_group.elastic_machines_alb_target_group' "<load-balancer-target-group-arn>"

# Load balancer listeners listed with `aws elbv2 describe-listeners --load-balancer-arn <load-balancer-arn> --query 'Listeners[].ListenerArn'` (This returns array of <load-balancer-listener-arn>)
terraform import 'module.service.aws_lb_listener.https_forward' "<load-balancer-listener-arn>"

# ----------------------
# Cloudwatch resources
# ----------------------

# Cloudwatch log group listed with `aws logs describe-log-groups --query 'logGroups[].logGroupName'` (This returns array of <cloudwatch-log-group-name>)
terraform import 'module.service.aws_cloudwatch_log_group.elastic_machines_ecs_container_cloudwatch_loggroup' "<cloudwatch-log-group-name>"

# Cloudwatch log streams listed with `aws logs describe-log-streams --log-group-name <cloudwatch-log-group-name> --query 'logStreams[].logStreamName'` (This returns array of <cloudwatch-log-stream-name>)
terraform import 'module.service.aws_cloudwatch_log_stream.elastic_machines_ecs_container_cloudwatch_metricstream[0]' "<cloudwatch-log-group-name>:<cloudwatch-log-stream-name>"

# ------------------
# ECS resources
# ------------------

# List ECS clusters listed with `aws ecs describe-clusters --query 'clusters[].clusterName'` (This returns array of <ecs-cluster-name>)
terraform import 'module.service.aws_ecs_cluster.elastic_machines_ecs_cluster' "<ecs-cluster-name>"

# List ECS task definitions listed with `aws ecs list-task-definitions --query 'taskDefinitionArns[]'` (This returns array of <ecs-task-definition-arn>)
terraform import 'module.service.aws_ecs_task_definition.elastic_machines_ecs_task_definition' "<ecs-task-definition-arn>"

# List ECS services listed with `aws ecs list-services --cluster <ecs-cluster-name> --query 'serviceArns[]'` (This returns array of <ecs-service-arn>)
terraform import 'module.service.aws_ecs_service.elastic_machines_ecs_service' "<ecs-cluster-name>/<ecs-service-arn>"

# ------------------
# IAM resources
# ------------------

# IAM policies listed with `aws iam list-policies --query 'Policies[].Arn'` (This returns array of <iam-policy-arn>)
terraform import 'module.service.aws_iam_policy.elastic_machines_orchestration_policy' "<iam-policy-arn>"

# IAM roles listed with `aws iam list-roles --query 'Roles[].RoleName'` (This returns array of <iam-role-arn>)
terraform import 'module.service.aws_iam_role.elastic_machines_ecs_task_execution_role' "<iam-role-arn>"
terraform import 'module.service.aws_iam_role.elastic_machines_ecs_task_role' "<iam-role-arn>"

# ---------------------------
# Secrets manager resources
# ---------------------------

# Secrets listed with `aws secretsmanager list-secrets --query 'SecretList[].ARN'` (This returns array of <secret-arn>)
terraform import 'module.service.aws_secretsmanager_secret.elastic_machines_secret_ghcr' "<secret-arn>"

# Secrets versions listed with `aws secretsmanager list-secret-version-ids --secret-id <secret-arn> --query 'Versions[].VersionId'` (This returns array of <secret-version-id>)
terraform import 'module.service.aws_secretsmanager_secret_version.GHCR' "<secret-arn>|<secret-version-id>"
