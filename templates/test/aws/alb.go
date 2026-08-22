package aws

import (
	"testing"

	"github.com/aws/aws-sdk-go/service/elbv2"
	"github.com/elastic-machines/monolith/test/shared/slices"
	"github.com/gruntwork-io/terratest/modules/aws"
	"github.com/stretchr/testify/assert"
)

type ALB struct {
	Resource
	VPC            string   `json:"vpc_id"`
	Subnets        []string `json:"subnets"`
	SecurityGroups []string `json:"security_groups"`
}

func (alb ALB) VerifyCloudResources(t *testing.T) {
	assert := assert.New(t)
	session, err := aws.NewAuthenticatedSession(alb.Region)
	assert.Nil(err, "Error getting session")

	elbClient := elbv2.New(session)
	albResponse, err := elbClient.DescribeLoadBalancers(
		new(elbv2.DescribeLoadBalancersInput).
			SetLoadBalancerArns([]*string{&alb.ARN}))
	assert.Nil(err, "Error describing ALB")

	targetResponse, err := elbClient.DescribeTargetGroups(
		new(elbv2.DescribeTargetGroupsInput).
			SetLoadBalancerArn(alb.ARN))
	assert.Nil(err, "Error describing target group")

	targetGroup := targetResponse.TargetGroups[0]
	assert.Equal(alb.VPC, *targetGroup.VpcId)

	awsALB := albResponse.LoadBalancers[0]
	assert.Equal(alb.VPC, *awsALB.VpcId)
	// Verify the security groups match
	assert.ElementsMatch(awsALB.SecurityGroups, slices.AsPointerSlice(alb.SecurityGroups))

	albSubnets := make([]string, len(awsALB.AvailabilityZones))
	for i, az := range awsALB.AvailabilityZones {
		albSubnets[i] = *az.SubnetId
	}
	// Verify the subnets match
	assert.ElementsMatch(albSubnets, alb.Subnets)
}

func (alb ALB) VerifyToConfig(t *testing.T, config *ServiceConfig) {
	assert := assert.New(t)

	if config.Network != nil {
		assert.Equal(config.Network.VPC, alb.VPC)
		assert.ElementsMatch(config.Network.ALBSubnets, alb.Subnets)
		assert.ElementsMatch(config.Network.ALBSecurityGroups, alb.SecurityGroups)
	}
}
