# Deploying Elastic Machines to Kubernetes

This guide describes how to deploy Elastic Machines to a Kubernetes cluster using a custom domain name.

## Basic Components

The main Kubernetes resources that comprise an Elastic Machines deployment in Kubernetes are:

1. A [Kubernetes deployment](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/) consisting of the Elastic Machines container, configured with appropriate values to connect to the GitHub app and control the desired runner provider(s).
2. A [Kubernetes Service](https://kubernetes.io/docs/concepts/services-networking/service/) definition pointing to the Elastic Machines deployment on port 80.
3. A [Kubernetes Ingress](https://kubernetes.io/docs/concepts/services-networking/ingress/) definition exposing the Elastic Machines service securely via TLS over port 443 to the Internet (to support webhook delivery from GitHub).
  a. This guide focuses on the [Nginx Ingress Controller](https://kubernetes.github.io/ingress-nginx/), which is community-driven and easily supports TLS; however, other ingress controllers can be used.
  b. Likewise, this guide utilizes [Cert Manager](https://cert-manager.io/) to automatically provision TLS certificates from [Let's Encrypt](https://letsencrypt.org/); however, other certificate providers/existing certificates can also be used.

A sample deployment template containing these components can be found in this [example workflow](deploy-service.yml).

## Prerequisites

This guide assumes:

* A GitHub app has been configured according to the steps defined in the [readme](../../../README.md#github-app).
* A working Kubernetes cluster and working installations of Helm and `kubectl`.
* An [image pull secret](https://kubernetes.io/docs/concepts/containers/images/#creating-a-secret-with-a-docker-config) named `ghcr` that is configured to pull images from the GitHub Container Registry (GHCR) for the Elastic Machines service container.
* Access to a domain name that can be used to access the Elastic Machines service (e.g. `elastic-machines.<my org>.com`), and the ability to update DNS records to point to an IP address assigned to a Kubernetes Ingress.

Additionally, the following Helm charts must be installed:

### Nginx Ingress Controller

```console
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo update

helm install ingress-nginx ingress-nginx/ingress-nginx \
  --namespace <namespace> \
  --set controller.nodeSelector."kubernetes\.io/os"=linux \
  --set controller.admissionWebhooks.patch.nodeSelector."kubernetes\.io/os"=linux \
  --set defaultBackend.nodeSelector."kubernetes\.io/os"=linux \
  --set controller.service.annotations."service\.beta\.kubernetes\.io/azure-load-balancer-health-probe-request-path"=/healthz
```

Notes:

* The `nodeSelector`s defined in the above command are only needed in Kubernetes clusters that support multiple operating systems (i.e. Windows in addition to Linux). If the cluster only supports Linux, these values may be omitted.
* The service annotation is specific to Azure Kubernetes Service (AKS). Another value may be required for other Kubernetes cloud hosts, to bridge the ingress controller with the underlying infrastructure provided by each cloud host.

Once the ingress controller is installed, the installation can be verified by running the following command:

```console
kubectl get services -n <namespace> -o wide -w ingress-nginx-controller
```

At this point, the external IP address of the ingress controller should be populated in the 'EXTERNAL-IP' column. This IP address should be used to configure a custom domain name (e.g. `elastic-machines.<my org>.com`) that will be used for the Elastic Machines service.

### Cert Manager

```console
helm repo add jetstack https://charts.jetstack.io
helm repo update

kubectl label namespace <namespace> cert-manager.io/disable-validation=true

helm install cert-manager jetstack/cert-manager \
  --namespace <namespace> \
  --version <specific version, if desired> \
  --set installCRDs=true \
  --set nodeSelector."kubernetes\.io/os"=linux
```

Notes:

* The `nodeSelector` defined in the above command is only needed in Kubernetes clusters that support multiple operating systems (i.e. Windows in addition to Linux). If the cluster only supports Linux, this value may be omitted.

## Deploy Kubernetes Resources

Once the Nginx Ingress Controller and Cert Manager are installed, the Elastic Machines service can be deployed to the Kubernetes cluster.

First, using the [deployment template](deploy-service.yml) as a starting point, the following values will need to be updated:

* Environment variables on the service container (e.g. `APP_ID` et al.).
  * These values are all sourced from the GitHub App configuration.
  * Additionally, any environment variables needed to configure the runner provider(s) should be defined here, e.g. a `KUBECONFIG_CONTENTS` value used to connect to the Kubernetes cluster for the `kubernetes-pod` runner provider, or any Azure or AWS values needed to control providers in those environments.
* The 'host' values in the ingress definition.
* The email address used to register the TLS certificate with Let's Encrypt (both staging and production environments).
* (Optionally) edit the `cert-manager.io/issuer: letsencrypt` annotation to use the `letsencrypt-staging` certificate issuer, to verify that the certificate is being issued correctly before switching to the production issuer.

Next, use the updated deployment yaml to deploy the Elastic Machines service to the Kubernetes cluster:

```console
kubectl apply -f <path to deployment yaml>/deploy-service.yml -n <namespace>
```

At this point the Elastic Machines service should be deployed and accessible via the custom domain name (e.g. `elastic-machines.<my org>.com`). If the `letsencrypt-staging` issuer was used, the certificate will be issued by Let's Encrypt's staging environment, which has a shorter validity period and a lower rate limit. If the certificate is issued successfully, the production issuer can be used by updating the `cert-manager.io/issuer` annotation to the `letsencrypt` issuer.

## Resources

* [NGINX Ingress Controller](https://kubernetes.github.io/ingress-nginx/)
* [Cert Manager Installation](https://cert-manager.io/docs/installation/helm/)
* [Create an ingress controller in Azure Kubernetes Service (AKS)](https://learn.microsoft.com/en-us/azure/aks/ingress-basic?tabs=azure-cli)
* [Use TLS with an ingress controller on Azure Kubernetes Service (AKS)](https://learn.microsoft.com/en-us/azure/aks/ingress-tls?tabs=azure-cli)
* [Securing NGINX-ingress](https://cert-manager.io/docs/tutorials/acme/nginx-ingress/)
* [Troubleshooting Problems with ACME / Let's Encrypt Certificates](https://cert-manager.io/docs/troubleshooting/acme/)
