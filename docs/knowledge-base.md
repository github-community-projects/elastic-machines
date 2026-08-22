# Knowledge base

Explore answers to common questions and other useful information.

## Setup

### Manual deployment

Elastic Machines is packaged as a container, which means it can be deployed almost anywhere as long as all the [required settings](../README.md#container-configuration) are provided.

However, we strongly recommend deploying Elastic Machines to AWS or Azure with the provided [Terraform templates](../templates). The templates offer the best setup experience by handling the details of creating and updating properly configured resources.

### Azure disks

Some workloads may require more storage than Azure's default VM disk size. There are several options for increasing the amount of storage available to each runner:

1. Increase the OS disk size with the `osDisk.sizeGb` property in your [configuration](./providers/azure-virtual-machine.md#yaml-configuration).

2. Add additional data disks by basing VMs on a [custom image](https://docs.microsoft.com/en-us/azure/virtual-machines/image-version). Create this image from a VM with the desired data disks attached and initialized. Data disks from the original VM will be preserved in the custom image and automatically appear on each new runner Elastic Machines creates.

3. Configure [empty data disks](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-attached-disks) with the `dataDisks` property in your [configuration](./providers/azure-virtual-machine.md#yaml-configuration) and format them with a [custom script extension](https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/tutorial-use-disks-cli).

> Empty data disks must be formatted before they can be used, which will impact VM startup time. For the best performance, adjust the OS disk size or use a custom image.

## Limitations

### Ephemeral only

Elastic Machines only supports ephemeral runners.

### Single label only

Elastic Machines only supports a single, unique label. This label is automatically set to the `name` property from the Elastic Machines configuration. Other labels like `self-hosted` or `linux` will not appear on runners created by Elastic Machines so use the configuration `name` to target runners from workflows with the `runs-on` property (e.g. `runs-on: <name>`).

## Manual intervention

### Disabling or resetting a configuration

Do not delete a configuration from an **elastic-machines.yml** file while it has active machines. Elastic Machines will no longer manage or delete any existing VMs once their configuration block is removed. Set `maximumMachines` to zero on the desired configuration and wait for Elastic Machines to shut down all active runners before making changes.

### Deleting runners from GitHub

Do not manually delete online runners from GitHub. Elastic Machines will not immediately know that runners have been deleted so will not scale up enough when new jobs are queued.

### Deleting VMs from cloud providers

Do not delete VMs unless there is no chance for those machines to run jobs while shutting down. Jobs will fail if the VMs shuts down before completion. Also, Elastic Machines will not immediately know that VMs have been deleted so will not scale up enough when new jobs are queued.

## Runners

### Speed up runner creation

For optimal performance, preinstall the [runner](https://github.com/actions/runner) on a [base VM image](https://docs.microsoft.com/en-us/azure/virtual-machines/image-version) at `/home/runneruser/runner` on Linux and `C:\runner` on Windows. If the runner is not present, Elastic Machines will download and extract the agent onto the target machine. Leave the runner unconfigured so that Elastic Machines can provide the correct name, labels, etc. when starting it up.

However, make sure the runner is kept up to date. Actions will not assign jobs to outdated runners. Runners will need to update themselves before running jobs, which will negate any performance gained by preinstalling the runner application.

### Windows automatic updates

We recommend disabling automatic updates since they force runners to restart and go offline. Once runners go offline, Elastic Machines will delete and recreate them from the base image, negating any updates.

### User accounts

Runners are started under a new local user account named `runneruser`. On Windows, this account's password will be reset if it already exists.

### Permissions

Just like GitHub's hosted runners, workflows will have full admin access. User Access Control (UAC) is disabled on Windows and passwordless sudo is enabled on Linux.
