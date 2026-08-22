package azure

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

type Resource struct {
	Name           string
	DeploymentName string
	SubscriptionId string `json:"subscription_id"`
	ResourceGroup  string `json:"resource_group"`
	Tags           map[string]string
}

func (resource Resource) ValidateTags(t *testing.T, tags map[string]*string) {
	for k, v := range tags {
		assert.Equal(t, resource.Tags[k], *v)
	}
}

func (resource *Resource) Augment(deployment Deployment) {
	resource.DeploymentName = deployment.Name

	if resource.SubscriptionId == "" {
		resource.SubscriptionId = deployment.SubscriptionId
	}

	if resource.ResourceGroup == "" && deployment.ResourceGroup != nil {
		resource.ResourceGroup = deployment.ResourceGroup.Name
	}

	if resource.Tags == nil {
		resource.Tags = deployment.Tags
	}
}
