package assertions

import (
	"fmt"
	"strconv"
	"testing"

	"github.com/stretchr/testify/assert"
)

// StringEquals asserts that two values are equal once they're been converted to strings
func StringEquals(t *testing.T, expected any, actual any) {
	// Convert floats to strings without using scientific notation (used by default for large numbers)
	if float, ok := actual.(float64); ok {
		actual = strconv.FormatFloat(float, 'f', -1, 64)
	}

	assert.Equal(t, fmt.Sprint(expected), fmt.Sprint(actual))
}
