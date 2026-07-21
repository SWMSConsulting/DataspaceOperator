# DataspaceOperator.Core.Tests

**Zielframework:** `net10.0` · **Referenzen:** `DataspaceOperator.Core`,
`DataspaceOperator.ProtocolHost` · **Framework:** xUnit + `Microsoft.AspNetCore.Mvc.Testing`

Unit- und Integrationstests für den Krypto- und Protokollkern. 19 Tests, ausführbar mit:

```bash
dotnet test tests/DataspaceOperator.Core.Tests/DataspaceOperator.Core.Tests.csproj -c Release
```

## Testdateien

| Datei | Fokus |
|---|---|
| `CryptoTests.cs` | Ed25519-Schlüssel, Base64url, JWS-Roundtrip (Sign/Verify), JWT-VC-Ausstellung, Self-Issued-Token. |
| `VpVerifierTests.cs` | `VpVerifier`: gültige Membership-VP wird akzeptiert; nicht vertrauenswürdiger Issuer, falscher Credential-Typ und Holder-Bindung werden abgelehnt. Nutzt einen In-Memory-`ITrustedIssuerStore`. |
| `DirectoryEndpointTests.cs` | BDRS-Endpoint via `WebApplicationFactory` über den `ProtocolHost`: gültige Membership-VP → gzip-Map; ohne Bearer → `401`. |
| `VaultSecretStoreTests.cs` | `HashiCorpVaultSecretStore`: fehlendes Feld/Not-Found → `null`; Kubernetes-Auth loggt sich ein und liest dann (gegen einen gemockten Vault-HTTP-Handler). |
| `VaultTransitSignerTests.cs` | `VaultTransitSigner`: ein per Transit signiertes Credential verifiziert gegen den aus dem Vault geholten öffentlichen Schlüssel. |

## Prinzip

Die Tests laufen **ohne echte Infrastruktur**: Vault-Interaktionen werden über einen gemockten
`HttpMessageHandler` geprüft, Endpoints über die In-Memory-Variante des `ProtocolHost`. Damit ist
der sicherheitskritische Kern (Signatur, Verifikation, Trust, Vault-Anbindung) schnell und
deterministisch abgedeckt.
