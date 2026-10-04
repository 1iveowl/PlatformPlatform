// The deployment the staging acceptance checks, as Azure describes it (read only), and the image and fingerprint gate that
// runs before every other check: each deployed app runs the tag being verified on every active revision, and the documents
// the app host and the back-office host serve reference only fingerprinted assets of that tag's image.
//
// The functions passed in:
// - azure: createAzure of support/deployed.mjs, { json(arguments), raw(arguments) }
// - send(identity, method, url, { headers }): a recorded request of the entry point, { status, body, recorded }
// - readImageManifest(image): the routes of the image's endpoint manifest, a Set; pulls the image unless replaced

import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { pathBase, readEndpointManifest } from "../support/stack.mjs";

// The apps the deployment procedure deploys, and the image repository each runs (back-office runs the account API image)
export const deployedApps = { "account-api": "account-api", "back-office": "account-api", "account-workers": "account-workers", "blazor-host": "blazor-host" };

// Paths the Blazor host generates when it runs rather than publishes, so they are not in the image's endpoint manifest:
// the brand stylesheet and the web manifest (HostApplication) and the framework's resource collection. Observed on
// 2026-09-30 as the only references of the served documents outside the manifest of 2026.09.30.1815.
const hostGeneratedPaths = [/^brand\.css$/, /^manifest\.webmanifest$/, /^_framework\/resource-collection(\.[a-z0-9]+)?\.js(\.gz)?$/];

// Every deployed app, its environment variables, the two public hosts and the managed environment
export function readDeployment(azure, resourceGroup) {
  const readApp = (name) => azure.json(["containerapp", "show", "--name", name, "--resource-group", resourceGroup]);
  const apps = Object.fromEntries(Object.keys(deployedApps).map((name) => [name, readApp(name)]));
  const environmentOf = (app) => Object.fromEntries((app.properties.template.containers[0].env ?? []).map((variable) => [variable.name, variable.value ?? `secretRef:${variable.secretRef}`]));
  const variables = Object.fromEntries(Object.entries(apps).map(([name, app]) => [name, environmentOf(app)]));
  const backOfficeHost = apps["back-office"].properties.configuration.ingress.customDomains?.[0]?.name;
  if (!backOfficeHost) throw new Error("The back-office app has no custom domain.");
  const managedEnvironment = azure.json(["containerapp", "env", "show", "--ids", apps["back-office"].properties.managedEnvironmentId]);
  return {
    apps,
    variables,
    backOfficeHost,
    backOfficeUrl: `https://${backOfficeHost}`,
    appUrl: variables["blazor-host"].PUBLIC_URL,
    managedEnvironment,
    environmentDomain: managedEnvironment.properties.defaultDomain
  };
}

const imageTag = (image) => image.slice(image.lastIndexOf(":") + 1);
const imageRepository = (image) => image.slice(image.indexOf("/") + 1, image.lastIndexOf(":"));

// The routes of the Blazor host image's endpoint manifest, read from a container created from the pulled image
export function pullImageManifest(azure, image) {
  const registry = image.slice(0, image.indexOf("/"));
  azure.raw(["acr", "login", "--name", registry.split(".")[0]]);
  execFileSync("docker", ["pull", "--quiet", image], { stdio: "ignore" });
  const container = execFileSync("docker", ["create", image], { encoding: "utf8" }).trim();
  const folder = mkdtempSync(path.join(os.tmpdir(), "staging-acceptance-"));
  try {
    const file = path.join(folder, "endpoints.json");
    execFileSync("docker", ["cp", `${container}:/app/Blazor.Host.staticwebassets.endpoints.json`, file], { stdio: "ignore" });
    return new Set(readEndpointManifest(file).map((endpoint) => endpoint.Route));
  } finally {
    execFileSync("docker", ["rm", container], { stdio: "ignore" });
    rmSync(folder, { recursive: true, force: true });
  }
}

// Every asset path below the path base that a document references: attributes, the import map and the preload list
export function referencedAssets(html) {
  const assets = new Set();
  const pattern = /["'](?:\/blazor\/|\.\/)?((?:_framework|_content|js|css|fonts|images)\/[^"'?#\s]+|[A-Za-z0-9._-]+\.(?:css|js|webmanifest|ico|png|svg))(?:\?[^"'\s]*)?["']/g;
  for (const match of html.matchAll(pattern)) assets.add(match[1]);
  return [...assets];
}

// The gate's check body: throws with every failure found, or returns the revisions, the image and the documents it read
export async function checkImageGate({ azure, resourceGroup, tag, appUrl, backOfficeUrl, send, requests, readImageManifest = (image) => pullImageManifest(azure, image) }) {
  const failures = [];
  const revisions = {};
  for (const [name, repository] of Object.entries(deployedApps)) {
    const active = azure.json(["containerapp", "revision", "list", "--name", name, "--resource-group", resourceGroup]).filter((revision) => revision.properties.active);
    revisions[name] = active.map((revision) => ({
      name: revision.name,
      image: revision.properties.template.containers[0].image,
      trafficWeight: revision.properties.trafficWeight ?? null,
      healthState: revision.properties.healthState,
      runningState: revision.properties.runningState
    }));
    if (active.length === 0) failures.push(`${name} has no active revision`);
    for (const revision of revisions[name]) {
      if (imageTag(revision.image) !== tag || imageRepository(revision.image) !== repository) failures.push(`${name} revision ${revision.name} runs ${revision.image}, not ${repository}:${tag}`);
      if (revision.healthState !== "Healthy") failures.push(`${name} revision ${revision.name} is ${revision.healthState}`);
    }
  }
  if (failures.length > 0) throw new Error(failures.join("; "));

  const image = revisions["blazor-host"][0].image;
  const routes = readImageManifest(image);
  const appCss = [...routes].find((route) => /^app\.[a-z0-9]+\.css$/.test(route));
  const blazorWeb = [...routes].find((route) => /^_framework\/blazor\.web\.[a-z0-9]+\.js$/.test(route));
  const documents = {};
  for (const [label, identity, url] of [
    ["app host", null, `${appUrl}${pathBase}/`],
    ["back-office host", "admin", `${backOfficeUrl}${pathBase}/back-office`]
  ]) {
    const answer = await send(identity, "GET", url, { headers: { Accept: "text/html" } });
    requests.push(answer.recorded);
    if (answer.status !== 200) {
      failures.push(`${label}: ${url} answered ${answer.status}`);
      continue;
    }
    const assets = referencedAssets(answer.body);
    const foreign = assets.filter((asset) => !routes.has(asset) && !hostGeneratedPaths.some((pattern) => pattern.test(asset)));
    documents[label] = { url, assets: assets.length, notInImage: foreign };
    if (foreign.length > 0) failures.push(`${label}: ${url} references ${foreign.join(", ")}, which the image ${image} does not contain`);
    if (!assets.includes(appCss)) failures.push(`${label}: ${url} does not reference ${appCss} of ${image}`);
    if (!assets.includes(blazorWeb)) failures.push(`${label}: ${url} does not reference ${blazorWeb} of ${image}`);
  }
  if (failures.length > 0) throw new Error(failures.join("; "));
  return { revisions, image, imageRoutes: routes.size, appCss, blazorWeb, documents };
}
