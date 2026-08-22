package azure

import (
	"fmt"

	"github.com/elastic-machines/monolith/test/shared"
)

type Deployment struct {
	shared.Deployment
	SubscriptionId string `json:"subscription_id"`
	ResourceGroup  *ResourceGroup
	AppService     *AppService
	AppInsights    *AppInsights
}

func (deployment Deployment) GetBaseDeploymentDetails() shared.Deployment {
	return deployment.Deployment
}

func (deployment Deployment) FormatSecret(secret string) string {
	return fmt.Sprintf("@Microsoft.KeyVault(SecretUri=%s)", secret)
}
