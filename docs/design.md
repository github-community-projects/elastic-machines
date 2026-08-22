# Design

Understanding how Elastic Machines communicates with GitHub and cloud providers can help you understand Elastic Machines's behavior and troubleshoot any issues. This overview is fairly high level since Elastic Machines is actively developed and details will change over time.

## Communication

Elastic Machines's main function is to facilitate communication between GitHub and various clouds (AWS, Azure, etc.). Elastic Machines gathers data about the state of jobs and runners in GitHub and instructs the cloud provider to create or destroy machines to match the desired capacity.

### Long poll

Elastic Machines uses long polling to get data from GitHub as quickly as possible. Every poll triggers the following sequence.

```mermaid
sequenceDiagram
    participant GH as GitHub
    participant EM as Elastic Machines
    participant CP as Cloud Provider
    EM->>+GH: long poll
    GH->>GH: jobs started or completed
    GH-->>-EM: statistics and messages
    EM->>EM: calculate delta
    EM->>GH: create runner registration tokens
    par
      EM->>CP: create new machines
      EM->>CP: start runner on new machines
    and
      EM->>CP: remove offline machines
    and
      EM->>GH: acquire jobs
    end
```

### Refresh state

Elastic Machines also does a full refresh every hour to detect discrepancies.

```mermaid
sequenceDiagram
    participant GH as GitHub
    participant EM as Elastic Machines
    participant CP as Cloud Provider
    EM->>GH: query for runners
    EM->>CP: query for machines
    EM->>GH: remove offline runners
    EM->>CP: remove offline machines
    EM->>GH: query for jobs
```

## Configuration updates

Elastic Machines has a separate process to keep track of `elastic-machines.yml` files in your repositories and apply updates when changes are detected.

### Webhooks

Webhooks are optional, but enabling `push` events allows Elastic Machines to detect configuration changes immediately.

```mermaid
sequenceDiagram
    participant GH as GitHub
    participant EM as Elastic Machines
    participant CP as Cloud Provider
    GH->>GH: files changed
    GH->>EM: webhook
    EM->>EM: remove outdated orchestrators
    EM->>EM: create new orchestrators
    EM->>GH: create new runner scale sets
    EM->>CP: create new cloud provider resources
```

### Refresh configurations

Elastic Machines also does a full refresh every hour to detect discrepancies.

```mermaid
sequenceDiagram
    participant GH as GitHub
    participant EM as Elastic Machines
    participant CP as Cloud Provider
    EM->>GH: query for configurations
    EM->>EM: remove outdated orchestrators
    EM->>EM: create new orchestrators
    EM->>GH: create new runner scale sets
    EM->>CP: create new cloud provider resources
```
