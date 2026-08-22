package azure

import (
	"fmt"
	"strings"
	"testing"
	"time"

	"github.com/gruntwork-io/terratest/modules/azure"
	http_helper "github.com/gruntwork-io/terratest/modules/http-helper"
	"github.com/stretchr/testify/assert"
)

type Identity struct {
	Resource
	Type        string
	PrincipalId string `json:"principal_id"`
	Managed     bool   `json:"is_managed"`
}

type AppService struct {
	Resource
	HostName string `json:"host_name"`
	PlanSku  string `json:"plan_sku"`
	Identity *Identity
}

func (appService AppService) VerifyCloudResources(t *testing.T) {
	assert := assert.New(t)
	azure.ResourceGroupExists(t, appService.ResourceGroup, appService.SubscriptionId)
	azure.AppExists(t, appService.Name, appService.ResourceGroup, appService.SubscriptionId)

	site := azure.GetAppService(t, appService.Name, appService.ResourceGroup, appService.SubscriptionId)

	assert.True(*site.HTTPSOnly)
	assert.Equal(appService.Name, *site.Name)
	assert.Equal(appService.HostName, *site.DefaultHostName)
	assert.Equal("Running", *site.State)
	assert.Equal(appService.ResourceGroup, *site.ResourceGroup)
	assert.Equal(appService.Identity.Type, string(site.Identity.Type))

	appService.ValidateTags(t, site.Tags)
}

func (appService AppService) WaitForUpResponse(t *testing.T) {
	// Make an HTTP request to the app service and ensure we get back a 200 OK with a body that includes "Uptime:"
	url := fmt.Sprintf("https://%s", appService.HostName)

	http_helper.HttpGetWithRetryWithCustomValidation(t, url, nil, 15, 15*time.Second, func(status int, body string) bool {
		return status == 200 && strings.Contains(body, "Uptime:")
	})
}

func (appService AppService) VerifyToConfig(t *testing.T, deployment *Deployment, config *ServiceConfig) {
	assert := assert.New(t)

	if config.AppService == nil {
		config.AppService = &AppServiceConfig{}
	}

	// Without an app service name specified, the config name should be used
	if config.AppService.Name == "" {
		assert.Equal(deployment.Name, appService.Name)
		assert.Equal(deployment.Name, appService.ResourceGroup)
		appService.verifyHostName(t, deployment.Name)
	} else {
		// This config specifies a host name
		assert.Equal(config.AppService.Name, appService.Name)
		appService.verifyHostName(t, config.AppService.Name)
	}

	if config.AppService.PlanSku != "" {
		assert.Equal(config.AppService.PlanSku, appService.PlanSku)
	} else {
		assert.Equal("S1", appService.PlanSku)
	}

	// Without an identity specified, the app service should use a system assigned identity
	if config.AppService.Identity == nil {
		assert.Equal("SystemAssigned", appService.Identity.Type)
	} else {
		assert.Equal("UserAssigned", appService.Identity.Type)

		// If Name + ResourceGroup are specified, an existing identity should be used
		assert.False(appService.Identity.Managed)
		assert.Equal(config.AppService.Identity.Name, appService.Identity.Name)
		assert.Equal(config.AppService.Identity.ResourceGroup, appService.Identity.ResourceGroup)
	}
}

func (appService AppService) verifyHostName(t *testing.T, host string) {
	assert.Equal(t, fmt.Sprintf("%s.azurewebsites.net", strings.ToLower(host)), appService.HostName)
}
