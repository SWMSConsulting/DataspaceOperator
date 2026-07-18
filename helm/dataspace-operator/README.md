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
  --set image.repository=ghcr.io/swmsconsulting/dataspaceoperator \
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

## Choosing a secret backend for the issuer key

The operator holds essentially **one** dataspace secret: its issuer signing key. Pick per environment:

| Option | When | Notes |
|---|---|---|
| **Kubernetes Secret** (default) | small/simple deployments | Secrets are only base64 in etcd — enable **encryption at rest** (ideally KMS-backed) and strict RBAC, otherwise it is "obfuscated, not protected". No audit/rotation. |
| **Vault KV** (`vault.app.enabled`) | you need audit/rotation, or already run Vault | seed stored at rest in Vault; the app reads it at startup. |
| **Vault Transit signing** (`vault.transit.enabled`) | the key's compromise is catastrophic | The **private key never leaves Vault** — the app calls Vault to sign. Strongest option for a signing key. |

> Running Vault just for one static key is often over-engineering. `Kubernetes Secret + etcd
> encryption-at-rest + RBAC` is a reasonable default; move to Vault Transit when you want the key to
> be non-extractable.

## Security notes

- **Issuer seed & URL signing key are secrets** — set real values (or `existingSecret`). Do not keep
  the placeholder `security.urlSigningKey`.
- **Release images seed no users.** The default `Admin`/`User` accounts are created only under the
  `Debug` config (`#if !RELEASE`). For a production `Release` image, provision the first admin via
  your own onboarding/job. For evaluation, build with `BUILD_CONFIGURATION=Debug`.
- `ASPNETCORE_ENVIRONMENT=Production` disables the dev-only `/admin/*` trigger endpoints.

## HashiCorp Vault (optional) — native, end-to-end

The application reads the issuer seed **directly from Vault** (KV v2) at startup via the built-in
`HashiCorpVaultSecretStore`. Enable it with `vault.app.enabled=true`.

- `vault.enabled=true` optionally deploys the HashiCorp Vault subchart (dev mode = evaluation only).
  You can instead point `vault.app.address` at any existing/external Vault.
- Auth:
  - **Dev:** `vault.app.token` (static token).
  - **Production:** leave the token empty and set `vault.app.kubernetesRole` — the app exchanges the
    pod's ServiceAccount JWT for a Vault token via the Kubernetes auth method (mount
    `vault.app.kubernetesAuthMount`).
- The seed is read from `{kvMount}/data/{secretPath}`, field `seedField` (default `secret/data/dataspace-operator/issuer`, field `seed`).

Write the seed into Vault, e.g.:

```bash
vault kv put secret/dataspace-operator/issuer seed="$(openssl rand -base64 32)"
```

When `vault.app.enabled=true`, the chart injects `Vault__*` env vars and stops mounting the seed from
the Kubernetes Secret. Configure a Vault policy + Kubernetes auth role bound to this release's
ServiceAccount (out of chart scope). The **Kubernetes Secret** path (default, `vault.app.enabled=false`)
remains fully supported.
