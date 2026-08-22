package aws

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

type Resource struct {
	Name           string
	ARN            string
	Region         string
	DeploymentName string
	Tags           map[string]string
}

func (resource *Resource) Augment(deployment Deployment) {
	resource.DeploymentName = deployment.Name

	if resource.Region == "" {
		resource.Region = deployment.Region
	}

	if resource.Tags == nil {
		resource.Tags = deployment.Tags
	}
}

func (resource *Resource) ValidateTags(t *testing.T, tags map[string]*string) {
	for k, v := range tags {
		assert.Equal(t, resource.Tags[k], *v)
	}
}
