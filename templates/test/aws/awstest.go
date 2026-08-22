package aws

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
	deployment.Telemetry = ResourceFromOutput[Telemetry](test, *deployment, "telemetry")
	deployment.Service = ResourceFromOutput[Service](test, *deployment, "service")
	deployment.ALB = ResourceFromOutput[ALB](test, *deployment, "alb")

	return deployment
}

func (runner TestRunner) VerifyDeploymentToConfig(test terraform.Test[ServiceConfig], deployment *Deployment, config *ServiceConfig) {
	assert := assert.New(test.T)

	// Verify all config settings possible using the deserialized config + output values
	deployment.TestEnvironment = shared.VerifyDeploymentToConfig(test, deployment, &config.ServiceConfig)
	deployment.Telemetry.VerifyToConfig(test.T, config)
	deployment.Service.VerifyToConfig(test.T, config)
	deployment.ALB.VerifyToConfig(test.T, config)

	// Verify any additional high-level AWS config
	assert.Equal(config.Region, deployment.Region)
}

func (runner TestRunner) VerifyDeployment(test terraform.Test[ServiceConfig], deployment *Deployment) {
	// Ensure the deployment name was prefixed with the test namespace
	assert.True(test.T, strings.HasPrefix(deployment.Name, test.Namespace))

	// Verify that the values of the output variables have the values we expect
	deployment.Telemetry.VerifyCloudResources(test.T, deployment.TestEnvironment)
	deployment.Service.VerifyCloudResources(test.T)
	deployment.Service.VerifyContainer(test.T, deployment.Container, deployment.TestEnvironment)
	deployment.ALB.VerifyCloudResources(test.T)
}
