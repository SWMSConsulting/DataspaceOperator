# DataspaceOperator.Xaf.Blazor.Server

**Zielframework:** `net10.0` · **Referenzen:** `DataspaceOperator.Xaf.Module`,
`DataspaceOperator.Endpoints`, `DataspaceOperator.Core` ·
**Framework:** DevExpress XAF (Blazor Server) + EF Core (SQLite)

Die **produktiv deployte Anwendung** — der zentrale Dienst. Vereint dreierlei in einem Prozess:
1. die **Admin-Oberfläche** (XAF Blazor: Teilnehmer, Trusted Issuers, Credentials, Audit-Trail),
2. die **Protokoll-Endpunkte** (`did:web`, BDRS, DCP-Issuance, Status-Liste — aus dem
   [`Endpoints`](../../src/DataspaceOperator.Endpoints/README.md)-Projekt),
3. die **Persistenz** (SQLite auf PVC) und das **Security-System** (PermissionPolicy).

## Dateien

### `Startup.cs`

Konfiguriert Services und die HTTP-Pipeline. Reihenfolge in `Configure`:
`UseHttpsRedirection → UseRouting → UseAuthentication → UseAuthorization → UseXaf →`
**`UseDataspaceAudit()`** `→ UseEndpoints(…)`. In `UseEndpoints`:
**`MapDataspaceProtocol()`** (die Protokoll-Routen), im Development zusätzlich Dev-Trigger, dann
`MapXafEndpoints`/`MapBlazorHub`/Fallback. Registriert das XAF-Security-System und ruft
`AddDataspaceProtocol()` (siehe unten).

### `ProtocolIntegration.cs` — die zentrale Verdrahtung

`AddDataspaceProtocol(this IServiceCollection, IConfiguration)` verbindet den framework-freien
`Core` mit dem XAF-Objektraum:

- **Issuer-Signer** aus Konfiguration (`SecretStores.BuildIssuerSignerAsync`: Vault-Transit /
  Vault-KV / Config-Seed).
- Protokolldienste: `DidDocumentBuilder`, `IssuerMetadata`, `StatusListService`,
  `DcpIssuanceService` (mit dem Schalter `Issuer:IncludeCredentialStatus`), `VpVerifier`,
  `BdrsDirectoryService`, `IssuanceRequestTracker`, getypte `HttpClient`s für Delivery/Offer.
- `OperatorDidResolver` — eigene DID lokal, alle anderen `did:web` **über HTTPS**.
- **Store-Adapter** über den XAF-`INonSecuredObjectSpaceFactory` (mappen `Core`-Domänentypen
  ⇄ EF-Entities): `XafParticipantStore`, `XafTrustedIssuerStore`, `XafCredentialDefinitionStore`,
  `XafCredentialStore`, `XafStatusListStore` und **`XafAuditStore`** (ordnet Audit-Einträge dem
  Teilnehmer per DID zu → 1-n am Participant).

### `Program.cs`

Einstiegspunkt. Startet den Host; die CLI-Option `--updateDatabase` (im Helm-Init-Container)
führt Schema- + Daten-Update (`Updater`) aus, bevor der Web-Prozess startet.

### `BlazorApplication.cs` / `BlazorModule.cs`

XAF-Anwendungs- und Modul-Bootstrap (registriert das `Xaf.Module`, Security, Model).

### `Services/CircuitHandlerProxy.cs`, `Services/ProxyHubConnectionHandler.cs`

XAF-Blazor-Infrastruktur (SignalR-Circuit-Handling), aus der XAF-Vorlage.

## Konfiguration (Auszug)

| Schlüssel (Env `__`) | Wirkung |
|---|---|
| `Issuer:Did` | Eigene Issuer-DID (auch Trust-Anchor im Updater). |
| `Issuer:PrivateSeedBase64` | Ed25519-Seed des Issuers (aus K8s-Secret). |
| `Issuer:IncludeCredentialStatus` | `false` lässt `credentialStatus` weg (Status-Listen-Konflikt). |
| `Operator:ApiKey` | Schützt `POST /api/issuance/offer`; ohne Wert wird die Route nicht gemappt. |
| `Vault:*` / `Vault:Transit:*` | Optionale Vault-Anbindung des Issuer-Schlüssels. |
| `Bootstrap:AdminUserName` / `Bootstrap:AdminPassword` | Erst-Admin (produktionssicher, nur bei gesetztem Passwort). |
| `ConnectionStrings:ConnectionString` | SQLite-Pfad (`/app/data`, PVC). |

## Persistenz & Betrieb

- **SQLite** unter `/app/data` auf einem `ReadWriteOnce`-PVC → genau **eine** Replica; Daten und
  Issuer-Schlüssel (K8s-Secret) überleben Rollouts. Kein Vault nötig.
- Deployment über das Helm-Chart in [`helm/dataspace-operator`](../../helm/dataspace-operator);
  Betrieb + Neuaufbau in [`docs/dataspace-betrieb-und-aufbau.md`](../../docs/dataspace-betrieb-und-aufbau.md).

## Ablaufbeispiel „Issue Membership Credential"

UI-Aktion → `IssueMembershipController` → `ICredentialOfferService` (Offer an Wallet) → IdentityHub
startet DCP-Request → `POST /api/issuance/credentials` (dieser Prozess) → `DcpIssuanceService`
stellt aus und liefert die `CredentialMessage` → Wallet speichert. Jeder dieser Aufrufe landet im
**Audit-Trail** am Teilnehmer.
