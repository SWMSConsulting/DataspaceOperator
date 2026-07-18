using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Secrets;

public sealed record VaultTransitOptions
{
    public VaultConnection Connection { get; init; } = new();
    public string Mount { get; init; } = "transit";
    public string KeyName { get; init; } = "dataspace-issuer";
}

/// <summary>
/// An issuer signer whose Ed25519 private key lives in HashiCorp Vault's Transit engine and never
/// leaves it — the app only asks Vault to sign. The public key is fetched once (for the DID document).
///
/// NOTE: validate the Transit response encodings (public_key / signature) against your Vault version.
/// </summary>
public sealed class VaultTransitSigner : IIssuerSigner
{
    private readonly HttpClient _http;
    private readonly VaultTransitOptions _options;

    private VaultTransitSigner(HttpClient http, VaultTransitOptions options, string issuerDid, string keyId, JsonObject publicJwk)
    {
        _http = http;
        _options = options;
        IssuerDid = issuerDid;
        KeyId = keyId;
        PublicJwk = publicJwk;
    }

    public string IssuerDid { get; }
    public string KeyId { get; }
    public JsonObject PublicJwk { get; }

    /// <summary>Creates the signer, fetching the Transit public key up front.</summary>
    public static async Task<VaultTransitSigner> CreateAsync(
        HttpClient http, VaultTransitOptions options, string issuerDid, string? keyId = null, CancellationToken ct = default)
    {
        var conn = options.Connection;
        var token = await VaultAuth.GetTokenAsync(http, conn, ct);

        var url = $"{conn.BaseUrl}/v1/{options.Mount}/keys/{options.KeyName}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        VaultAuth.AddNamespace(request, conn);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vault Transit key read failed ({(int)response.StatusCode}) for {url}.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // data.keys is a map keyed by version; take the highest version's public_key.
        var keys = doc.RootElement.GetProperty("data").GetProperty("keys");
        string? publicKeyB64 = null;
        var maxVersion = -1;
        foreach (var kv in keys.EnumerateObject())
        {
            if (int.TryParse(kv.Name, out var v) && v > maxVersion &&
                kv.Value.TryGetProperty("public_key", out var pk) && pk.ValueKind == JsonValueKind.String)
            {
                maxVersion = v;
                publicKeyB64 = pk.GetString();
            }
        }
        if (publicKeyB64 is null) throw new InvalidOperationException("Vault Transit key has no public_key.");

        var raw = Convert.FromBase64String(publicKeyB64);
        // ed25519 raw is 32 bytes; SubjectPublicKeyInfo DER is 44 bytes (12-byte prefix + 32-byte key).
        if (raw.Length == 44) raw = raw[^32..];
        if (raw.Length != 32) throw new InvalidOperationException($"Unexpected Transit public key length: {raw.Length}.");

        var publicJwk = new JsonObject
        {
            ["kty"] = "OKP",
            ["crv"] = "Ed25519",
            ["x"] = Base64Url.Encode(raw),
        };
        return new VaultTransitSigner(http, options, issuerDid, keyId ?? $"{issuerDid}#key-1", publicJwk);
    }

    public async Task<byte[]> SignAsync(byte[] message, CancellationToken ct = default)
    {
        var conn = _options.Connection;
        var token = await VaultAuth.GetTokenAsync(_http, conn, ct);

        var url = $"{conn.BaseUrl}/v1/{_options.Mount}/sign/{_options.KeyName}";
        var body = new JsonObject { ["input"] = Convert.ToBase64String(message) };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Vault-Token", token);
        VaultAuth.AddNamespace(request, conn);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vault Transit sign failed ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var signature = doc.RootElement.GetProperty("data").GetProperty("signature").GetString()
            ?? throw new InvalidOperationException("Vault Transit sign response had no signature.");

        // format: "vault:v1:<base64 signature>"
        var lastColon = signature.LastIndexOf(':');
        var b64 = lastColon >= 0 ? signature[(lastColon + 1)..] : signature;
        return Convert.FromBase64String(b64);
    }
}
