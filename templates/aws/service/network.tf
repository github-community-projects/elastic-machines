################################################################
# Locals
################################################################

locals {
  # Determine whether to use the default VPC or a specified VPC
  vpc_id = coalesce(var.service.network.vpc_id, length(data.aws_vpc.default) > 0 ? one(data.aws_vpc.default).id : "INVALID_VPC")

  # Use either the subnet(s) set in the config OR find subnet(s) for the VPC we're using
  subnets = coalescelist(
    tolist(var.service.network.subnets),
    [
      # Filter subnets so we're only taking 1 per availability zone
      for zone, subnets in data.aws_subnets.elastic_machines_vpc_subnets : subnets.ids[0]
      if length(subnets.ids) > 0
    ]
  )

  # Use ALB subnet(s) set in the config or the same subnet(s) we calculated above
  alb_subnets = coalescelist(tolist(var.service.network.alb_subnets), local.subnets)
  # Determine whether we're using ALB security group(s) from the configuration or creating a new one
  alb_security_groups = length(var.service.network.alb_security_groups) > 0 ? var.service.network.alb_security_groups : [one(aws_security_group.elastic_machines_lb_security_group).id]

  # Try to determine if we're working with an apex (<whatever>.com) domain vs. a subdomain (my.<whatever>.com)
  domain_parts = split(".", var.service.domain.name)
  # Attempt to find the top level/apex domain, if we have > 2 parts
  apex_domain_name = length(local.domain_parts) > 2 ? join(".", slice(local.domain_parts, 1, length(local.domain_parts))) : var.service.domain.name
  # Get the zone ID of a newly created zone or Retrieve the existing zone ID
  zone_id = length(aws_route53_zone.primary) > 0 ? one(aws_route53_zone.primary).zone_id : one(data.aws_route53_zone.primary).zone_id
}

################################################################
# Find subnet(s) to use
################################################################

data "aws_availability_zones" "available" {
  state = "available"

  filter {
    name   = "region-name"
    values = ["${var.service.region}"]
  }
}

data "aws_vpc" "default" {
  # Is there a VPC set in the config?
  count   = var.service.network.vpc_id == null ? 1 : 0
  default = true
}

data "aws_subnets" "elastic_machines_vpc_subnets" {
  # Are there subnets set in the config?
  for_each = length(var.service.network.subnets) == 0 ? toset(data.aws_availability_zones.available.zone_ids) : []

  # Find the subnet(s) for the default VPC or the VPC specified
  filter {
    name   = "vpc-id"
    values = [local.vpc_id]
  }

  filter {
    name   = "availability-zone-id"
    values = ["${each.value}"]
  }
}

################################################################
# Route53 DNS
################################################################

# Determine if we need to create a new hosted Zone or if there's an existing hosted Zone and grab that
resource "aws_route53_zone" "primary" {
  count = var.service.domain.create_hosted_zone ? 1 : 0
  name  = local.apex_domain_name
}

data "aws_route53_zone" "primary" {
  count = var.service.domain.create_hosted_zone ? 0 : 1
  name  = local.apex_domain_name
}

# Alias A record to associate our domain with the dns assigned to the ALB
resource "aws_route53_record" "alias_route53_record" {
  zone_id = local.zone_id
  name    = var.service.domain.name
  type    = "A"

  alias {
    name                   = aws_lb.elastic_machines_load_balancer.dns_name
    zone_id                = aws_lb.elastic_machines_load_balancer.zone_id
    evaluate_target_health = true
  }
}

################################################################
# Certificate
################################################################

resource "aws_acm_certificate" "certificate_request" {
  count = var.service.domain.certificate_arn == null ? 1 : 0

  domain_name               = var.service.domain.create_hosted_zone ? local.apex_domain_name : var.service.domain.name
  subject_alternative_names = var.service.domain.create_hosted_zone ? ["*.${local.apex_domain_name}"] : null
  validation_method         = "DNS"

  lifecycle {
    create_before_destroy = true # recommended for this resource type
  }
}

# insert CNAME record(s) into the Route53 DNS; this allows for auto validation of the cert request
resource "aws_route53_record" "route53_validation_record" {
  for_each = {
    for dvo in(var.service.domain.certificate_arn != null ? [] : aws_acm_certificate.certificate_request[0].domain_validation_options) : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  }

  allow_overwrite = true
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
  zone_id         = local.zone_id
}

resource "aws_acm_certificate_validation" "certificate_validation" {
  count = var.service.domain.certificate_arn == null ? 1 : 0

  certificate_arn         = one(aws_acm_certificate.certificate_request).arn
  validation_record_fqdns = [for record in aws_route53_record.route53_validation_record : record.fqdn]
}

################################################################
# Load Balancer configuration
################################################################

resource "aws_security_group" "elastic_machines_lb_security_group" {
  count = length(var.service.network.alb_security_groups) == 0 ? 1 : 0

  name_prefix            = "${var.config_name}-lb-sg"
  description            = "Controls access to the Application Load Balancer"
  vpc_id                 = local.vpc_id
  revoke_rules_on_delete = true

  ingress {
    description = "TLS from VPC"
    protocol    = "tcp"
    from_port   = 443
    to_port     = 443
    cidr_blocks = ["0.0.0.0/0"]
  }

  # "Allow All" outbound rule
  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.config_name}-lb-sg"
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_security_group" "elastic_machines_ecs_tasks_security_group" {
  name_prefix            = "${var.config_name}-ecs-tasks-sg"
  description            = "Allow inbound access from the ALB only"
  vpc_id                 = local.vpc_id
  revoke_rules_on_delete = true

  ingress {
    description     = "Allow HTTP from ALB to ECS/Container"
    protocol        = "tcp"
    from_port       = 80
    to_port         = 80
    security_groups = local.alb_security_groups
  }

  # "Allow All" outbound rule
  egress {
    protocol    = "-1"
    from_port   = 0
    to_port     = 0
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.config_name}-ecs-tasks-sg"
  }

  depends_on = [aws_security_group.elastic_machines_lb_security_group]

  lifecycle {
    create_before_destroy = true
  }
}

resource "aws_lb" "elastic_machines_load_balancer" {
  name               = "${var.config_name}-lb"
  load_balancer_type = "application"
  security_groups    = local.alb_security_groups
  subnets            = local.alb_subnets

  depends_on = [
    aws_security_group.elastic_machines_lb_security_group,
    aws_security_group.elastic_machines_ecs_tasks_security_group
  ]

  lifecycle {
    replace_triggered_by = [aws_security_group.elastic_machines_ecs_tasks_security_group.vpc_id]
  }
}

resource "aws_lb_target_group" "elastic_machines_alb_target_group" {
  name        = "${var.config_name}-lb-tg"
  port        = 80
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = local.vpc_id

  health_check {
    healthy_threshold   = 3
    interval            = 300
    protocol            = "HTTP"
    matcher             = "200"
    timeout             = 20
    path                = "/"
    unhealthy_threshold = 2
  }

  depends_on = [aws_lb.elastic_machines_load_balancer]
}

resource "aws_lb_listener" "https_forward" {
  load_balancer_arn = aws_lb.elastic_machines_load_balancer.arn
  port              = 443
  protocol          = "HTTPS"

  certificate_arn = var.service.domain.certificate_arn != null ? var.service.domain.certificate_arn : one(aws_acm_certificate_validation.certificate_validation).certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.elastic_machines_alb_target_group.arn
  }

  lifecycle {
    replace_triggered_by = [aws_lb_target_group.elastic_machines_alb_target_group]
  }
}
