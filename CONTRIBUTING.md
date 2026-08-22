# Contributing

This repository isn't currently accepting external contributions. Feel free to [fork](https://github.com/elastic-machines/elastic-machines/fork) or read on to learn how to develop locally.

## Development environment

Elastic Machines is an ASP.NET Core app published as a Docker container. Elastic Machines can be run directly on any machine with the correct version of .NET installed or can be run inside a [devcontainer](https://code.visualstudio.com/docs/remote/containers). Either method will require some additional setup.

### Create a GitHub App

Follow [these steps](README.md#github-app) to create a new GitHub App for local development. For the webhook URL, use a new [smee channel](https://smee.io/new) so that webhook events can be delivered locally.

### Configure settings

Elastic Machines can be configured to use the new GitHub App by setting [certain environment variables](README.md#container-configuration). The easiest way to set these values during development is to create an **ElasticMachines.Monolith/appsettings.Development.json** file.

```json
{
  "APP_ID": "",
  "PRIVATE_KEY": "",
  "WEBHOOK_SECRET": "",
  "GHE_HOST": ""
}
```

> This file can also override any configuration defined in **ElasticMachines.Monolith/appsettings.json** if needed.

### Local webhook deliveries

[Smee](https://smee.io/) is a reverse proxy that can route webhooks from a GitHub App to a development environment. The Smee client runs locally and delivers webhooks to Elastic Machines during development and debugging. Install the `smee-client` npm package with the following command:

```shell
npm install -g smee-client
```

Create a [new Smee channel](https://smee.io/new) and copy the URL into your [GitHub App's](#create-a-github-app) settings. Run `smee` locally to connect with that URL and proxy requests to the local port used by Elastic Machines (5000 by default).

```shell
smee --url https://smee.io/MY_CHANNEL --target http://localhost:5000
```

Now that `smee` is running, any new webhook events will get delivered locally. Past webhook events can also be redelivered from the Smee channel's webpage.

### Using HTTPS

By default, Kestrel doesn't listen for HTTPS traffic in development environments. In order to use HTTPS locally, Kestrel needs to be configured with an HTTPS endpoint to listen on. Add the following `Kestrel` configuration to the **ElasticMachines.Monolith/appsettings.Development.json** file:

```json
{
  "APP_ID": "",
  "PRIVATE_KEY": "",
  "WEBHOOK_SECRET": "",
  "GHE_HOST": "",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      },
      "Https": {
        "Url": "https://localhost:5001"
      }
    }
  }
}
```

Now Kestrel will listen for HTTPS traffic on port 5001 and Elastic Machines will redirect any HTTP traffic to HTTPS. However, Kestrel still needs an SSL certificate to properly handle HTTPS requests. It's possible to use `dotnet`'s built in support for [generating self-signed SSL certificates](https://docs.microsoft.com/en-us/dotnet/core/additional-tools/self-signed-certificates-guide), but `dotnet`'s certificate will not be valid for `smee.io`, which will prevent local webhook deliveries through Smee. Instead, install [mkcert](https://github.com/FiloSottile/mkcert) to simplify creation of a more compatible certificate.

> Although it's possible to install and use `mkcert` within the devcontainer, it's generally more convenient to run `mkcert` and `smee` directly on the host. Devcontainers are routinely rebuilt, which erases `mkcert`'s local certificate authority.

Once installed, use `mkcert` to create a new local certificate authority.

```shell
mkcert -install
```

Next, create a new certificate that is valid for `smee.io` and `localhost`.

```shell
mkcert smee.io localhost 127.0.0.1 ::1
```

This command will create two files (**smee.io+3.pem** and **smee.io+3-key.pem**) in the current directory. Add a `Certificates` configuration to the the `Kestrel` configuration in **ElasticMachines.Monolith/appsettings.Development.json** and specify the paths to the newly created PEM files.

```json
{
  "APP_ID": "",
  "PRIVATE_KEY": "",
  "WEBHOOK_SECRET": "",
  "GHE_HOST": "",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      },
      "Https": {
        "Url": "https://localhost:5001"
      }
    },
    "Certificates": {
      "Default": {
        "Path": "../certificates/smee.io+3.pem",
        "KeyPath": "../certificates/smee.io+3-key.pem"
      }
    }
  }
}
```

Now run `smee` with the following command to trust `mkcert`'s root certificate:

```shell
NODE_EXTRA_CA_CERTS="$(mkcert -CAROOT)/rootCA.pem" smee --url https://smee.io/MY_CHANNEL --target https://localhost:5001
```

## Testing

### Service and Provider Tests (C#)

Comprehensive tests are important to keep Elastic Machines stable. Add tests whenever making changes and make sure all tests pass before submitting a PR. Use the following command to run the entire test suite:

```shell
dotnet test
```

However, consider only running tests specific to your change since some tests (particularly the provider tests) err more towards integration tests and may take a long time to run. For example, the Azure tests require an Azure subscription and create resources there. Run tests for a single project by specifying the folder name as an argument to `dotnet test`. e.g.

```shell
dotnet test ElasticMachines.Engine.Tests
```

#### Test settings

Providers typically require extra settings and authentication to run. Create or edit an `appsettings.Test.json` file in the `ElasticMachines.Providers.Tests.Core` project with details of the GitHub app and repo used for testing, as well as any other provider specific settings:

```jsonc
{
  "APP_ID": "",
  "PRIVATE_KEY": "",
  "ACCOUNT": "", // The account used to add/remove runner for testing
  "REPOSITORY": "", // The repo that will be used to add/remove runners for testing
  "azure-virtual-machine": {
    "subscription": "", // Optionally override the authenticated account's default subscription
    "location": "" // The region to create resources in (e.g. westus3)
  },
  "aws-instance": {
    "region": "", // The region to create resources in (e.g. us-west-2)
    "imageId": "", // The AMI to use for EC2 instances
    "iamInstanceProfile": {
      "arn": "" // The instance profile ARN to use for EC2 instances
    },
    "subnetId": "", // The subnet to use for EC2 instances
    "testMacos": "false" // Set to "true" to run tests on macOS instances
  },
  "kubernetes-pod": {
    "context": "", // Optionally override the default context
  }
}
```

The GitHub App ID/private key can be configured in the same way as described in [Create a GitHub App](#create-a-github-app) (and can likely be reused). The repo details required are for a repo that the App is installed in.

##### Azure authentication

Ensure the [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) is authenticated against a subscription where the tests can create resources.

```shell
az login
az account set --subscription "<account_name>"
```

##### Kubernetes authentication

Ensure there is a Kubernetes cluster available where the tests can create resources. Set up a local cluster with [Docker](https://docs.docker.com/desktop/kubernetes/#enable-kubernetes), [minikube](https://minikube.sigs.k8s.io/docs/start/), etc. or authenticate against a remote cluster.

Use the following commands to authenticate against an Azure Kubernetes Service (AKS) cluster:

```shell
az aks install-cli # If not installed
az aks get-credentials --resource-group <resource_group> --name <elastic_machines_cluster>
```

> These commands assume the Azure CLI is [set up](#azure-authentication) with access to the desired AKS cluster.

Use the following commands to authenticate against an Elastic Kubernetes Service (EKS) cluster:

```shell
aws configure
aws eks update-kubeconfig --region "<region-code>" --name "<eks-cluster>"
```

> These commands assume the AWS CLI is [installed](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) with access to the desired EKS cluster.

##### Adding a new container image test

Within the `KubernetesProviderTests` class:

* Add the container image to test as an `InlineData` value on the test method in `KubernetesProviderTests`, e.g. `[InlineData("ubuntu:16.04")]`.
  * All image:tag combinations found on [Docker Hub](https://hub.docker.com/) should be pullable by AKS.
* Run or debug the Linux or Windows test method, depending on the image being used.
  * It's often useful to comment out other `InlineData` images temporarily to focus on the image being added.

Run or debug the test:

* If the test completes successfully, the image:tag can be added to the supported image list.
* if the test fails, it can be debugged by following these steps:
  * Add a breakpoint to the `DisposeAsync()` method to prevent the test namespace from being deleted in the `KubernetesProviderFixture` test fixture.
  * Start the test in 'Debug' mode. (e.g. in VSCode, use the method hint above the test method to Debug the test).
  * Capture the test namespace logged to the console when the test begins.
  * Using the test namespace, use `kubectl get pods --namespace=<test_namespace_from_console>` to view details of the Pod that's created for the test.
  * This will allow inspection of the created Pod, reading of the Pod logs, etc., and allow debugging the reason for test failure.
  * Once the test hits the breakpoint in `DisposeAsync()` and debugging is finished, make sure to continue execution so the test namespace is properly cleaned up.

If an error is found using the above steps, the typical resolution process includes adjusting the container startup script to account for a difference/deficiency within the container image. This is often missing packages, a different/unsupported package manager, or other similar issues. Repeat the above steps in an iterative manner to achieve a passing test for the container image under test.

### Service Deployment Tests (Go)

The service deployment tests are written in Go and are located in `/private/templates/test`. These tests should be updated to incorporate any new features or settings that are added to the service deployment templates.

The tests can be run and debugged locally within VS Code, or run via the command line. Tests for a specific platform can be run by navigating into the `/<platform>` directory (e.g. `/aws`) and executing the following command:

```shell
go test -v -timeout 30m
```

The entire test suite may be run by navigating to the parent `/test` directory and executing the following command:

```shell
go test -v -timeout 30m ./...
```

Note that when running tests across packages as in the second command above, the test output will be buffered until the test run is complete.

#### Environment

The tests require the following environment variables to be set:

```shell
CONTAINER_REGISTRY_TOKEN: <token>
PRIVATE_KEY: <key contents>
WEBHOOK_SECRET: <secret>
```

These values can be obtained from the GitHub App created in the [Create a GitHub App](#create-a-github-app) section and placed in a file named `.env` within the specific platform directory (e.g. `/aws`).

#### Adding tests

New tests can be added to the test suite by created a service deployment `.yml` file and placing it in the `/testdata` directory under the platform directory (e.g. `/aws/testdata`). The test suite will automatically pick up any new files in this directory and run them as part of the test suite.

If a new feature is being tested, the logic within the test suite may need to be updated to account for the new feature. For example, if a new setting is added to the service deployment template, the test suite will need to be updated to account for the new setting.

This is done by updating the appropriate `<area>.go` file to test that the new feature is configured as expected once deployed. This may also require an update to the deployment template outputs and the schema within the `.go` file. Additionally, the config schema will also need to be update in `/<platform>/service_config.go`.

#### Debugging tests

To execute and debug a single test, a new function can be temporarily used to apply a single configuration.

Example:

```Go
func TestDebug(t *testing.T) {
  azure.RunTest(t, "ExistingResources")
}
```

This function runs the test loop for `/testdata/ExistingResources.yml` and will add the ability to run or debug the test via the IDE integration.

The test loop can be inconveniently long when working with a single test and iterating on the verification step(s). To speed up the process, the test can be left 'staged' in the filesystem and cloud by using the following environment variable flags (in the `.env` file)

```shell
# Set this to leave a test staged and all files and cloud resources in place
SKIP_cleanup: true
# Once a test is staged and not cleaned up, the following flags can be set to skip re-deploying files + resources
SKIP_stage: true
SKIP_apply: true
```

Once test debugging is complete, be sure to remove/un-set the `SKIP_cleanup` flag to ensure the test is cleaned up properly and all cloud resources are destroyed.

## Linting

Linters are run on every PR, but can be run locally to get early feedback.

### C# code

The [dotnet format](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-format) tool is used to verify C# code formatting.

```shell
dotnet format --verify-no-changes
```

### Markdown

If developing in VS Code, use the [markdownlint extension](vscode:extension/davidanson.vscode-markdownlint) to get immediate, in-editor feedback while editing markdown files.

Alternatively use `markdownlint-cli`, which has the added benefit of supporting custom rules. If not installed, install `markdownlint-cli` and the `markdownlint-rule-github-internal-links` custom rule with with npm.

```shell
npm install -g markdownlint-cli markdownlint-rule-github-internal-links
```

Then lint all files in the repository.

```shell
markdownlint "**/*.md" --rules markdownlint-rule-github-internal-links
```

## Kubernetes

[kubectl](https://kubernetes.io/docs/reference/kubectl/) is an essential tool for managing local and remote Kubernetes clusters. [Install kubectl](https://kubernetes.io/docs/tasks/tools/) to make authentication and debugging easier. Refer to the [kubectl commands documentation](https://kubernetes.io/docs/reference/generated/kubectl/kubectl-commands) for more information on its capabilities.

### Minikube

[minikube](https://minikube.sigs.k8s.io/docs/) is the recommended way to run Kubernetes for development. It will not be affected by network instability and can easily be reset without worrying about deleting resources from a shared Kubernetes instance running in the cloud. [Install minikube](https://minikube.sigs.k8s.io/docs/start/) and run `minikube start` to start a new cluster and automatically add it to your kubeconfig.

### Azure Kubernetes Service (AKS)

To test AKS, ensure the [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) is installed and authenticated. Run `az aks install-cli` to finish installing required dependencies.

If there is not already an AKS cluster available, create a new cluster through the Azure portal or use the following command:

```sh
az aks create --resource-group "<resource-group>" --name "<name>" --node-count "<node-count>"
```

Add an AKS cluster's authentication details to your local kubeconfig by running the following command:

```sh
az aks get-credentials --resource-group "<resource-group>" --name "<name>"
```

### Elastic Kubernetes Service (EKS)

To test EKS, ensure the [AWS CLI](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html) is installed and authenticated.

If there is not already an EKS cluster available, create a new cluster through the AWS portal. Kubernetes has it's own internal authentication mechanism so the user that creates the cluster is the only user that will initially have full access to resources within the cluster. Creating the cluster through the AWS portal is the easiest way to ensure you will be able to view and manage the cluster through the portal after it's created.

Once the cluster is online, you need to give other users access to it. Even the CLI user you use locally may not match the identity used in the portal so may not initially have access to resources within the cluster. Instead of manually giving everybody access to the cluster, create an EKS admin role that users can assume when accessing the cluster. Create a role named `eks-admin` with a trust relationship that allows access to any entity in the account (replace `<account-id>` with the ID of the desired account):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "AWS": "arn:aws:iam:<account-id>:root"
      },
      "Action": "sts:AssumeRole",
      "Condition": {}
    }
  ]
}
```

Attach a new policy with EKS full access:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "eks:*",
      "Resource": "*"
    }
  ]
}
```

Now the `eks-admin` role has access to EKS, but it still doesn't have access to internal Kubernetes resources. Open CloudShell in the AWS portal so that you can run commands as the original user that created the cluster (which could be different than your local CLI user). The easiest way to update permissions within Kubernetes is to use [eksctl](https://docs.aws.amazon.com/eks/latest/userguide/eksctl.html). Install it and add the `eks-admin` user with the following commands:

```sh
eksctl create iamidentitymapping --cluster  "<cluster-name>" --arn "arn:aws:iam::<account-id>:role/eks-admin" --group system:masters --username eks-admin
```

Now switch to your local machine. Ensure your CLI user has permission to assume the `eks-admin` role and run the following command to add the EKS cluster's authentication details to your local kubeconfig (your CLI user may temporarily need the `eks:DescribeCluster` permission for this command):

```sh
aws eks update-kubeconfig --name "<cluster-name>" --role-arn "arn:aws:iam::<account-id>:role/eks-admin" --alias "<cluster-name>"
```

You now have full access to the Kubernetes cluster locally. `kubectl` and other tools will automatically assume the `eks-admin` role before connecting to the cluster so that you get full access to internal resources. Your AWS portal identity also retains full access to the cluster because it created it in the fist place.

## Codespaces Setup

The following section outlines the steps required to setup a Codespace for development.

* Open the project in GitHub Codespaces and execute the following commands from the integrated terminal
* Authenticate to Azure using `az login --use-device-code` (--use-device-code is required when using Codespaces)
* Authenticate to AWS using `aws configure`
* Copy local `appsettings.Development.json` file into `ElasticMachines.Monolith` in Codespaces
* Copy local `certificates` directory into the root folder in Codespaces
* Copy local `~/.kube/config` file into `/home/vscode/.kube/config` in Codespaces (**not the root of the project**)
  * `mkdir -p /home/vscode/.kube`
  * `touch /home/vscode/.kube/config`
  * On Local Machine: `kubectl config view --raw | pbcopy`
  * On Codespace: `cd /home/vscode/.kube/config` and Paste the contents of your local kube config
* Debug the project in Codespaces `F5`
