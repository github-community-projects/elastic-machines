package shared

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/elastic-machines/monolith/test/shared/assertions"
	"github.com/elastic-machines/monolith/test/shared/terraform"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

type Deployment struct {
	Name            string     `json:"config_name"`
	Container       *Container `json:"container"`
	TestEnvironment *Environment
	Tags            map[string]string `json:"tags"`
}

type CloudDeployment interface {
	GetBaseDeploymentDetails() Deployment
	FormatSecret(secret string) string
}

type Container struct {
	Registry    string
	Image       string
	Version     string
	FullImage   string `json:"full_image"`
	Environment map[string]any
	Secrets     []string
}

type AugmentedResource[T any, TDeploy any] interface {
	*T
	Augment(deployment TDeploy)
}

func VerifyDeploymentToConfig[TConfig any, CD CloudDeployment](test terraform.Test[TConfig], deployment CD, config *ServiceConfig) *Environment {
	assert := assert.New(test.T)
	deploy := deployment.GetBaseDeploymentDetails()

	// Ensure the deployment name was prefixed with the test namespace + ends with the config name
	assert.True(strings.HasPrefix(deploy.Name, test.Namespace))
	assert.True(strings.HasSuffix(deploy.Name, config.Name))

	assert.Equal(config.Container.Image, deploy.Container.Image)

	if config.Container.Version == "" {
		config.Container.Version = "latest"
	}

	assert.Equal(config.Container.Version, deploy.Container.Version)
	assert.Equal(config.Tags, deploy.Tags)

	environment := NewEnvironment()

	// Check that all env vars from the config are present in the deployment and match
	for key, val := range config.Container.Environment {
		assert.Contains(deploy.Container.Environment, key)

		switch valType := val.(type) {
		case map[string]any:
			value, ok := valType["secret"]

			if ok {
				environment.ShouldNotHave(key)
				assert.Equal(deployment.FormatSecret(value.(string)), deploy.Container.Environment[key])
				continue
			}

			// Anything other than a secret will be in env
			environment.ShouldHaveValue(key, deploy.Container.Environment[key])

			value, ok = valType["file"]

			if ok {
				contents, err := os.ReadFile(filepath.Join(test.Options.TerraformDir, value.(string)))
				require.NoError(test.T, err)
				assert.Equal(strings.TrimSpace(string(contents)), deploy.Container.Environment[key])
				continue
			}

			assertions.StringEquals(test.T, valType["value"], deploy.Container.Environment[key])
		default:
			environment.ShouldHaveValue(key, deploy.Container.Environment[key])
			assertions.StringEquals(test.T, val, deploy.Container.Environment[key])
		}
	}

	return environment
}
