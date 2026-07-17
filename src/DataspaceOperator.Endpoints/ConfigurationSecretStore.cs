using DataspaceOperator.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DataspaceOperator.Endpoints;

/// <summary>
/// Default <see cref="ISecretStore"/>: reads secrets from configuration. Because .NET configuration
/// merges environment variables (e.g. <c>Issuer__PrivateSeedBase64</c>) and user-secrets over
/// appsettings, the real key can be supplied out-of-band without committing it. Replace this with a
/// HashiCorp/Azure Key Vault implementation for production.
/// </summary>
public sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretStore
{
    public Task<string?> GetSecretAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(configuration[name]);
}
