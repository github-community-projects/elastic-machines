package azure

import (
	"strings"
	"testing"

	"github.com/gruntwork-io/terratest/modules/azure"
	"github.com/stretchr/testify/assert"
)

type ResourceGroup struct {
	Resource
	Location string
	Managed  bool `json:"is_managed"`
}

func (resourceGroup ResourceGroup) VerifyCloudResources(t *testing.T) {
	assert := assert.New(t)
	azure.ResourceGroupExists(t, resourceGroup.Name, resourceGroup.SubscriptionId)
	rg := azure.GetAResourceGroup(t, resourceGroup.Name, resourceGroup.SubscriptionId)

	assert.Equal(resourceGroup.Location, *rg.Location)

	if resourceGroup.Managed {
		resourceGroup.ValidateTags(t, rg.Tags)
	}
}

func (resourceGroup ResourceGroup) VerifyToConfig(t *testing.T, deployment *Deployment, config *ServiceConfig) {
	assert := assert.New(t)

	// Verify the resource group details
	if config.ResourceGroup.Name == "" {
		// Without a resource group specified, a new RG with the config name should be created in the region
		config.ResourceGroup.Name = deployment.Name
	}

	assert.Equal(config.ResourceGroup.Name, resourceGroup.Name)

	// If a location is specified, the resource group should be newly created + managed
	if config.ResourceGroup.Location != "" {
		region := strings.ReplaceAll(strings.ToLower(config.ResourceGroup.Location), " ", "") // clean up region name
		assert.Equal(region, resourceGroup.Location)
		assert.True(resourceGroup.Managed)
	} else {
		assert.False(resourceGroup.Managed)
	}
}
