package aws

import (
	"fmt"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/aws/aws-sdk-go/service/ec2"
	"github.com/aws/aws-sdk-go/service/ecs"
	"github.com/elastic-machines/monolith/test/shared"
	"github.com/elastic-machines/monolith/test/shared/slices"
	"github.com/gruntwork-io/terratest/modules/aws"
	http_helper "github.com/gruntwork-io/terratest/modules/http-helper"
	"github.com/stretchr/testify/assert"
)

type Service struct {
	Resource
	Cluster       string `json:"cluster"`
	Task          string `json:"task"`
	HostName      string `json:"host_name"`
	VPC           string `json:"vpc_id"`
	Subnets       []string
	SecurityGroup string `json:"security_group"`
}

func (service Service) VerifyCloudResources(t *testing.T) {
	assert := assert.New(t)

	// Evaluate the state of thhe cluster, service, and deployment after startup is complete
	desiredCount, pendingCount, activeCount := int64(1), int64(0), int64(1)

	// Get the service and verify it's in the correct state
	awsService := aws.GetEcsService(t, service.Region, service.Cluster, service.Name)
	assert.Equal(1, len(awsService.Deployments))
	deployment := *awsService.Deployments[0]

	// Poll for the deployment to complete
	assert.Eventually(func() bool {
		awsService = aws.GetEcsService(t, service.Region, service.Cluster, service.Name)
		deployment = *awsService.Deployments[0]
		return *deployment.RolloutState == "COMPLETED"
	}, 5*time.Minute, 10*time.Second)

	assert.Equal("ACTIVE", *awsService.Status)
	assert.Equal("FARGATE", *awsService.LaunchType)
	assert.Equal(desiredCount, *awsService.DesiredCount)
	assert.Equal(pendingCount, *awsService.PendingCount)
	assert.Equal(activeCount, *awsService.RunningCount)

	assert.Equal(desiredCount, *deployment.DesiredCount)
	assert.Equal(pendingCount, *deployment.PendingCount)
	assert.Equal(activeCount, *deployment.RunningCount)
	assert.Equal(int64(0), *deployment.FailedTasks)

	assert.Equal("COMPLETED", *deployment.RolloutState)
	assert.Equal("PRIMARY", *deployment.Status)

	networkConfig := awsService.NetworkConfiguration.AwsvpcConfiguration
	assert.Equal("ENABLED", *networkConfig.AssignPublicIp)
	assert.ElementsMatch(networkConfig.Subnets, slices.AsPointerSlice(service.Subnets))
	assert.Equal(service.SecurityGroup, *networkConfig.SecurityGroups[0])

	// Grab details about the subnet(s)
	ec2Client := aws.NewEc2Client(t, service.Region)
	subnetOutput, err := ec2Client.DescribeSubnets(
		new(ec2.DescribeSubnetsInput).
			SetSubnetIds(slices.AsPointerSlice(service.Subnets)))
	assert.Nil(err, "Error describing subnets")

	for _, subnet := range subnetOutput.Subnets {
		assert.Equal(service.VPC, *subnet.VpcId)
	}

	assert.Equal(1, len(awsService.LoadBalancers))
	alb := *awsService.LoadBalancers[0]
	assert.Equal(int64(80), *alb.ContainerPort)

	awsCluster := aws.GetEcsCluster(t, service.Region, service.Cluster)
	assert.Equal(pendingCount, *awsCluster.PendingTasksCount)
	assert.Equal(activeCount, *awsCluster.ActiveServicesCount)
	assert.Equal(activeCount, *awsCluster.RunningTasksCount)
	assert.Equal("ACTIVE", *awsCluster.Status)
}

func (service Service) VerifyContainer(t *testing.T, container *shared.Container, environment *shared.Environment) {
	assert := assert.New(t)

	taskDefinition := aws.GetEcsTaskDefinition(t, service.Region, service.Task)
	assert.Equal("ACTIVE", *taskDefinition.Status)
	assert.Equal(1, len(taskDefinition.ContainerDefinitions))

	containerDefinition := taskDefinition.ContainerDefinitions[0]
	assert.Equal(filepath.Base(container.Image), *containerDefinition.Name)
	assert.Equal(container.FullImage, *containerDefinition.Image)
	assert.Equal(1, len(containerDefinition.PortMappings))

	service.verifyContainerEnvironment(t, containerDefinition, environment)

	portMapping := *containerDefinition.PortMappings[0]
	assert.Equal(int64(80), *portMapping.ContainerPort)
	assert.Equal(*portMapping.ContainerPort, *portMapping.HostPort)
	assert.Equal("tcp", *portMapping.Protocol)
}

func (service *Service) verifyContainerEnvironment(t *testing.T, containerDefinition *ecs.ContainerDefinition, environment *shared.Environment) {
	assert := assert.New(t)

	// Get the container env in map form
	containerEnv := make(map[string]string, len(containerDefinition.Environment))
	for _, envVar := range containerDefinition.Environment {
		containerEnv[*envVar.Name] = *envVar.Value
	}

	// Verify the environment variables that should and should not exist
	assert.NotContains(containerEnv, environment.Omitted)
	assert.Equal(environment.Values, containerEnv)
}

func (service Service) WaitForUpResponse(t *testing.T) {
	// Make an HTTP request to the service and ensure we get back a 200 OK with a body that includes "Uptime:"
	url := fmt.Sprintf("https://%s", service.HostName)

	http_helper.HttpGetWithRetryWithCustomValidation(t, url, nil, 15, 15*time.Second, func(status int, body string) bool {
		return status == 200 && strings.Contains(body, "Uptime:")
	})
}

func (service Service) VerifyToConfig(t *testing.T, config *ServiceConfig) {
	assert := assert.New(t)

	assert.Equal(config.Region, service.Region)
	assert.Equal(config.Domain.Name, service.HostName)

	if config.Network != nil {
		assert.Equal(config.Network.VPC, service.VPC)
		assert.Equal(config.Network.Subnets, service.Subnets)
	}
}
