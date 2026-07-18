using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Secrets;

public sealed record VaultOptions
{
    public string Address { get; init; } = "http://127.0.0.1:8200";
    /// <summary>Static token auth. If empty, Kubernetes auth is used.</summary>
    public string? Token { get; init; }
    /// <summary>KV v2 mount (e.g. "secret").</summary>
    public string KvMount { get; init; } = "secret";
    /// <summary>Path of the secret within the KV mount (e.g. "dataspace-operator/issuer").</summary>
    public string SecretPath { get; init; } = "dataspace-operator/issuer";

    // --- Kubernetes auth (used when Token is empty) ---
    public string? KubernetesRole { get; init; }
    public string KubernetesAuthMount { get; init; } = "kubernetes";
    public string ServiceAccountTokenPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    /// <summary>Optional Vault Enterprise namespace.</summary>
    public string? Namespace { get; init; }
}

/// <summary>
/// Reads secrets from HashiCorp Vault (KV v2) over HTTP. Authenticates with a static token or via
/// the Kubernetes auth method (exchanging the pod's service-account JWT for a Vault token).
/// The requested <c>name</c> is the field within the configured secret path.
/// </summary>
public sealed class HashiCorpVaultSecretStore(HttpClient http, VaultOptions options) : ISecretStore
{
    private VaultConnection Connection => new()
    {
        Address = options.Address,
        Token = options.Token,
        KubernetesRole = options.KubernetesRole,
        KubernetesAuthMount = options.KubernetesAuthMount,
        ServiceAccountTokenPath = options.ServiceAccountTokenPath,
        Namespace = options.Namespace,
    };

    public async Task<string?> GetSecretAsync(string name, CancellationToken ct = default)
    {
        var conn = Connection;
        var token = await VaultAuth.GetTokenAsync(http, conn, ct);
        var url = $"{conn.BaseUrl}/v1/{options.KvMount}/data/{options.SecretPath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        VaultAuth.AddNamespace(request, conn);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vault read failed ({(int)response.StatusCode}) for {url}.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // KV v2 shape: { "data": { "data": { "<field>": "<value>" }, "metadata": {...} } }
        if (doc.RootElement.TryGetProperty("data", out var outer) &&
            outer.TryGetProperty("data", out var data) &&
            data.TryGetProperty(name, out var field) &&
            field.ValueKind == JsonValueKind.String)
        {
            return field.GetString();
        }
        return null;
    }
}
