package terraform

import (
	"encoding/json"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"

	"github.com/gruntwork-io/terratest/modules/files"
	"github.com/gruntwork-io/terratest/modules/logger"
	"github.com/gruntwork-io/terratest/modules/random"
	"github.com/gruntwork-io/terratest/modules/terraform"
	test_structure "github.com/gruntwork-io/terratest/modules/test-structure"
	"github.com/joho/godotenv"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"github.com/tidwall/gjson"
	"gopkg.in/yaml.v3"
)

type Test[TConfig any] struct {
	T          *testing.T
	Options    *terraform.Options
	Namespace  string
	Platform   string
	Config     *TConfig
	Folder     string
	configFile string
	output     string
}

type TestRunner[TConfig, TDeploy any] interface {
	BuildDeployment(test Test[TConfig]) *TDeploy
	VerifyDeployment(test Test[TConfig], deployment *TDeploy)
	VerifyDeploymentToConfig(test Test[TConfig], deployment *TDeploy, config *TConfig)
}

type TestRunnerPt[TRunner TestRunner[TConfig, TDeploy], TConfig, TDeploy any] interface {
	*TRunner
	BuildDeployment(test Test[TConfig]) *TDeploy
	VerifyDeployment(test Test[TConfig], deployment *TDeploy)
	VerifyDeploymentToConfig(test Test[TConfig], deployment *TDeploy, config *TConfig)
}

type TestDetails struct {
	Namespace      string
	PlatformFolder string
	ConfigFile     string
}

func init() {
	if files.FileExists(".env") {
		// Load the .env file from the platform test folder
		if err := godotenv.Load(); err != nil {
			log.Fatal("Error loading .env file")
		}
	}
}

func TestWithDetails[TConfig any](t *testing.T, testDetails *TestDetails) Test[TConfig] {
	tempFolder, _ := filepath.Abs(filepath.Join(testDetails.PlatformFolder, "..", ".."))
	env := map[string]string{"NAMESPACE": testDetails.Namespace}

	return Test[TConfig]{
		T: t,
		// Construct the terraform options with default retryable errors to handle the most common retryable errors in terraform testing
		Options: terraform.WithDefaultRetryableErrors(t, &terraform.Options{
			TerraformDir: testDetails.PlatformFolder,
			EnvVars:      env,
		}),
		Namespace:  testDetails.Namespace,
		Platform:   filepath.Base(testDetails.PlatformFolder),
		Config:     readServiceConfig[TConfig](t, testDetails.ConfigFile, env),
		Folder:     tempFolder,
		configFile: testDetails.ConfigFile,
	}
}

// Creates and stages a new Terraform test - Creates a namespace and copies the platform Terraform folder + <TEST_NAME>.yml to a temp dir
func stageTest(t *testing.T, platform string) *TestDetails {
	assert := assert.New(t)

	// Generate a unique namespace so we don't clash with other tests running in parallel
	namespace := fmt.Sprintf("em%s", random.UniqueId())

	// Figure out the path to the templates folder
	wd, err := os.Getwd()
	assert.Nil(err, "Error getting current directory")
	path, err := filepath.Abs(filepath.Join(wd, "../../../templates"))
	assert.Nil(err, "Error determining template directory")

	// Copy the terraform folder to a temp dir so we can parallelize tests
	tmpFolder, err := files.CopyTerraformFolderToTemp(path, namespace+"-")
	assert.Nil(err, "Error while creating temp dir")

	// Create the test struct
	platformFolder := filepath.Join(tmpFolder, platform)
	testdataFolder, _ := filepath.Abs(fmt.Sprintf("../%s/testdata", platform))
	configFile, _ := filepath.Abs(filepath.Join(testdataFolder, fmt.Sprintf("%s.yml", filepath.Base(t.Name()))))

	// Copy the testdata config file to the temp dir in the appropriate platform folder
	destConfig := fmt.Sprintf("%s/config.yml", platformFolder)
	err = files.CopyFile(configFile, destConfig)
	assert.Nil(err, "Error copying config file")
	err = files.CopyFolderContents(filepath.Join(testdataFolder, "external"), platformFolder)
	assert.Nil(err, "Error copying external testdata")

	return &TestDetails{namespace, platformFolder, configFile}
}

func getTestDetailsFile(testName string) string {
	wd, _ := os.Getwd()
	return filepath.Join(wd, fmt.Sprintf("_details_%s.json", testName))
}

func RunAllTests[TRunner TestRunner[TConfig, TDeploy], PT TestRunnerPt[TRunner, TConfig, TDeploy], TConfig, TDeploy any](t *testing.T) {
	t.Parallel()
	testFiles, err := os.ReadDir("./testdata")
	assert.Nil(t, err)

	for _, testFile := range testFiles {
		ext := filepath.Ext(testFile.Name())
		if ext != ".yml" {
			continue
		}

		testName := strings.TrimSuffix(testFile.Name(), ext)
		RunTest[TRunner, PT, TConfig, TDeploy](t, testName)
	}
}

func RunTest[TRunner TestRunner[TConfig, TDeploy], PT TestRunnerPt[TRunner, TConfig, TDeploy], TConfig, TDeploy any](t *testing.T, testName string) {
	t.Run(testName, func(t *testing.T) {
		t.Parallel()
		wd, _ := os.Getwd()
		platform := filepath.Base(wd)
		testDetailsFile := getTestDetailsFile(testName)
		var testDetails *TestDetails

		// Stage the test - copy all the files to a temp dir and create a namespace
		test_structure.RunTestStage(t, "stage", func() {
			require.False(t, files.FileExists(testDetailsFile), "Test details file already exists for %s", testName)
			testDetails = stageTest(t, platform)
			test_structure.SaveTestData(t, testDetailsFile, false, testDetails)
		})

		// If we've skipped test staging above, we'll need to look for a stashed details file
		if testDetails == nil {
			test_structure.LoadTestData(t, testDetailsFile, &testDetails)
		}

		// Create the test struct
		test := TestWithDetails[TConfig](t, testDetails)
		t.Cleanup(test.Cleanup)

		// Apply the config in the cloud via Terraform
		test_structure.RunTestStage(t, "apply", func() {
			test.InitAndApply()
		})

		// Verify all facets of the resulting deployment
		test_structure.RunTestStage(t, "verify", func() {
			test.CacheOutputs()
			// Create the TestRunner that will be used to verify the deployment
			runner := PT(new(TRunner))
			// Build the deployment + resources from the Terraform output values
			deployment := runner.BuildDeployment(test)
			// Verify deployment details match the config file
			runner.VerifyDeploymentToConfig(test, deployment, test.Config)
			// Verify deployment details match the resources in the cloud
			runner.VerifyDeployment(test, deployment)
		})
	})
}

// Runs `terraform init` + `terraform apply`
func (test Test[TConfig]) InitAndApply() {
	terraform.InitAndApply(test.T, test.Options)
}

func (test *Test[TConfig]) CacheOutputs() {
	// Temp discard logger so we don't leak sensitive outputs
	defer func() {
		test.Options.Logger = nil
	}()
	test.Options.Logger = logger.Discard

	// Run `terraform output` to get the value of the `service` output that contains all sub-outputs
	test.output = terraform.OutputJson(test.T, test.Options, "service")
}

// Run `terraform destroy`
func (test Test[TConfig]) Destroy() string {
	return terraform.Destroy(test.T, test.Options)
}

// Cleanup runs `terraform destroy` and deletes the temporary folder created for this test
func (test Test[TConfig]) Cleanup() {
	test_structure.RunTestStage(test.T, "cleanup", func() {
		test.Destroy()
		if test.Folder != "" {
			os.RemoveAll(test.Folder)
		}

		testName := filepath.Base(test.T.Name())
		test_structure.CleanupTestData(test.T, getTestDetailsFile(testName))
	})
}

// Get the value of a single output variable and marshall it to the given type
func ObjectFromOutput[TOutput any, TConfig any](test Test[TConfig], outputName string) *TOutput {
	resource := new(TOutput)
	result := gjson.Get(test.output, outputName)
	err := json.Unmarshal([]byte(result.Raw), resource)
	require.NoError(test.T, err)

	return resource
}

// Read the config yaml file into a config struct
func readServiceConfig[TServiceConfig any](t *testing.T, configFile string, testEnv map[string]string) *TServiceConfig {
	assert := assert.New(t)
	buf, err := os.ReadFile(configFile)
	assert.Nil(err, "Error reading config file")

	regex, err := regexp.Compile(`\${ *([^}]*?) *}`)
	assert.Nil(err, "Error compiling regex")

	processed := regex.ReplaceAllStringFunc(string(buf), func(match string) string {
		match = strings.Trim(match, "${ }")

		if strings.HasPrefix(match, "env.") {
			envVar := strings.TrimPrefix(match, "env.")
			val, exists := os.LookupEnv(envVar)
			if exists {
				return val
			}

			return testEnv[envVar] // Fall back to the test env, which is set per test
		}

		return match
	})

	config := new(TServiceConfig)
	err = yaml.Unmarshal([]byte(processed), config)
	assert.Nil(err, "Error deserializing config file")

	return config
}
