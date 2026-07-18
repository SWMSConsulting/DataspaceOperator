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
    public async Task<string?> GetSecretAsync(string name, CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        var url = $"{options.Address.TrimEnd('/')}/v1/{options.KvMount}/data/{options.SecretPath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        AddNamespace(request);

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

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(options.Token)) return options.Token!;

        if (string.IsNullOrEmpty(options.KubernetesRole))
            throw new InvalidOperationException("Vault: neither a static token nor a Kubernetes role is configured.");

        var jwt = (await File.ReadAllTextAsync(options.ServiceAccountTokenPath, ct)).Trim();
        var loginUrl = $"{options.Address.TrimEnd('/')}/v1/auth/{options.KubernetesAuthMount}/login";
        var body = new JsonObject { ["role"] = options.KubernetesRole, ["jwt"] = jwt };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddNamespace(request);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vault Kubernetes login failed ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("auth").GetProperty("client_token").GetString()
            ?? throw new InvalidOperationException("Vault login response had no client_token.");
    }

    private void AddNamespace(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(options.Namespace))
            request.Headers.TryAddWithoutValidation("X-Vault-Namespace", options.Namespace);
    }
}
