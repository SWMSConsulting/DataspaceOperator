# DataspaceOperator.ProtocolHost

**Zielframework:** `net10.0` · **Referenzen:** `DataspaceOperator.Endpoints`,
`DataspaceOperator.Core` · **Typ:** eigenständige ASP.NET-Core-Minimal-API

Ein **schlanker Standalone-Host** des zentralen Protokolldienstes — ohne XAF, ohne UI, mit
**In-Memory-Stores**. Zweck: lokale Entwicklung, Tests und ein referenzieller Minimal-Aufbau des
Protokoll-Servers. Die **produktive** Instanz ist stattdessen
[`Xaf.Blazor.Server`](../../xaf/DataspaceOperator.Xaf.Blazor.Server/README.md).

## Dateien

### `Program.cs`

Baut den Host und registriert alle Protokolldienste als Singletons:
- `IIssuerSigner` (aus einem Config-Seed), `DidDocumentBuilder`, `IssuerMetadata`,
  `DcpIssuanceService`, `IssuanceRequestTracker`, `StatusListService`, `BdrsDirectoryService`,
  `VpVerifier`.
- Getypte `HttpClient`s für `ICredentialDeliveryService` und `ICredentialOfferService`.
- `CompositeDidResolver` (eigene DID lokal, alles andere via `did:web`).
- `IDidResolver`, In-Memory-Store-Implementierungen (siehe unten).

Mappt neben `MapDataspaceProtocol()` zusätzliche **Demo-/Test-Routen**:
`POST /admin/participants`, `POST /admin/participants/{did}/issue`,
`POST /wallet/{id}/credentials` + `GET …` (eine Fake-Wallet-Senke `WalletSink`).

### `InMemoryStores.cs`

Flüchtige Implementierungen der `Core`-Ports für den Standalone-Betrieb:
`InMemoryParticipantStore`, `InMemoryTrustedIssuerStore`, `InMemoryCredentialStore`,
`InMemoryStatusListStore`, `WalletSink`, `CompositeDidResolver`.

## Abgrenzung zur produktiven App

| | ProtocolHost | Xaf.Blazor.Server |
|---|---|---|
| Persistenz | In-Memory (flüchtig) | SQLite auf PVC |
| UI / Auth | keine | XAF Blazor + PermissionPolicy |
| Stores | `InMemory*` | XAF-EF-Adapter |
| Verwendung | Dev/Tests/Referenz | Cluster-Deployment |

Der Host wird u. a. von [`DataspaceOperator.Core.Tests`](../../tests/DataspaceOperator.Core.Tests/README.md)
(`WebApplicationFactory`) für Endpoint-Tests hochgefahren.
