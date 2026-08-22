package azure

import (
	"fmt"
	"path/filepath"
	"testing"

	"github.com/gruntwork-io/terratest/modules/azure"
	"github.com/stretchr/testify/assert"
)

type AppInsights struct {
	Resource
	Enabled     bool
	Managed     bool   `json:"is_managed"`
	WorkspaceId string `json:"workspace_id"`
}

func (appInsights AppInsights) GetWorkspaceName() string {
	// If we've deployed the workspace, look for it with the expected name; otherwise, parse the ID
	if appInsights.Managed {
		return fmt.Sprintf("%s-workspace", appInsights.DeploymentName)
	} else {
		return filepath.Base(appInsights.WorkspaceId)
	}
}

func (appInsights AppInsights) VerifyCloudResources(t *testing.T) {
	assert := assert.New(t)

	if appInsights.Enabled {
		workspaceName := appInsights.GetWorkspaceName()
		azure.LogAnalyticsWorkspaceExists(t, workspaceName, appInsights.ResourceGroup, appInsights.SubscriptionId)

		if appInsights.Managed {
			azure.LogAnalyticsWorkspaceExists(t, workspaceName, appInsights.ResourceGroup, appInsights.SubscriptionId)
			workspace := azure.GetLogAnalyticsWorkspace(t, workspaceName, appInsights.ResourceGroup, appInsights.SubscriptionId)
			// Ensure we've deployed Insights with the expected name & details
			assert.Equal(fmt.Sprintf("%s-app-insights", appInsights.DeploymentName), appInsights.Name)
			assert.Equal("Succeeded", string(workspace.ProvisioningState))
			appInsights.ValidateTags(t, workspace.Tags)
		}
	}
}

func (appInsights AppInsights) VerifyToConfig(t *testing.T, deployment *Deployment, config *ServiceConfig) {
	assert := assert.New(t)

	if config.AppInsights != nil {
		enabled := config.AppInsights.Enabled == nil || *config.AppInsights.Enabled
		assert.Equal(enabled, appInsights.Enabled)

		// Existing App Insights and Key Vault resources should be used if name+resource group are specified
		if config.AppInsights.Name != "" && config.AppInsights.ResourceGroup != "" {
			assert.False(appInsights.Managed)
			assert.Equal(config.AppInsights.Name, appInsights.Name)
			assert.Equal(config.AppInsights.ResourceGroup, appInsights.ResourceGroup)
		} else if enabled {
			assert.True(appInsights.Managed)
		}
	} else {
		// With no additional config, App Insights should be enabled
		assert.True(appInsights.Enabled)
		assert.True(appInsights.Managed)
	}
}
