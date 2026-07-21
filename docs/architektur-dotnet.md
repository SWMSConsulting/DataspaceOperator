# .NET-Architektur: Projektübersicht

Der zentrale Dienst (`auth-windx`) besteht aus sechs .NET-Projekten. Die Trennung folgt einem
Prinzip: **framework-freier Kern**, darauf **dünne Framework-Adapter**.

## Projekte auf einen Blick

| Projekt | TFM | Rolle | README |
|---|---|---|---|
| `src/DataspaceOperator.Core` | net8.0 | Krypto, DCP/DSP-Logik, Domäne, Abstraktionen (nur BouncyCastle) | [→](../src/DataspaceOperator.Core/README.md) |
| `src/DataspaceOperator.Endpoints` | net8.0 | ASP.NET-Endpoint-Mapping + Audit-Middleware + Signer-Aufbau | [→](../src/DataspaceOperator.Endpoints/README.md) |
| `src/DataspaceOperator.ProtocolHost` | net10.0 | Standalone-Host mit In-Memory-Stores (Dev/Tests) | [→](../src/DataspaceOperator.ProtocolHost/README.md) |
| `tests/DataspaceOperator.Core.Tests` | net10.0 | xUnit-Tests für Krypto/Protokoll/Vault | [→](../tests/DataspaceOperator.Core.Tests/README.md) |
| `xaf/DataspaceOperator.Xaf.Module` | net10.0 | XAF-Geschäftsobjekte, DbContext, Updater, Aktionen | [→](../xaf/DataspaceOperator.Xaf.Module/README.md) |
| `xaf/DataspaceOperator.Xaf.Blazor.Server` | net10.0 | **Produktiv deployte App** (UI + Endpoints + Persistenz) | [→](../xaf/DataspaceOperator.Xaf.Blazor.Server/README.md) |

## Abhängigkeitsgraph

```
                         Core  (net8, framework-frei)
                        ╱  ▲  ╲
                       ╱   │   ╲
              Endpoints    │    Xaf.Module ──┐
              (net8)       │    (net10, XAF) │
                 ▲         │        ▲        │
                 │         │        │        │
        ProtocolHost   Core.Tests  │        │
        (net10)        (net10)     │        │
                 ▲                 │        │
                 └──────── Xaf.Blazor.Server ◀── referenziert Endpoints + Core + Xaf.Module
                          (net10, produktiv)
```

- **Core** hängt von nichts ab (außer BouncyCastle) und ist der einzige Ort mit Kryptografie.
- **Endpoints** macht aus Core-Diensten ASP.NET-Routen; kennt XAF/EF nicht.
- **Xaf.Module** liefert das Datenmodell + die UI; kennt ASP.NET nicht.
- **Xaf.Blazor.Server** ist der einzige Ort, der **alles** zusammenführt (die `ProtocolIntegration`
  verdrahtet Core-Ports gegen XAF-EF-Adapter und mappt die Endpoints).
- **ProtocolHost** ist eine alternative, schlanke Zusammenführung (In-Memory) für Dev/Tests.

## Zwei Kompositionswurzeln

Es gibt bewusst **zwei** Stellen, die den Core zusammenstecken — mit identischen Protokolldiensten,
aber unterschiedlicher Persistenz:

| | `ProtocolHost/Program.cs` | `Xaf.Blazor.Server/ProtocolIntegration.cs` |
|---|---|---|
| Stores | `InMemory*` | `Xaf*`-Adapter über EF-Objektraum |
| Zweck | Entwicklung, Tests | Produktion |

## Wo finde ich …?

| Frage | Ort |
|---|---|
| Wie wird ein VC signiert/verifiziert? | `Core/Crypto/{Jws,JwkVerifier,VerifiableCredentials,VpVerifier}.cs` |
| Wie läuft die DCP-Ausstellung? | `Core/Protocol/DcpIssuanceService.cs` + `Endpoints/ProtocolEndpoints.cs` (`/api/issuance/*`) |
| Wie wird BDRS geprüft? | `Endpoints/ProtocolEndpoints.cs` (B) + `Core/Crypto/VpVerifier.cs` |
| Welche Endpunkte gibt es? | `Endpoints/ProtocolEndpoints.cs` |
| Wo lebt der Audit-Trail? | `Core/Abstractions/Audit.cs`, `Endpoints/AuditMiddleware.cs`, `Xaf.Blazor.Server/ProtocolIntegration.cs` (`XafAuditStore`) |
| Datenmodell / UI | `Xaf.Module/BusinessObjects/DataspaceEntities.cs` |
| Wie wird der Issuer-Schlüssel geladen? | `Endpoints/SecretStores.cs`, `Core/Secrets/*` |
| Betrieb / Neuaufbau / Abläufe | [`dataspace-betrieb-und-aufbau.md`](dataspace-betrieb-und-aufbau.md) |

## Wichtige framework-übergreifende Verträge

- **`IIssuerSigner`** — abstrahiert den privaten Schlüssel (lokaler Seed vs. Vault-Transit).
- **`IDidResolver`** — eigene DID lokal, fremde `did:web` über HTTPS.
- **Stores** (`IParticipantStore` etc.) — In-Memory oder XAF-EF.
- **`IAuditStore`** — No-op (Host) oder XAF-persistiert.

Diese Ports sind der Grund, warum derselbe Protokollkern unverändert im Dev-Host und in der
produktiven XAF-App läuft.
