# DataspaceOperator

Zentraler Datenraum-Dienst („echter Betreiber") als **eigener Dienst** — er stellt
**ausschließlich die definierten Datenraum-Protokoll-Endpoints** bereit, mit eigener
Logik/Persistenz und eigener Krypto (JWT-VC via .NET-Bibliotheken). **Keine EDC-Engine,
keine `v1alpha`-Admin-APIs.** Teilnehmer bringen ihre eigene Wallet + `did:web` mit.

Grundlage: die Konzept-Dokumente in `tractusx-edc/docs/`
(`konzept-eigener-dienst-protokoll-endpoints-de.md` u. a.).

## Architektur

```
DataspaceOperator.Core        (net8)  Krypto + Protokoll + Domäne — FRAMEWORK-UNABHÄNGIG, kein DevExpress
DataspaceOperator.Endpoints   (net8)  ASP.NET-Core Minimal-API-Mapper der 4 Protokoll-Endpoints
DataspaceOperator.ProtocolHost(net10) lauffähiger Kestrel-Host mit In-Memory-Stores (Demo/Tests)
DataspaceOperator.Core.Tests  (net10) 19 Tests: Krypto-Round-Trip, VP-Verifikation, E2E-HTTP
xaf/DataspaceOperator.Xaf.*   (net10)  DevExpress XAF 26.1.3 Blazor-Admin-UI + EF-Core-Persistenz,
                                      hostet dieselben Protokoll-Endpoints (Stores über XAF-ObjectSpace)
```

Die harte Arbeit (Krypto/Protokoll) liegt in **`Core`** und ist DevExpress-frei und
voll getestet. XAF ist die **Betreiber-Admin-UI + Persistenz** und hostet denselben
Core.

## Die 4 Protokoll-Endpoints (der Interop-Vertrag)

| Endpoint | Zweck | Quelle des Vertrags |
|---|---|---|
| `GET /.well-known/did.json` | Issuer-DID-Dokument (öffentl. Schlüssel) | W3C did:web |
| `GET /api/directory/bpn-directory` | BPN↔DID-Verzeichnis, **Auth via Membership-VP**, gzip | tractusx-Connector (`BdrsClientImpl`) |
| `GET /api/issuance/.well-known/vci` + `POST /api/issuance/credentials` | DCP-Issuance (discovery-basiert) | DCP-Spec |
| `GET /status-lists/revocation` | signierte StatusList-VC (Revocation) | W3C StatusList |

## Krypto (bei uns, per Library)

- **Ed25519** (BouncyCastle) → `Ed25519Key`
- **Compact JWS/EdDSA** → `Jws`
- **JWT-VC + VP** (VC-DM 1.1) → `VerifiableCredentials`
- **did:web-Resolver** → `DidWebResolver`
- **VP-Verifikation** (Holder-Sig → VC-Sig → Trusted-Issuer → Expiry) → `VpVerifier`

## Bauen & Testen (DevExpress-frei)

```bash
dotnet build DataspaceOperator.slnx
dotnet test tests/DataspaceOperator.Core.Tests   # 12/12 grün
```

Lauffähiger Demo-Host (In-Memory):
```bash
dotnet run --project src/DataspaceOperator.ProtocolHost
# GET /.well-known/did.json | /api/issuance/.well-known/vci | /status-lists/revocation
# POST /admin/participants  +  POST /admin/participants/{did}/issue   (Demo-Admin)
```

## XAF-Anwendung (DevExpress 26.1.3)

Erzeugt via DevExpress CLI-Templates (`dotnet new dx.xaf -p Blazor -orm EFCore -db Sqlite`).
DevExpress-Pakete kommen von **nuget.org** (v25.1+), Lizenzschlüssel muss registriert sein.

```bash
cd xaf/DataspaceOperator.Xaf.Blazor.Server
dotnet build ../DataspaceOperator.Xaf.sln

# DB-Schema anlegen + Governance-Seed (eigener Trusted Issuer):
dotnet run -- --updateDatabase --silent --forceUpdate

# App starten (Admin-UI + Protokoll-Endpoints):
dotnet run
```

> **Runtime-Hinweis:** Das XAF-Template zielt auf **net8.0**. Ist nur die
> **.NET-10-Runtime** installiert, per Roll-Forward starten:
> `DOTNET_ROLL_FORWARD=Major dotnet bin/Debug/net8.0/DataspaceOperator.Xaf.Blazor.Server.dll …`

Business-Objects im Admin-UI: **ParticipantEntity, BpnDidEntryEntity,
TrustedIssuerEntity, IssuedCredentialEntity**. Der Issuer-Schlüssel/-DID kommt aus
`appsettings.json` (`Issuer:Did`, `Issuer:PrivateSeedBase64`).

## Verifikationsstand

- ✅ `Core`/`Endpoints`/`ProtocolHost`/Tests: Build grün, **19/19 Tests** (inkl. vollem
  E2E über HTTP: Membership-VC ausstellen → VP → BDRS-Read liefert gzip-Map mit BPN↔DID).
- ✅ XAF-Solution (DevExpress 26.1.3): Build grün; DB angelegt + geseedet; die **laufende
  XAF-App liefert alle 4 Protokoll-Endpoints** (did.json, VCI-Metadata, StatusList,
  BDRS→401 ohne VP) aus dem XAF-ObjectSpace.

## Technische Dokumentation

- **Gesamtsetup Alice & Bob + DCP-Protokoll (mit Mermaid-Sequenzdiagrammen):** [`docs/gesamtsetup-alice-bob-dcp.md`](docs/gesamtsetup-alice-bob-dcp.md)
- **Projektübersicht & Abhängigkeitsgraph:** [`docs/architektur-dotnet.md`](docs/architektur-dotnet.md)
- **Pro Projekt** (Typen, Verantwortlichkeiten, Einordnung):
  - [`src/DataspaceOperator.Core`](src/DataspaceOperator.Core/README.md)
  - [`src/DataspaceOperator.Endpoints`](src/DataspaceOperator.Endpoints/README.md)
  - [`src/DataspaceOperator.ProtocolHost`](src/DataspaceOperator.ProtocolHost/README.md)
  - [`tests/DataspaceOperator.Core.Tests`](tests/DataspaceOperator.Core.Tests/README.md)
  - [`xaf/DataspaceOperator.Xaf.Module`](xaf/DataspaceOperator.Xaf.Module/README.md)
  - [`xaf/DataspaceOperator.Xaf.Blazor.Server`](xaf/DataspaceOperator.Xaf.Blazor.Server/README.md)
- **Betrieb, Neuaufbau, Abläufe (einfache Sprache):** [`docs/dataspace-betrieb-und-aufbau.md`](docs/dataspace-betrieb-und-aufbau.md)
- **Deploy-Reihenfolge + Teilnehmer-Manifeste:** [`docs/DEPLOY.md`](docs/DEPLOY.md), [`deploy/participants/`](deploy/participants/)

## Erreichter Stand (live verifiziert)

- **Echte holder-initiierte DCP-Ausstellung** in tractusx-IdentityHub-Wallets (Offer → Request → Deliver).
- **Dezentraler Aufbau:** je Teilnehmer eigener Stack (Vault + Postgres + IdentityHub + Connector);
  die Zentrale ist ausschließlich Issuer + BDRS und hält **keine** Teilnehmer-Secrets.
- **Alice→Bob-Dateiaustausch** über das öffentliche Internet (Katalog → Negotiation → Transfer → Pull).
- **Audit-Trail** über alle Protokoll-Aufrufe (1-n am Teilnehmer).
- **Härtung:** Offer-Endpoint per Operator-API-Key geschützt; alle Default-Credentials rotiert.

## Bewusst offen

- Widerruf deaktiviert (`Issuer:IncludeCredentialStatus=false`): IdentityHub erwartet die
  Status-Liste als JWT, EDC als JSON — gleiche URL, ununterscheidbare Requests.
- Teilnehmer-Vault im Dev-Modus (flüchtig); der **zentrale** Dienst ist davon nicht betroffen
  (SQLite auf PVC + Issuer-Schlüssel im K8s-Secret).
