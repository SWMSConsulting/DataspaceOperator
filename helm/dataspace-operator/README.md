# dataspace-operator (Helm chart)

Deploys the central dataspace operator service (issuer / BDRS directory / DCP issuance / status
list) — the DevExpress XAF Blazor app — to Kubernetes.

## What it deploys

- **Deployment** (1 replica) of the app image, serving on `:8080`.
- **Init container** running `--updateDatabase` to create/migrate the schema before serving.
- **PVC** for the SQLite database (single-writer → keep `replicaCount: 1`).
- **ConfigMap** (non-secret config) + **Secret** (issuer seed, XAF URL signing key).
- **Service**, optional **Ingress**.
- Optional **HashiCorp Vault** (disabled by default — see caveat below).

Machine-facing protocol endpoints stay anonymous: `/.well-known/did.json`,
`/api/directory/bpn-directory`, `/api/issuance/*`, `/status-lists/revocation`. The Blazor admin
UI requires login (XAF security).

## 1. Build & publish the image (GitHub → GHCR)

A workflow is provided at `.github/workflows/build-image.yml`. It builds the image and pushes it to
`ghcr.io/<owner>/<repo>`.

**Prerequisite:** DevExpress v25.1+ restores from nuget.org but the **build needs your license key**.
Create a repository secret named **`DEVEXPRESS_LICENSE`** (Settings → Secrets and variables → Actions)
with your personal DevExpress .NET license key. The workflow passes it as the `DevExpress_License`
build-arg (exact casing required).

Push to `main` (or tag `v*`) to trigger the build. Make the GHCR package accessible to your cluster
(public, or create an image pull secret and set `imagePullSecrets`).

> Build locally instead: `docker build --build-arg DevExpress_License=<KEY> -t dataspace-operator .`
> Add `--build-arg BUILD_CONFIGURATION=Debug` for an **evaluation** image that seeds the default
> `Admin`/`User` accounts (the `Release` config omits them — see Security notes).

## 2. Install

```bash
cd helm/dataspace-operator
helm dependency build          # fetches the (optional) vault subchart
helm install operator . \
  --set image.repository=ghcr.io/<owner>/<repo> \
  --set image.tag=main \
  --set issuer.did=did:web:issuer.example.org \
  --set issuer.privateSeedBase64=$(openssl rand -base64 32) \
  --set security.urlSigningKey=$(uuidgen)
```

Then point your `did:web` host at `GET /.well-known/did.json` and expose the Service/Ingress.

## 3. Key values

| Value | Purpose |
|---|---|
| `image.repository` / `image.tag` | the GHCR image |
| `issuer.did` | the operator issuer DID |
| `issuer.privateSeedBase64` | Ed25519 signing-key seed (**secret**); empty = auto-generate (unstable DID) |
| `issuer.existingSecret` / `existingSecretSeedKey` | use a pre-existing secret for the seed |
| `security.urlSigningKey` | XAF URL signing key (**secret**) |
| `database.connectionString` | EF Core connection string (`{dataDir}` = PVC mount) |
| `persistence.*` | SQLite PVC |
| `ingress.*` | ingress |
| `vault.*` | optional HashiCorp Vault (see below) |

## Security notes

- **Issuer seed & URL signing key are secrets** — set real values (or `existingSecret`). Do not keep
  the placeholder `security.urlSigningKey`.
- **Release images seed no users.** The default `Admin`/`User` accounts are created only under the
  `Debug` config (`#if !RELEASE`). For a production `Release` image, provision the first admin via
  your own onboarding/job. For evaluation, build with `BUILD_CONFIGURATION=Debug`.
- `ASPNETCORE_ENVIRONMENT=Production` disables the dev-only `/admin/*` trigger endpoints.

## HashiCorp Vault (optional) — status

**The application does not yet have a native Vault client** — it reads the issuer seed from
configuration/env via `ISecretStore` (`ConfigurationSecretStore`). So Vault is **opt-in scaffolding**,
not a wired default:

- `vault.enabled=true` deploys the HashiCorp Vault subchart (dev mode by default — evaluation only).
- `vault.injector.enabled=true` adds Agent-Injector annotations that render the seed to a file and an
  `export Issuer__PrivateSeedBase64=...` template.

To make Vault the real source of the seed you still need **one** of:
1. source the injected file in the container command (wrap the entrypoint), or
2. implement a `HashiCorpVaultSecretStore : ISecretStore` in the app (the seam already exists).

Until then, the **Kubernetes Secret** path (default) is the supported way to supply the seed.
