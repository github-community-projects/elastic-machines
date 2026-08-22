# Bicep

[Bicep](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/overview) is an alternative way to deploy if you don't want to use the [Azure Container Instances YAML format](README.md). Use the following example Bicep file to deploy a single Elastic Machines configuration or modify it to work in your environment (e.g. multiple configurations, modified resource limits, etc.):

```bicep
@secure()
param ghcrToken string

@secure()
param privateKey string

var configurationFile = '<configuration>.yml'

resource containergroup 'Microsoft.ContainerInstance/containerGroups@2021-10-01' = {
  location: resourceGroup().location
  name: '<name>'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    osType: 'Linux'
    imageRegistryCredentials: [
      {
        server: 'ghcr.io'
        username: '<username>'
        password: ghcrToken
      }
    ]
    containers: [
      {
        name: loadYamlContent(configurationFile).name
        properties: {
          image: 'ghcr.io/elastic-machines/azure-virtual-machine:<version>'
          resources: {
            requests: {
              cpu: 1
              memoryInGB: 2
            }
          }
          environmentVariables: [
            {
              name: 'APP_ID'
              value: '<app-id>'
            }
            {
              name: 'PRIVATE_KEY'
              secureValue: privateKey
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: '<applicationinsights-connection-string>'
            }
            {
              name: 'CONFIGURATION'
              value: loadTextContent(configurationFile)
            }
          ]
        }
      }
    ]
  }
}
```

Copy this template to a new `.bicep` file and fill in any placeholders (denoted in `<brackets>`) with proper values for your environment. To deploy multiple Elastic Machines configurations, add more entries under the `containers` property or create multiple container group resources for more isolation. Refer to the [multiple containers](README.md#multiple-containers) section in the readme for more information on the different ways to organize containers.

Deploy the Bicep template with the following [command](https://learn.microsoft.com/en-us/cli/azure/deployment/group?view=azure-cli-latest#az-deployment-group-create):

```shell
az deployment group create \
    --resource-group <resource-group> \
    --template-file <template-file>.bicep \
    --parameters ghcrToken=<ghcr-token> privateKey=@<private-key>.pem
```

> [!NOTE]
> File contents can be passed as a parameter by prefixing the file path with an `@` symbol (as shown in the previous command).

This example references an Elastic Machines configuration stored in a separate YAML file, but the configuration could also be passed in as a parameter or defined inline by wrapping the multiline string in triple quotes:

```bicep
{
  name: 'CONFIGURATION'
  value: '''
    name: <name>
    url: <url>
    with:
      image: Canonical:0001-com-ubuntu-server-jammy:22_04-lts-gen2:latest
      location: centralus
      resourceGroup: <resource-group>
      subscription: <subscription>
      vmSku: Standard_DS1_v2
      # ...
  '''
}
```

Once deployed, follow the [post deployment steps](README.md#post-deployment) in the readme. Also refer to the readme for general information, such as [resource limits](README.md#limits), [troubleshooting steps](README.md#troubleshooting), etc.
