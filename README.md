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
DataspaceOperator.Core.Tests  (net10) 12 Tests: Krypto-Round-Trip, VP-Verifikation, E2E-HTTP
xaf/DataspaceOperator.Xaf.*   (net8)  DevExpress XAF 26.1.3 Blazor-Admin-UI + EF-Core-Persistenz,
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

- ✅ `Core`/`Endpoints`/`ProtocolHost`/Tests: Build grün, **12/12 Tests** (inkl. vollem
  E2E über HTTP: Membership-VC ausstellen → VP → BDRS-Read liefert gzip-Map mit BPN↔DID).
- ✅ XAF-Solution (DevExpress 26.1.3): Build grün; DB angelegt + geseedet; die **laufende
  XAF-App liefert alle 4 Protokoll-Endpoints** (did.json, VCI-Metadata, StatusList,
  BDRS→401 ohne VP) aus dem XAF-ObjectSpace.

## Offene/Nächste Schritte

- Onboarding-`ViewController` (XAF-Action) auf Basis des Zustandsautomaten
  (`docs/xaf-solution-und-onboarding-de.md`).
- Vollständiger DCP-Issuance-Handshake (statt vereinfachter synchroner Ausstellung).
- Live-Capture des echten BDRS-/DCP-Wire-Traffics aus MXD zur Feinjustierung.
- Persistente Issuer-Schlüsselverwaltung (Secret Store statt appsettings-Seed).
