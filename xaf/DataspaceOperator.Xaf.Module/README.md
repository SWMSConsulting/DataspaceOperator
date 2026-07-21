# DataspaceOperator.Xaf.Module

**Zielframework:** `net10.0` · **Referenz:** `DataspaceOperator.Core` ·
**Framework:** DevExpress XAF (ExpressApp) + EF Core

Das **plattformunabhängige XAF-Modul**: Geschäftsobjekte, EF-Core-`DbContext`, das Security-Modell,
der Datenbank-Updater und die Operator-Aktionen. Wird von der Blazor-App
([`Xaf.Blazor.Server`](../DataspaceOperator.Xaf.Blazor.Server/README.md)) geladen. UI wird von XAF
aus den Geschäftsobjekten generiert.

## Dateien

### `BusinessObjects/DataspaceEntities.cs` — die Domäne als XAF-EF-Entities

Alle Properties `virtual` (EF-Core-Change-Tracking-Proxies). Schlüssel aus `BaseObject`.

| Entity | Bedeutung |
|---|---|
| `ParticipantEntity` | Ein Teilnehmer: `Name`, `Bpn` (1-1), `Did` (1-1), `CredentialServiceUrl`, `State`, `OnboardedUtc`. 1-n: `Credentials`, `AuditEntries`. |
| `AuditEntryEntity` | Ein Audit-Eintrag: `TimestampUtc`, `Kind`, `Method`, `Path`, `StatusCode`, `DurationMs`, `ParticipantDid`, `RequestBody`, `Detail`, Rückverweis `Participant`. |
| `IssuedCredentialEntity` | Ausgestelltes Credential (Typ, JWT, Status-Index, Lifecycle, Zeitpunkte, Delivery-Status). |
| `TrustedIssuerEntity` | Vertrauenswürdiger Aussteller: `Did`, `IsOwnIssuer`, n-m `SupportedTypes`. |
| `CredentialTypeEntity` | Credential-Typname (Pickliste). |
| `CredentialDefinitionEntity` | Datengetriebene VC-Definition: `CredentialType`, `ContextUrl`, `ClaimTemplateJson`, `ValiditySeconds`. |
| `StatusListStateEntity` | Persistenter Zustand der Widerrufs-Bitstring-Liste (`NextIndex`, `Bits`). |

### `BusinessObjects/XafDbContext.cs`

`DbContext` mit `DbSet`s für alle Entities plus die XAF-/Security-Tabellen (`ModelDifference*`,
`PermissionPolicyRole`, `ApplicationUser`, `ApplicationUserLoginInfo`).

### `BusinessObjects/ApplicationUser.cs` / `ApplicationUserLoginInfo.cs`

Benutzer-Objekte des XAF-Security-Systems (PermissionPolicy).

### `Controllers/IssueMembershipController.cs`

`ViewController` mit der `SimpleAction` **„Issue Membership Credential"** auf `ParticipantEntity`.
Schickt über `ICredentialOfferService.SendOfferAsync` ein Credential-Offer an die Wallet des
Teilnehmers (der IdentityHub startet daraufhin den DCP-Request). Läuft in einem `Task.Run` mit
eigenem DI-Scope, um einen Sync-over-Async-Deadlock auf dem Blazor-Circuit zu vermeiden.

### `DatabaseUpdate/Updater.cs`

`ModuleUpdater`. Läuft beim Start/Update idempotent:
- seedet Credential-Typen,
- **seedet den eigenen Issuer als Trusted Issuer** — die DID kommt aus `Issuer__Did`
  (Env/Config), **nicht** aus einem Platzhalter, sonst würde der Betreiber den eigenen
  Credentials nicht vertrauen.

### `Module.cs`

Registriert das Modul (`ModuleBase`), Model-Erweiterungen und Abhängigkeiten.

## Einordnung

Das Modul kennt **keine** ASP.NET-Pipeline. Die Verdrahtung von Protokoll-Diensten und
Store-Adaptern passiert in `Xaf.Blazor.Server` (`ProtocolIntegration`). Die Geschäftsobjekte hier
sind zugleich das **Datenmodell** (EF Core) und die **UI-Definition** (XAF generiert Listen/Detail).
