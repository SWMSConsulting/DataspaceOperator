using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataspaceOperator.Core.Secrets;

/// <summary>Shared Vault connection + auth settings (token or Kubernetes auth).</summary>
public sealed record VaultConnection
{
    public string Address { get; init; } = "http://127.0.0.1:8200";
    public string? Token { get; init; }
    public string? KubernetesRole { get; init; }
    public string KubernetesAuthMount { get; init; } = "kubernetes";
    public string ServiceAccountTokenPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public string? Namespace { get; init; }

    public string BaseUrl => Address.TrimEnd('/');
}

/// <summary>Obtains a Vault token: static token, or Kubernetes auth (SA JWT -> Vault token).</summary>
public static class VaultAuth
{
    public static async Task<string> GetTokenAsync(HttpClient http, VaultConnection conn, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(conn.Token)) return conn.Token!;

        if (string.IsNullOrEmpty(conn.KubernetesRole))
            throw new InvalidOperationException("Vault: neither a static token nor a Kubernetes role is configured.");

        var jwt = (await File.ReadAllTextAsync(conn.ServiceAccountTokenPath, ct)).Trim();
        var url = $"{conn.BaseUrl}/v1/auth/{conn.KubernetesAuthMount}/login";
        var body = new JsonObject { ["role"] = conn.KubernetesRole, ["jwt"] = jwt };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddNamespace(request, conn);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vault Kubernetes login failed ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("auth").GetProperty("client_token").GetString()
            ?? throw new InvalidOperationException("Vault login response had no client_token.");
    }

    public static void AddNamespace(HttpRequestMessage request, VaultConnection conn)
    {
        if (!string.IsNullOrEmpty(conn.Namespace))
            request.Headers.TryAddWithoutValidation("X-Vault-Namespace", conn.Namespace);
    }
}
