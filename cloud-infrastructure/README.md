## Cloud Infrastructure

This folder contains Bash and [Bicep](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/overview) scripts used by [GitHub Actions](https://github.com/features/actions) to deploy resources to Azure.

Bicep is an Infrastructure-as-Code (IaC) language specific to Azure. While Bicep is less mature than Terraform, it is a great choice for Azure-only projects. Unlike Terraform which often is many months behind, Bicep is always up to date with the latest Azure features, and as it matures, it will become the better choice for Azure infrastructure. The tooling is already much better than Terraform, and since Bicep is a typed language, you get intellisense and the compiler will catch many errors while writing the code.

## Getting started

Please follow the simple instructions in [Getting started](/README.md#setting-up-cicd-with-passwordless-deployments-from-github-to-azure-in-minutes) to setup passwordless deployments from GitHub to Azure.

## Folder structure

- `environment`: Each environment (like `Staging` and `Production`) has resources that are shared between clusters, e.g., Azure Log Analytics workspace and Application Insights. This allows for central tracking and monitoring across clusters. No Personally Identifiable Information (PII) is tracked, which ensures compliance with data protection laws. See the [`environment/main-environment.bicep`](/cloud-infrastructure/environment/main-environment.bicep).
- `cluster`: Scripts to deploy a cluster into clearly named resource groups like `ppdemo-stage-weu`, `ppdemo-prod-weu`, and `ppdemo-prod-eus2`. A cluster has its own Azure Container Apps environment (managed Kubernetes), PostgreSQL, Azure Blob Storage, etc. Tenants (a.k.a. a customer) are created in a dedicated cluster that contains all data belonging to that tenant. This ensures compliance with data protection laws like GDPR, CCPA, PIPEDA, APPs, etc., through geo-isolation. See the [`cluster/main-cluster.bicep`](/cloud-infrastructure/cluster/main-cluster.bicep).

- `modules`: Each Azure Resource is created by a separate Bicep module file, ensuring a modular, reusable, and manageable infrastructure.

### Scripts in `cluster`

- `deploy-cluster.sh UNIQUE_PREFIX ENVIRONMENT CLUSTER_LOCATION CLUSTER_LOCATION_ACRONYM POSTGRES_ADMIN_OBJECT_ID DOMAIN_NAME BACK_OFFICE_DOMAIN_NAME --plan|--apply` deploys the cluster itself. It reads the container image tag each container app already runs, so a cluster deployment never changes which image version runs.
- `deploy-container.sh UNIQUE_PREFIX ENVIRONMENT IMAGE_NAME VERSION [options]` deploys one container image to its container app from a workstation, after `az login`. It does what the container deployment steps of `.github/workflows/_deploy-container.yml` do, in the same order: sign in to the registry, build and push for `linux/amd64` and `linux/arm64` (or `--import-from <staging subscription id>` for production), update the container app with a revision suffix derived from the version, then wait for that revision to report healthy. Run it with `--help` for the full option list.

### Container apps

`main-cluster.bicep` creates `account-workers`, `account-api`, `back-office`, `main-workers`, `main-api`, `blazor-host` and `app-gateway`. Only `back-office` and `app-gateway` are externally reachable; the rest take traffic from `app-gateway` on the cluster's internal names, which it reads from `ACCOUNT_API_URL`, `MAIN_API_URL` and `BLAZOR_HOST_URL`.

`blazor-host` serves the Blazor edition under the `/blazor` path base. Its image is built from a trimmed publish the developer CLI produces, which is not in the build context by default:

```bash
dotnet run --project developer-cli -- blazor-publish --version <VERSION>
rm -rf blazor/Blazor.Host/publish && cp -r .workspace/blazor-publish blazor/Blazor.Host/publish
cloud-infrastructure/cluster/deploy-container.sh <UNIQUE_PREFIX> <ENVIRONMENT> blazor-host <VERSION> \
  --context blazor --dockerfile ./Blazor.Host/Dockerfile
```

Publish with the same version the image is tagged with. The version a Blazor client compares against the server's is the assembly version of the publish inside the image, not the image tag.

### Naming Conventions

Azure resources are named using the following convention: `uniquePrefix-environment-locationAcronym-name`.

- `uniquePrefix` (2-6 characters): e.g., `pp`, `ppdemo`
- `environment` (max 5 characters): e.g., `prod`, `dev`, `qa`, `stage`
- `locationAcronym` (max 4 characters): e.g., `weu`, `eus2`
- `name` (for some resources like storage accounts there is a max of 24 characters, so depending on the length of `uniquePrefix` this allows for 9-13 characters)

There are a couple of exceptions:
- Azure Storage Accounts, Azure Container Apps, etc., do not allow `-` in names
- Child resources like Azure Container Apps (ACA) and SQL databases are not prefixed, as ACA has a limit of 32 characters, making names too cryptic

Examples of cluster-specific resources:
- Resource Group: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- PostgreSQL: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- PostgreSQL database: `main`, `account`
- Azure Container App Environment: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- Azure Container Apps: `main-api`, `account-api`, `account-workers`
- Managed Identity: `ppdemo-stage-weu-main`, `ppdemo-stage-weu-account`
- Key Vault: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- Virtual Network: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- Communication Service: `ppdemo-stage-weu`, `ppdemo-prod-eus2`
- Storage Accounts: `ppdemostageweuacctmgmt`, `ppdemoprodweudiagnostic`

Examples of global resources (shared across all clusters in an environment):
- Resource Group: `ppdemo-stage-global`, `ppdemo-prod-global`
- Application Insights: `ppdemo-stage`, `ppdemo-prod`
- Log Analytics workspace: `ppdemo-stage`, `ppdemo-prod`
- Container Registry: `ppdemostage`, `ppdemoprod`

All Azure resources are tagged with `environment` (e.g., `stage`, `prod`) and `managed-by` (e.g., `bicep`, `manual`) for easier cost tracking and resource management.