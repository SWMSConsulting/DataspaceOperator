# DataspaceOperator.Endpoints

**Zielframework:** `net8.0` · **Referenz:** `DataspaceOperator.Core` ·
**Framework:** ASP.NET Core (Minimal API, Middleware)

Bindet den framework-unabhängigen [`Core`](../DataspaceOperator.Core/README.md) an ASP.NET Core.
Enthält **nur** das Mapping der Protokoll-Endpunkte, die Audit-Middleware und den Aufbau des
Issuer-Signers aus der Konfiguration. Wird sowohl vom Standalone-Host
([`ProtocolHost`](../DataspaceOperator.ProtocolHost/README.md)) als auch von der produktiven
XAF-App ([`Xaf.Blazor.Server`](../../xaf/DataspaceOperator.Xaf.Blazor.Server/README.md)) verwendet.

## Dateien

### `ProtocolEndpoints.cs`

`MapDataspaceProtocol(this IEndpointRouteBuilder)` — die **einzige** Interoperabilitäts-Oberfläche
des zentralen Dienstes. Vier Gruppen:

| Gruppe | Route(n) | Zweck |
|---|---|---|
| **(A) did:web** | `GET /.well-known/did.json` | Issuer-DID-Dokument inkl. `IssuerService`-Endpoint (Origin aus der DID abgeleitet, nicht aus dem Request — wegen Reverse-Proxy). |
| **(B) BDRS** | `GET /api/directory/bpn-directory` | BPN→DID-Verzeichnis. Erfordert `Authorization: Bearer <Membership-VP>`, geprüft über `VpVerifier.VerifyMembershipAsync`; Antwort gzip-komprimiert. |
| **(C) DCP-Issuance** | `GET /api/issuance/.well-known/vci`, `POST /api/issuance/credentials`, `GET /api/issuance/requests/{issuerPid}`, `POST /api/issuance/offer` | Issuer-Metadata; holder-initiierter Credential-Request (verifiziert das Holder-Token, gibt `201` + `issuerPid` zurück, stellt asynchron aus und liefert); Request-Status; Operator-Trigger für ein Offer. |
| **(D) Status-Liste** | `GET /status-lists/revocation` | Widerrufs-Credential; JSON-LD als Default, JWT bei `Accept: application/jwt`. |

Details:
- **`POST /api/issuance/credentials`** parst die `CredentialRequestMessage` sowohl kompakt als auch
  **expandiert** (JSON-LD; Helfer `DcpJsonLd`), verifiziert das Self-Issued-Token
  (`SelfIssuedToken.VerifyAsync`), legt einen `IssuanceRequestTracker`-Eintrag an und stößt Ausstellung
  + Zustellung in einem eigenen DI-Scope an (Fire-and-Forget).
- **`POST /api/issuance/offer`** ist durch einen Operator-API-Key (`Operator:ApiKey`, Header
  `X-Api-Key`, zeitkonstanter Vergleich) geschützt und wird **nur gemappt, wenn ein Key gesetzt
  ist** (fail closed).
- Alle Handler reichern den Audit-Trail über `HttpContext.Items` an
  (`audit.did`, `audit.kind`, `audit.detail`).

### `AuditMiddleware.cs`

`UseDataspaceAudit(this IApplicationBuilder)` — protokolliert **jeden** Aufruf gegen die
Protokoll-Pfade (`/.well-known/did.json`, `/api/directory`, `/api/issuance`, `/status-lists`).
Puffert den JSON-Body (`EnableBuffering`, gekappt bei 4096 Zeichen), misst die Dauer, ermittelt Art
(`KindFor`) und schreibt einen `AuditRecord` in den — falls registrierten — `IAuditStore`.
Konstanten `DidKey`/`KindKey`/`DetailKey` für die Anreicherung. **Auditing bricht einen
Protokollaufruf nie ab** (Fehler werden nur geloggt).

### `SecretStores.cs`

`BuildIssuerSignerAsync(config, http, issuerDid)` — baut den `IIssuerSigner` aus der Konfiguration:
Vault-Transit (Schlüssel bleibt im Vault), Vault-KV-Seed oder Config-Seed
(`Issuer:PrivateSeedBase64`). Wählt automatisch die passende Quelle.

### `ConfigurationSecretStore.cs`

`ISecretStore` über `IConfiguration` — Secrets aus Env/Config (Fallback ohne Vault).

## Einordnung

Dieses Projekt bringt **keinen** eigenen Webserver mit — es liefert Erweiterungsmethoden. Der Host
(`ProtocolHost` bzw. `Xaf.Blazor.Server`) ruft in seiner Pipeline `UseDataspaceAudit()` und in den
Endpoints `MapDataspaceProtocol()` auf und registriert die im `Core` definierten Ports.
