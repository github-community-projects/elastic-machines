package aws

import (
	"fmt"
	"strings"

	"github.com/elastic-machines/monolith/test/shared"
)

type Deployment struct {
	shared.Deployment
	Region    string
	Telemetry *Telemetry
	Service   *Service
	ALB       *ALB
}

func (deployment Deployment) GetBaseDeploymentDetails() shared.Deployment {
	return deployment.Deployment
}

func (deployment Deployment) FormatSecret(secret string) string {
	parts := strings.Count(secret, ":")
	return fmt.Sprintf("%s%s", secret, strings.Repeat(":", 9-parts))
}
