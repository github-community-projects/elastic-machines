# Azure Virtual Machine

This provider can be used to scale Azure virtual machine instances in a resource group.

## YAML Configuration

To use this provider, specify `azure-virtual-machine@v1` as the `provider`. A minimal configuration should include the following properties:

```yaml
- name: <name>
  provider: azure-virtual-machine@v1
  with:
    # The marketplace image URN or compute gallery image ID (with or without the version):
    # - Canonical:0001-com-ubuntu-server-focal:20_04-lts-gen2:latest
    # - /subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.Compute/galleries/<gallery>/images/<image>
    # - /subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.Compute/galleries/<gallery>/images/<image>/versions/<version>
    image: Canonical:0001-com-ubuntu-server-focal:20_04-lts-gen2:latest
    # The location to create resources in (optional if the resource group already exists)
    location: centralus
    # The name of the resource group to create resources in (will be created if needed and Elastic Machines has permission to do so)
    resourceGroup: <resource-group>
    # The Azure subscription ID or name (optional if Elastic Machines only has access to a single subscriptions)
    subscription: <subscription>
    # The size of the virtual machine.
    vmSku: Standard_DS1_v2
```

Use the optional properties documented below to customize the configuration further.

```yaml
- name: <name>
  provider: azure-virtual-machine@v1
  with:
    image: Canonical:0001-com-ubuntu-server-focal:20_04-lts-gen2:latest
    location: centralus
    resourceGroup: <resource-group>
    subscription: <subscription>
    vmSku: Standard_DS1_v2

    # Specifies the parameters that are used to add a data disk to a virtual machine
    dataDisks:
      # Specifies the caching requirements (defaults to None for Standard disks, ReadOnly for Premium disks)
      - caching: None # Options: None, ReadOnly, or ReadWrite
        # Specifies the size of an empty data disk in gigabytes
        sizeGb: 123
        # Specifies the storage account type for the managed disk
        sku: Standard_LRS
    # The extensions to install (retrieved extension details with the Azure CLI: az vm extension image list --location <region> -o table)
    extensions:
      # The virtual machine extension name
      - name: CustomScript
        # The extension specific settings for sensitive values
        protectedSettings:
          <key>: <value>
        # The name of the extension handler publisher
        publisher: Microsoft.Azure.Extensions
        # The extension specific settings
        settings:
          <key>: <value>
        # The version in major.minor format (truncate the full version if needed: 1.2.3.4 -> 1.2)
        version: 1.2
    # The list of user identities associated with the Virtual Machine
    identities:
      - /subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<user-assigned-identity>
    # Specifies that the image or disk that is being used was licensed on-premises
    # For more information, see https://learn.microsoft.com/en-us/azure/virtual-machines/windows/hybrid-use-benefit-licensing
    licenseType: null # Options: Windows_Client, Windows_Server, RHEL_BYOS, SLES_BYOS
    # Override the image's default OS disk properties
    osDisk:
      # Override the image's OS disk caching type
      caching: None # Options: None, ReadOnly, or ReadWrite
      # Enable ephemeral OS disks for better performance by choosing a local storage location (CacheDisk or ResourceDisk)
      # The VM SKU used must have a cache or temp disk large enough to hold the OS disk
      # This option will force the caching type to ReadOnly
      # More information: https://docs.microsoft.com/en-us/azure/virtual-machines/ephemeral-os-disks
      ephemeralPlacement: null # Options: CacheDisk, ResourceDisk
      # Override the image's OS disk size (must be larger than the image's OS disk size)
      sizeGb: 123
      # Override the image's OS disk SKU
      sku: Standard_LRS
    # Enable an individual public IP for each VM, which is helpful when using an existing subnet that does not have outbound internet access
    publicIpPerVm: true
    # Specifies the Security related profile settings for the virtual machine
    securityProfile:
      # This property can be used by user in the request to enable or disable the Host Encryption for the virtual machine or virtual machine scale set
      # This will enable the encryption for all the disks including Resource/Temp disk at host itself
      # The default behavior is: The Encryption at host will be disabled unless this property is set to true for the resource
      encryptionAtHost: true
    # Enable spot virtual machines by setting a maximum price (in US dollars) to charge for each instance per hour
    # Use -1 to indicate no max price - machines will not be evicted for price reasons
    # More information: https://docs.microsoft.com/en-us/azure/virtual-machines/spot-vms
    spotMaxPrice: 0.12
    # Override the default virtual network with existing subnets (the first one will be set as the primary)
    # Ensure subnets have outbound internet access (e.g. through a NAT gateway) or enable publicIpPerVm (see more information below)
    subnets:
      - /subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.Network/virtualNetworks/<virtual-network>/subnets/<subnet>
    # The tags to apply
    tags:
      <key>: <value>
    # The availability zone
    zone: 1

# Additional provider blocks as needed
- name: <name>
  provider: azure-virtual-machine@v1
  with:
    # ...
```

## Authentication

Terraform configures a Managed Service Identity (MSI) with the proper permissions when deploying Elastic Machines to Azure. Use environment variables for authentication when manually running the container outside Azure.

* `AZURE_TENANT_ID`: Directory (tenant) ID

* `AZURE_CLIENT_ID`: Application (client) ID

* `AZURE_CLIENT_SECRET`: Application (client) secret

The identity used for Elastic Machines requires at least **Contributor** access on the resource group(s) where Elastic Machines will create machines and **Reader** access on the subscription(s). However, we recommend giving Elastic Machines **Contributor** access to the subscription as well to allow it to create new resource groups automatically when needed. Additionally, give Elastic Machines **Reader** access to supporting resources in other subscription(s) or resource group(s) that it doesn't already have access to (e.g. gallery images, virtual networks, etc.).

## Subnets

By default, Elastic Machines creates several new networking resources in the target resource group: a virtual network, a network security group, a NAT gateway, and a public IP address. These resources together enable outbound internet access for VMs created in this resource group. Elastic Machines may periodically update or modify these resources so editing them manually is not recommended.

Consider using an existing subnet if the default networking configuration does not meet your needs. Elastic Machines will use any subnet(s) in this provider's configuration instead of creating its own networking resources.

However, ensure any configured subnets have [outbound internet access enabled](https://aka.ms/defaultoutboundaccess). Azure requires outbound access for new VMs and will return the following error if there is no connectivity:

```text
No outbound connectivity configured for virtual machine /subscriptions/<subscription>/resourceGroups/<resource-group>/providers/Microsoft.Compute/virtualMachines/<virtual-machine>. Please attach standard load balancer or public IP address to VM, create NAT gateway or configure user-defined routes (UDR) in the subnet. Learn more at aka.ms/defaultoutboundaccess.
```

Outbound connectivity should be configured such that the virtual machine has access to the GitHub instance. If this instance is GitHub hosted (i.e. `https://github.com`), then public internet access is required. If the instance is GHES, then only network access to the GHES server is required. If it's not possible to add outbound connectivity to an existing subnet, consider enabling `publicIpPerVm` to create an individual IP address for each VM.

## Multiple subscriptions

Elastic Machines can manage resource groups in other subscriptions if Elastic Machines's identity has access to the desired resources.
