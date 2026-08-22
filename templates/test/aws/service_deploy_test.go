package aws_test

import (
	"testing"

	"github.com/elastic-machines/monolith/test/aws"
)

func TestAWS(t *testing.T) {
	aws.RunAllTests(t)
}
