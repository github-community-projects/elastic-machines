variable "config_name" {
  description = "The name of the Elastic Machines configuration to deploy. This name is used as a prefix for the AWS resources that are created in this module."
  type        = string
  nullable    = false
}

variable "tags" {
  description = "A map of tags that will be applied across created cloud resources."
  type        = map(string)
  default     = null
}

variable "container" {
  description = "The details of the Elastic Machines container image to be used for this deployment."
  type = object({
    # The login and access token that will be used to authenticate to the container image registry
    login = string # This is used for Docker login, and can generally be set to any user name provided the token is valid
    token = string

    # The image registry and organization that hosts the Elastic Machines container image
    registry = optional(string, "ghcr.io") # default: ghcr.io

    # The name and version/tag of the Elastic Machines container image
    image   = optional(string, "elastic-machines/monolith") # default: elastic-machines/monolith
    version = optional(string, "latest")                                      # default: latest

    # A list of environment variables to be passed to the Elastic Machines container
    environment = any
  })
  nullable = false
}

variable "service" {
  description = "Details for the Elastic Machines service that will run on AWS and talk to GitHub."
  type = object({
    # The AWS region to use for this deployment
    region = string
    # A named AWS profile to use for authentication. See: https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-files.html
    profile = optional(string)

    # The domain details to use for the Elastic Machines service
    domain = object({
      # The full dns name that will be used for the Elastic Machines service
      name = string
      # True to create a hosted zone in Route53 DNS with the apex domain portion of domain name, e.g. elastic-machines.com
      # This should be set to true for domains registered in external services, and false for domains created in Route53, where a hosted zone will already be created
      create_hosted_zone = optional(bool, false)
      # The ARN of an AWS ACM certificate that already exists in the account and region for this domain. If unset, a new certificate will be created
      certificate_arn = optional(string)
    })

    # The network details to be used for the Elastic Machines service. Named or default subnets will come from from the specified VPC or the default VPC if no VPC is specified. Omit this block to use the default VPC and subnets
    # Note: The VPC used must have at least 2 subnets in different availability zones
    network = optional(object({
      # The ID of an existing VPC to use for the Elastic Machines service
      vpc_id = optional(string)
      # One or more existing subnet(s) to use for the service resources. If not set, default subnets for the specified/default VPC will be used
      subnets = optional(set(string), [])
      # One or more existing subnet(s) to use for the Application Load Balancer
      # This should be a public subnet that allows internet traffic
      alb_subnets = optional(set(string), []) # Default: the subnets set in network.subnets or the default subnet(s) for the VPC being used
      # One or more existing security group(s) to use for the Application Load Balancer
      # The security group(s) used must allow inbound access for GitHub webhooks and outbound access to make GitHub and cloud API calls
      alb_security_groups = optional(set(string), []) # Default: Security groups will be created to allow inbound access to the Application Load Balancer over port 443 and from the ALB to the running service container over port 80
    }), {})

    # AWS XRay configuration for telemetry and monitoring of the Elastic Machines service
    xray = optional(object({
      # A value indicating whether to use XRay for telemetry
      traces_enabled = optional(bool, true)
      }), {
      traces_enabled = true
    })

    # AWS CloudWatch configuration for telemetry and monitoring of the Elastic Machines service
    cloud_watch = optional(object({
      # A value indicating whether to use CloudWatch for telemetry/monitoring
      metrics_enabled = optional(bool, true)
      }), {
      metrics_enabled = true
    })
  })
  nullable = false
}
