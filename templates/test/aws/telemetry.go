package aws

import (
	"testing"

	"github.com/aws/aws-sdk-go/service/cloudwatchlogs"
	"github.com/elastic-machines/monolith/test/shared"
	"github.com/gruntwork-io/terratest/modules/aws"
	"github.com/stretchr/testify/assert"
)

type Telemetry struct {
	Resource
	CloudWatchLogGroup  string `json:"cloudwatch_log_group"`
	MetricsEnabled      bool   `json:"metrics_enabled"`
	MetricsLogStream    string `json:"metrics_log_stream_name"`
	MetricsLogStreamArn string `json:"metrics_log_stream_arn"`
	XRayEnabled         bool   `json:"xray_enabled"`
}

func (telemetry Telemetry) VerifyCloudResources(t *testing.T, env *shared.Environment) {
	assert := assert.New(t)

	client := aws.NewCloudWatchLogsClient(t, telemetry.Region)
	_, err := client.DescribeLogGroups(
		new(cloudwatchlogs.DescribeLogGroupsInput).
			SetLogGroupNamePrefix(telemetry.CloudWatchLogGroup))
	assert.Nil(err) // Just make sure the log group exists

	if telemetry.MetricsEnabled {
		_, err := client.DescribeLogStreams(
			new(cloudwatchlogs.DescribeLogStreamsInput).
				SetLogGroupName(telemetry.CloudWatchLogGroup).
				SetLogStreamNamePrefix(telemetry.MetricsLogStream))
		assert.Nil(err) // Just make sure the log stream exists
		env.ShouldHaveValue("CLOUDWATCH_METRICS_LOG_GROUP", telemetry.CloudWatchLogGroup)
		env.ShouldHaveValue("CLOUDWATCH_METRICS_LOG_STREAM", telemetry.MetricsLogStream)
	} else {
		env.ShouldNotHave("CLOUDWATCH_METRICS_LOG_GROUP", "CLOUDWATCH_METRICS_LOG_STREAM")
	}

	if telemetry.XRayEnabled {
		env.ShouldHaveValue("ENABLE_AWS_XRAY_TELEMETRY", "true")
	} else {
		env.ShouldHaveValue("ENABLE_AWS_XRAY_TELEMETRY", "false")
	}
}

func (telemetry Telemetry) VerifyToConfig(t *testing.T, config *ServiceConfig) {
	assert := assert.New(t)

	if config.CloudWatch != nil {
		assert.Equal(config.CloudWatch.Enabled, telemetry.MetricsEnabled)
	} else {
		assert.True(telemetry.MetricsEnabled)
	}

	if config.XRay != nil {
		assert.Equal(config.XRay.Enabled, telemetry.XRayEnabled)
	} else {
		assert.True(telemetry.XRayEnabled)
	}
}
