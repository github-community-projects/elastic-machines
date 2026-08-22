package azure_test

import (
	"testing"

	"github.com/elastic-machines/monolith/test/azure"
)

func TestAzure(t *testing.T) {
	azure.RunAllTests(t)
}
