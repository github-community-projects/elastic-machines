package azure

import "github.com/elastic-machines/monolith/test/shared"

type ServiceConfig struct {
	shared.ServiceConfig `yaml:",inline"`
	SubscriptionID       string               `yaml:"subscription_id"`
	ResourceGroup        *ResourceGroupConfig `yaml:"resource_group"`
	AppService           *AppServiceConfig    `yaml:"app_service"`
	AppInsights          *AppInsightsConfig   `yaml:"app_insights"`
}

type ResourceGroupConfig struct {
	Name     string `yaml:"name"`
	Location string `yaml:"region"`
}

type AppServiceConfig struct {
	Name     string
	PlanSku  string          `yaml:"plan_sku"`
	Identity *IdentityConfig `yaml:"identity"`
}

type IdentityConfig struct {
	Name          string `yaml:"name"`
	ResourceGroup string `yaml:"resource_group"`
}

type AppInsightsConfig struct {
	Enabled       *bool  `yaml:"enabled"`
	Name          string `yaml:"name"`
	ResourceGroup string `yaml:"resource_group"`
}
