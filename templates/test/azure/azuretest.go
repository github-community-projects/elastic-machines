package azure

import (
	"strings"
	"testing"

	"github.com/elastic-machines/monolith/test/shared"
	"github.com/elastic-machines/monolith/test/shared/terraform"
	"github.com/stretchr/testify/assert"
)

type TestRunner struct{}

func RunAllTests(t *testing.T) {
	terraform.RunAllTests[TestRunner](t)
}

func RunTest(t *testing.T, testName string) {
	terraform.RunTest[TestRunner](t, testName)
}

func ResourceFromOutput[T any, PT shared.AugmentedResource[T, Deployment]](test terraform.Test[ServiceConfig], deployment Deployment, outputName string) *T {
	resource := terraform.ObjectFromOutput[T](test, outputName)
	PT(resource).Augment(deployment)

	return resource
}

func (runner TestRunner) BuildDeployment(test terraform.Test[ServiceConfig]) *Deployment {
	// Build the deployment + resources from the Terraform output values
	deployment := terraform.ObjectFromOutput[Deployment](test, "deployment")
	deployment.ResourceGroup = ResourceFromOutput[ResourceGroup](test, *deployment, "resource_group")
	deployment.AppService = ResourceFromOutput[AppService](test, *deployment, "app_service")
	deployment.AppInsights = ResourceFromOutput[AppInsights](test, *deployment, "app_insights")

	return deployment
}

func (runner TestRunner) VerifyDeploymentToConfig(test terraform.Test[ServiceConfig], deployment *Deployment, config *ServiceConfig) {
	assert := assert.New(test.T)

	// Verify all config settings possible using the deserialized config + output values
	deployment.TestEnvironment = shared.VerifyDeploymentToConfig(test, deployment, &config.ServiceConfig)
	deployment.ResourceGroup.VerifyToConfig(test.T, deployment, config)
	deployment.AppService.VerifyToConfig(test.T, deployment, config)
	deployment.AppInsights.VerifyToConfig(test.T, deployment, config)

	// Verify any additional high-level Azure config
	assert.Equal(config.SubscriptionID, deployment.SubscriptionId)
}

func (runner TestRunner) VerifyDeployment(test terraform.Test[ServiceConfig], deployment *Deployment) {
	// Ensure the deployment name was prefixed with the test namespace
	assert.True(test.T, strings.HasPrefix(deployment.Name, test.Namespace))

	// Verify that the values of the output variables have the values we expect
	deployment.ResourceGroup.VerifyCloudResources(test.T)
	deployment.AppService.VerifyCloudResources(test.T)
	deployment.AppInsights.VerifyCloudResources(test.T)

	// Verify the service comes up
	deployment.AppService.WaitForUpResponse(test.T)
}
