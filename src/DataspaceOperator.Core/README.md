# DataspaceOperator.Core

**Zielframework:** `net8.0` · **Abhängigkeiten:** `BouncyCastle.Cryptography` (sonst nichts)

Der framework-unabhängige Kern: **Krypto**, **DCP-/DSP-Protokolllogik**, **Domänenmodell** und
**Abstraktionen**. Enthält bewusst keine ASP.NET-, EF- oder XAF-Abhängigkeiten, damit die Logik
testbar und wiederverwendbar bleibt (Endpoints-, Host- und XAF-Projekt bauen darauf auf).

## Ordnerstruktur

### `Abstractions/` — Verträge (keine Implementierung)

| Datei | Inhalt |
|---|---|
| `IIssuerSigner.cs` | `IIssuerSigner` — signiert im Namen der Issuer-Identität. Kapselt **wo** der private Schlüssel liegt (`IssuerDid`, `KeyId`, `PublicJwk`, `SignAsync`). |
| `Stores.cs` | `IDidResolver`, `IParticipantStore`, `ITrustedIssuerStore`, `ICredentialDefinitionStore`, `ICredentialStore`, `IIssuerKeyProvider`. Persistenz-/Auflösungs-Ports, im XAF-Projekt implementiert. |
| `Delivery.cs` | `ICredentialDeliveryService` + `CredentialToDeliver`, `DeliveryResult`. Zustellung eines VC an die Holder-Wallet. |
| `Offers.cs` | `ICredentialOfferService` + `OfferResult`. Issuer-initiiertes Credential-Angebot. |
| `StatusList.cs` | `IStatusListStore` + `StatusListState` (Bitstring, `NextIndex`; 16 kB = 128k Einträge, W3C-Mindestgröße). |
| `Secrets.cs` | `ISecretStore` — `GetSecretAsync(name)`. |
| `Audit.cs` | `AuditRecord`, `IAuditStore`, `NullAuditStore`. Der Audit-Trail-Port. |

### `Crypto/` — die einzige „echte" Kryptografie

| Datei | Aufgabe |
|---|---|
| `Ed25519Key.cs` | Ed25519 über BouncyCastle: `Generate`, `FromPrivateSeed`, `FromPublicJwk`, `Sign`, `Verify`. Das einzige Stück Rohkrypto — alles andere baut darauf auf. |
| `Base64Url.cs` | Base64url ohne Padding (JWS-Wire-Format). |
| `Jws.cs` | Compact JWS (RFC 7515) mit EdDSA: `Sign`, `SignAsync` (via `IIssuerSigner`), `Parse`, `Verify`. `Parsed` liefert `Header`/`Payload`/`SigningInput`/`Signature`/`Kid`/`Algorithm`. |
| `JwkVerifier.cs` | **Verify-only**, dispatcht nach JWS-`alg`: `EdDSA` **und** `Ed25519` (OKP) sowie `ES256` (EC/P-256, IEEE-P1363). Nötig, weil Teilnehmer-Wallets P-256 bzw. den nicht-standardisierten Alg-Namen `Ed25519` nutzen. |
| `SelfIssuedToken.cs` | DCP-Self-Issued-Tokens: `IssueAsync` (iss==sub, aud, nbf, exp, kid) und `VerifyAsync` (prüft iss==sub, aud, Ablauf, Signatur gegen die did:web-Schlüssel des Absenders). |
| `VerifiableCredentials.cs` | Baut JWT-VCs (`IssueJwtVc`/`IssueJwtVcAsync`) und die reine VC-JSON-Form (`BuildVcJson`) sowie `ReadVc` (`VcInfo`). |
| `VpVerifier.cs` | Verifiziert eine Verifiable Presentation: Holder-Signatur, je enthaltenem VC die Issuer-Signatur, Holder-Bindung, Trust (`ITrustedIssuerStore`), Ablauf. `VerifyMembershipAsync` prüft zusätzlich auf ein `MembershipCredential`. Nutzt `JwkVerifier` (EdDSA/Ed25519/ES256). |
| `DidWebResolver.cs` | `did:web`→DID-Dokument über HTTP(S). `DidWebToUrl`, `DidWebToOrigin` (leitet die öffentliche Origin aus der DID ab — wichtig hinter dem Reverse-Proxy), `GetKey`, `GetVerificationJwk`, `GetCredentialServiceEndpoint`. |
| `LocalEd25519Signer.cs` | `IIssuerSigner` auf Basis eines lokalen `Ed25519Key` (Schlüssel aus einem Seed). |
| `IssuerKeyProviders.cs` | `StaticIssuerKeyProvider` (`IIssuerKeyProvider`) + `IssuerKeyFactory`. |

### `Domain/` — Domänenmodell (POCOs, framework-frei)

- `Models.cs`: `Participant`, `CredentialDefinition`, `TrustedIssuer`, `IssuedCredential` + Enums
  `ParticipantState`, `CredentialLifecycle`, `DeliveryStatus`.
- `DidDocument.cs`: `DidDocument`, `VerificationMethod`, `DidService`.

### `Protocol/` — die Protokolldienste

| Datei | Aufgabe |
|---|---|
| `DcpIssuanceService.cs` | Herzstück der Ausstellung. `IssueForRequestAsync` (holder-initiiert: VC signieren + korrelierte `CredentialMessage` mit Retry zustellen), `IssueAsync` (issuer-initiierter Push, v. a. lokal), `RevokeAsync`. Baut Claims aus der `CredentialDefinition`-Vorlage. `Issuer:IncludeCredentialStatus=false` lässt `credentialStatus` weg (siehe Status-Listen-Konflikt in der Betriebsdoku). |
| `DidDocumentBuilder.cs` | Baut unser Issuer-DID-Dokument inkl. `IssuerService`-Service-Eintrag. |
| `IssuerMetadata.cs` | DCP-Issuer-Metadata (`credentialsSupported` als `CredentialObject`s, id==credentialType, Profil `vc11-sl2021/jwt`). |
| `HttpCredentialDeliveryService.cs` | `ICredentialDeliveryService`: POSTet die DCP-`CredentialMessage` (`issuerPid`/`holderPid`/`status:ISSUED`/`format:VC1_0_JWT`) an den Storage-Endpoint der Holder-Wallet, Bearer = Issuer-SI-Token. |
| `HttpCredentialOfferService.cs` | `ICredentialOfferService`: POSTet eine `CredentialOfferMessage` an den `/offers`-Endpoint der Wallet; der IdentityHub startet daraufhin selbst den Request. |
| `IssuanceRequestTracker.cs` | In-Memory-Korrelation `issuerPid ⇄ holderPid ⇄ holderDid ⇄ credentialType` über den asynchronen Zustell-/Statusabruf. |
| `StatusListService.cs` | W3C BitstringStatusList (Widerruf): `AllocateAsync`, `RevokeAsync`, `IsRevokedAsync`, `StatusEntryFor`. Serviert die Liste als **JWT** (`…JwtAsync`) **und** als **JSON-LD** (`…JsonAsync`); `encodedList` mit Multibase-`u`-Präfix (gzip+base64url). |
| `BdrsDirectoryService.cs` | Baut die BPN→DID-Map aus `IParticipantStore` (nur Einträge mit BPN **und** DID). |

### `Secrets/` — HashiCorp-Vault-Anbindung (optional)

- `HashiCorpVaultSecretStore.cs`: `ISecretStore` über Vault KV.
- `VaultAuth.cs`: Token- bzw. Kubernetes-Auth.
- `VaultTransitSigner.cs`: `IIssuerSigner`, bei dem der private Schlüssel **nie** den Vault
  verlässt (Signatur via Transit-Engine).

## Designprinzipien

- **Signieren nur mit Ed25519** (unsere Issuer-Identität); **Verifizieren** zusätzlich ES256, da
  die Gegenstellen P-256 nutzen.
- Alle asynchronen Signaturpfade laufen über `IIssuerSigner`, damit lokaler Schlüssel und
  Vault-Transit austauschbar sind.
- Keine I/O-Framework-Abhängigkeit außer `HttpClient` (per Konstruktor injiziert).

Getestet in [`DataspaceOperator.Core.Tests`](../../tests/DataspaceOperator.Core.Tests/README.md).
