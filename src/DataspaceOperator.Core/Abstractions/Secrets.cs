namespace DataspaceOperator.Core.Abstractions;

/// <summary>
/// Source of secrets (issuer signing key seed, …). The seam for a real vault: the default
/// implementation reads configuration (env vars / user-secrets / appsettings); swap it for a
/// HashiCorp Vault / Azure Key Vault implementation without touching the key provider.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetSecretAsync(string name, CancellationToken ct = default);
}
