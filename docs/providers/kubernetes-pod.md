# Kubernetes Pod

This provider can be used to scale runners via pods in a Kubernetes cluster. All pods will use the [PodTemplateSpec](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.26/#podtemplatespec-v1-core) configured in the provider, as described below.

## YAML Configuration

To use this provider, specify `kubernetes-pod@v0` as the `provider`

```yaml
- name: <name>
  provider: kubernetes-pod@v0
  with:
    # The context of the Kubernetes cluster to use
    # This property is required in order to use a context other than the `current-context` specified in the kubeconfig
    context: <context_name> # Default: The current context set in the kubeconfig
    # A PodTemplateSpec object defining the pod to create
    # A commonly used subset of the available properties are shown below
    # For a full list, see: https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.26/#podtemplatespec-v1-core
    template:
      metadata:
        # The Kubernetes namespace to create Elastic Machines resources in
        # This namespace will be created in the cluster if it does not already exist
        namespace: <namespace> # Default: default
      spec:
        # One or more containers to run in the pod
        containers:
          - # A unique name for each container in the pod
            # The container containing the runner executable must be named 'runner' (or left blank)
            name: runner # Default: runner
            # The image to create the container from
            # Use the format 'image:tag' for images hosted in Docker Hub or 'registry/image:tag' for other registries
            image: ghcr.io/actions/actions-runner:latest
            # An optional command to override the image's default entrypoint
            # This property is required if the image's entrypoint doesn't already launch the runner executable
            command: [./run.sh] # Default: null
        # The name of one or more Kubernetes secret(s) that contain a 'docker-registry' secret value with credentials to pull the container image(s) used above
        # See the "Container Image Registry Authentication" section below for more information
        imagePullSecrets:
          - name: <name>
        # Labels that nodes must have to accept pods created from this configuration
        # The kubernetes.io/os and kubernetes.io/arch selectors are automatically populated from the 'runner' container image's manifest, but can be overridden here
        nodeSelector:
          <key>: <value>

# Additional provider blocks as needed
- provider: kubernetes-pod@v0
  with:
    # ...
```

## Authentication

Authentication is handled via a standard Kubernetes kubeconfig. Set the `KUBECONFIG_CONTENTS` environment variable with the contents of a kubeconfig file that contains authentication information for the desired cluster(s). Refer to the example [service account definition](./service-account.yml) for a list of required permissions.

The kubeconfig can optionally contain multiple contexts if Elastic Machines needs to manage resources in multiple Kubernetes clusters. Set the `context` property in each provider configuration to choose which context to use (Elastic Machines uses the kubeconfig's `current-context` by default).

Elastic Machines does not support user credentials that shell out to external executables. Consider creating a service account in the Kubernetes cluster and authenticate with a token instead. For example, Azure Kubernetes Service (AKS) creates a compatible kubeconfig with [`az aks get-credentials`](https://learn.microsoft.com/en-us/cli/azure/aks?view=azure-cli-latest#az-aks-get-credentials), but Amazon Elastic Kubernetes Service (EKS) creates an incompatible kubeconfig with [`aws eks update-kubeconfig`](https://awscli.amazonaws.com/v2/documentation/api/latest/reference/eks/update-kubeconfig.html). Use the following script with the example [service account definition](./service-account.yml) to create a compatible kubeconfig for EKS:

```shell
# Use a local kubeconfig to avoid polluting the default one
export KUBECONFIG=kubeconfig.yml

# Create the default EKS context and get the current EKS user
aws eks update-kubeconfig --name <name>
USER=$(kubectl config view --minify --output jsonpath='{.users[].name}')

# Create the service account and get the token
kubectl apply --filename service-account.yml
TOKEN=$(kubectl get secret elastic-machines --output jsonpath='{.data.token}' | base64 --decode)

# Replace the EKS user's exec command with the service account's token
kubectl config unset "users.$USER.exec"
kubectl config set-credentials "$USER" --token "$TOKEN"
```

> Some cloud platforms do not support multiline environment variables or secrets very well. For example, deploying Elastic Machines to Azure with Terraform will preserve environment variable formatting, but editing an environment variable through the portal will strip any newlines. Consider minifying the kubeconfig YAML before setting `KUBECONFIG_CONTENTS` manually or use a CLI command like `az config` that properly handles newlines:
>
> ```shell
> az config set "KUBECONFIG_CONTENTS=$(kubectl config view --raw)"
> ```

## Requirements

Elastic Machines accepts a full [PodTemplateSpec](https://kubernetes.io/docs/reference/generated/kubernetes-api/v1.26/#podtemplatespec-v1-core) object, but there are several requirements to be aware of.

### Runner executable

The container image must have the [runner executable](https://github.com/actions/runner) installed and must run it on startup by executing `./run.sh`. The easiest way to satisfy these requirements is to use an image based on the official [actions-runner](https://github.com/actions/runner/pkgs/container/actions-runner) container image and specify `./run.sh` as the container's `command`:

```yaml
- name: <name>
  provider: kubernetes-pod@v0
  with:
    template:
      spec:
        containers:
          - name: runner
            image: ghcr.io/actions/actions-runner:latest
            command: [./run.sh]
```

> The runner should not be configured in advance (with `config.sh`) because Elastic Machines provides configuration information via environment variables when starting the container.

If it's not possible to base your image on the official runner image, you must install the runner manually. The exact commands depend on your image, but the following Dockerfile commands will work on most images with `curl` and `tar` installed:

```dockerfile
ARG RUNNER_VERSION
WORKDIR runner
RUN curl -fsSL "https://github.com/actions/runner/releases/download/v$RUNNER_VERSION/actions-runner-linux-x64-$RUNNER_VERSION.tar.gz" | tar xz \
    && ./bin/installdependencies.sh
ENTRYPOINT ["./run.sh"]
```

If editing the image is not possible, the runner executable can be installed when the pod starts up. The exact command depends on your image, but the following command will work on most images with `curl` and `tar` installed:

```yaml
- name: <name>
  provider: kubernetes-pod@v0
  with:
    template:
      spec:
        containers:
          - name: runner
            image: <image>
            workingDir: /runner
            command: [bash]
            args:
              - -c
              - curl -fsSL https://github.com/actions/runner/releases/download/v<runner_version>/actions-runner-linux-x64-<runner_version>.tar.gz | tar xz && ./bin/installdependencies.sh && ./run.sh
```

### Nodes

The Kubernetes cluster must have nodes that can accommodate the image, OS, and architecture specified in the provider Pod template. The cluster must also have an appropriate number of nodes available to handle the desired number of runners.

### Restart Policy

The `restartPolicy` of the Pod spec should be set to `Never` or left unset. Elastic Machines will handle the lifecycle of the pod and will not restart a pod if it exits with an error. Pods will be created as needed to meet the runner demands, within the configured scale parameters.

### Container Image Registry Authentication

To authenticate to a private image registry, the `imagePullSecrets` node of the Pod spec must be set to the name(s) of Kubernetes secret(s) that contain a `docker-registry` secret value with proper credentials to pull the container image(s) in use. More information is available at [the Kubernetes documentation](https://kubernetes.io/docs/concepts/containers/images/#specifying-imagepullsecrets-on-a-pod).

### Image OS and Architecture

The Kubernetes Pod provider will attempt to determine the OS/arch values needed for the runner container image by reading the image manifest via the Docker API. This works for images hosted in Docker Hub and other registries, including private registries that require authentication, so long as [the image pull secret](#container-image-registry-authentication) is configured.

In the case the image in use leverages [multi-CPU architecture](https://docs.docker.com/desktop/multi-arch/), the provider will use the OS and architecture found in the first listed image manifest or default to `linux` and `amd64`. If the detected values are incorrect for the desired configuration, override the `kubernetes.io/os` and `kubernetes.io/arch` node selectors in your configuration:

```yaml
- name: <name>
  provider: kubernetes-pod@v0
  with:
    template:
      spec:
        containers:
          - name: runner
            image: ghcr.io/actions/actions-runner:latest
            command: [./run.sh]
        nodeSelector:
          kubernetes.io/os: linux
          kubernetes.io/arch: arm64
```
