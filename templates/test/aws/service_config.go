package aws

import "github.com/elastic-machines/monolith/test/shared"

type ServiceConfig struct {
	shared.ServiceConfig `yaml:",inline"`
	Region               string            `yaml:"region"`
	Domain               DomainConfig      `yaml:"domain"`
	Network              *NetworkConfig    `yaml:"network"`
	XRay                 *XRayConfig       `yaml:"xray"`
	CloudWatch           *CloudWatchConfig `yaml:"cloud_watch"`
}

type DomainConfig struct {
	Name        string `yaml:"name"`
	Certificate string `yaml:"certificate_arn"`
}

type NetworkConfig struct {
	VPC               string   `yaml:"vpc_id"`
	Subnets           []string `yaml:"subnets"`
	ALBSubnets        []string `yaml:"alb_subnets"`
	ALBSecurityGroups []string `yaml:"alb_security_groups"`
}

type XRayConfig struct {
	Enabled bool `yaml:"traces_enabled"`
}

type CloudWatchConfig struct {
	Enabled bool `yaml:"metrics_enabled"`
}
