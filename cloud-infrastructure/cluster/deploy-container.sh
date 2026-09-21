#!/bin/bash

# Deploys one container image to its container app in a cluster, from a workstation.
#
# Usage:
#   deploy-container.sh UNIQUE_PREFIX ENVIRONMENT IMAGE_NAME VERSION [options]
#
#   UNIQUE_PREFIX            The prefix the cluster resources are named with, e.g. "pp"
#   ENVIRONMENT              "stage" or "prod"
#   IMAGE_NAME               The repository in the container registry, e.g. "blazor-host"
#   VERSION                  The image tag to build or import and then run, e.g. "2026.09.21.1"
#
# Options:
#   --context <dir>          Docker build context, relative to the repository root, e.g. "blazor"
#   --dockerfile <file>      Dockerfile, relative to the context, e.g. "./Blazor.Host/Dockerfile"
#   --container-app <name>   The container app to update, when it differs from IMAGE_NAME
#   --import-from <id>       Subscription id of the staging registry to import the image from
#                            instead of building it; this is how a production deploy gets an image
#                            that was built and verified in staging
#   --cluster-location-acronym <acronym>  The location part of the cluster resource group name
#                            (default "weu"), e.g. "eus2"
#   --help                   Print this text and exit
#
# It does what the three container deployment steps of .github/workflows/_deploy-container.yml do, in
# the same order, so a deployment by hand and a deployment by the workflow produce the same revision:
# sign in to the container registry, build and push the image for both architectures (or import it
# from staging), update the container app to the new image with a revision suffix derived from the
# version, then wait for that revision to report healthy. Requires `az login` first, plus docker with
# buildx for the build path. It never deploys infrastructure; deploy-cluster.sh does that.
#
# The blazor-host image copies a publish the developer CLI produces, which does not land in the build
# context on its own:
#   dotnet run --project developer-cli -- blazor-publish --version <VERSION>
#   rm -rf blazor/Blazor.Host/publish && cp -r .workspace/blazor-publish blazor/Blazor.Host/publish
#   cloud-infrastructure/cluster/deploy-container.sh pp stage blazor-host <VERSION> \
#     --context blazor --dockerfile ./Blazor.Host/Dockerfile
# Publish with the same version the image is tagged with: the version a client compares against the
# server's is the assembly version of that publish, not the image tag.

set -eo pipefail

CYAN='\033[0;36m'
RED='\033[0;31m'
RESET='\033[0m' # Reset formatting

# Everything between the shebang and the first line of code is the usage text
print_usage()
{
  awk 'NR < 3 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"
}

if [[ "$*" == *"--help"* ]] || [[ $# -eq 0 ]]; then
  print_usage
  exit 0
fi

UNIQUE_PREFIX=$1
ENVIRONMENT=$2
IMAGE_NAME=$3
VERSION=$4
if [[ $# -ge 4 ]]; then shift 4; else shift $#; fi

CONTAINER_APP_NAME=""
DOCKER_CONTEXT=""
DOCKER_FILE=""
IMPORT_FROM_SUBSCRIPTION_ID=""
CLUSTER_LOCATION_ACRONYM="weu"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --context) DOCKER_CONTEXT="$2"; shift 2 ;;
    --dockerfile) DOCKER_FILE="$2"; shift 2 ;;
    --container-app) CONTAINER_APP_NAME="$2"; shift 2 ;;
    --import-from) IMPORT_FROM_SUBSCRIPTION_ID="$2"; shift 2 ;;
    --cluster-location-acronym) CLUSTER_LOCATION_ACRONYM="$2"; shift 2 ;;
    *) echo "ERROR: Unknown option '$1'. Run with --help." >&2; exit 1 ;;
  esac
done

if [[ -z "$UNIQUE_PREFIX" || -z "$ENVIRONMENT" || -z "$IMAGE_NAME" || -z "$VERSION" ]]; then
  echo "ERROR: UNIQUE_PREFIX, ENVIRONMENT, IMAGE_NAME and VERSION are all required. Run with --help." >&2
  exit 1
fi

if [[ -z "$IMPORT_FROM_SUBSCRIPTION_ID" ]] && { [[ -z "$DOCKER_CONTEXT" ]] || [[ -z "$DOCKER_FILE" ]]; }; then
  echo "ERROR: --context and --dockerfile are required unless --import-from is used. Run with --help." >&2
  exit 1
fi

REPOSITORY_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
CONTAINER_REGISTRY_NAME="$UNIQUE_PREFIX$ENVIRONMENT"
CLUSTER_RESOURCE_GROUP_NAME="$UNIQUE_PREFIX-$ENVIRONMENT-$CLUSTER_LOCATION_ACRONYM"
TARGET_APP="${CONTAINER_APP_NAME:-$IMAGE_NAME}"
IMAGE="$CONTAINER_REGISTRY_NAME.azurecr.io/$IMAGE_NAME:$VERSION"

echo -e "$CYAN$(date +"%Y-%m-%dT%H:%M:%S") Signing in to container registry $CONTAINER_REGISTRY_NAME...$RESET"
az acr login --name "$CONTAINER_REGISTRY_NAME"

if [[ -n "$IMPORT_FROM_SUBSCRIPTION_ID" ]]; then
  STAGING_REGISTRY_ID="/subscriptions/$IMPORT_FROM_SUBSCRIPTION_ID/resourceGroups/$UNIQUE_PREFIX-stage-global/providers/Microsoft.ContainerRegistry/registries/${UNIQUE_PREFIX}stage"

  echo -e "$CYAN$(date +"%Y-%m-%dT%H:%M:%S") Importing $IMAGE_NAME:$VERSION from staging...$RESET"
  az acr import \
    --name "$CONTAINER_REGISTRY_NAME" \
    --source "$IMAGE_NAME:$VERSION" \
    --image "$IMAGE_NAME:$VERSION" \
    --registry "$STAGING_REGISTRY_ID" \
    --force
else
  echo -e "$CYAN$(date +"%Y-%m-%dT%H:%M:%S") Building and pushing $IMAGE...$RESET"
  docker buildx create --use
  docker buildx build \
    --platform linux/amd64,linux/arm64 \
    --build-arg VERSION="$VERSION" \
    -t "$IMAGE" \
    -f "$REPOSITORY_ROOT/$DOCKER_CONTEXT/$DOCKER_FILE" \
    --push "$REPOSITORY_ROOT/$DOCKER_CONTEXT"
  docker buildx rm
fi

SUFFIX=$(echo "$VERSION" | sed 's/\./-/g')

echo -e "$CYAN$(date +"%Y-%m-%dT%H:%M:%S") Updating container app $TARGET_APP to $IMAGE...$RESET"
az containerapp update --name "$TARGET_APP" --resource-group "$CLUSTER_RESOURCE_GROUP_NAME" --image "$IMAGE" --revision-suffix "$SUFFIX"

echo "Waiting for the new revision to be active..."
for i in {1..10}; do
  sleep 15

  RUNNING_STATUS=$(az containerapp revision list --name "$TARGET_APP" --resource-group "$CLUSTER_RESOURCE_GROUP_NAME" --query "[?contains(name, '$SUFFIX')].properties.runningState" --output tsv)
  HEALTH_STATUS=$(az containerapp revision list --name "$TARGET_APP" --resource-group "$CLUSTER_RESOURCE_GROUP_NAME" --query "[?contains(name, '$SUFFIX')].properties.healthState" --output tsv)
  if [[ "$HEALTH_STATUS" == "Healthy" ]]; then
    echo "New revision is healthy. Running state: $RUNNING_STATUS"
    exit 0
  fi
  if [[ "$HEALTH_STATUS" == "Unhealthy" ]]; then
    echo -e "${RED}New revision is Unhealthy. Running state: $RUNNING_STATUS${RESET}"
    exit 1
  fi

  echo "($i) Waiting for revision to become active. Running state: $RUNNING_STATUS"
done
echo -e "${RED}New revision did not become active in time. Running state: $RUNNING_STATUS${RESET}"
exit 1
