using System.Net.Http.Json;
using System.Text.Json;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// Resolves did:web identifiers to DID documents over HTTP, per the did:web method:
///   did:web:example.com            -> https://example.com/.well-known/did.json
///   did:web:example.com:path:sub   -> https://example.com/path/sub/did.json
/// </summary>
public sealed class DidWebResolver(HttpClient http, bool useHttps = true) : IDidResolver
{
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        var url = DidWebToUrl(did, useHttps);
        try
        {
            return await http.GetFromJsonAsync<DidDocument>(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public static string DidWebToUrl(string did, bool useHttps = true)
    {
        const string prefix = "did:web:";
        if (!did.StartsWith(prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"Only did:web is supported, got '{did}'.");

        var rest = did[prefix.Length..];
        var segments = rest.Split(':');
        // First segment is host[%3Aport]; the rest are path segments.
        var host = Uri.UnescapeDataString(segments[0]);
        var scheme = useHttps ? "https" : "http";

        if (segments.Length == 1)
            return $"{scheme}://{host}/.well-known/did.json";

        var path = string.Join('/', segments[1..].Select(Uri.UnescapeDataString));
        return $"{scheme}://{host}/{path}/did.json";
    }

    /// <summary>Find the Ed25519 public key for a given verification-method id (kid), or the first key.</summary>
    public static Ed25519Key? GetKey(DidDocument doc, string? kid)
    {
        VerificationMethod? vm =
            (kid is not null ? doc.VerificationMethod.FirstOrDefault(v => v.Id == kid) : null)
            ?? doc.VerificationMethod.FirstOrDefault();
        if (vm?.PublicKeyJwk is null) return null;
        return Ed25519Key.FromPublicJwk(vm.PublicKeyJwk);
    }

    /// <summary>
    /// The raw public JWK for a verification-method id (kid), or the first method. Unlike
    /// <see cref="GetKey"/> this keeps the original key type (OKP/EC), so callers can verify
    /// ES256 (P-256) signatures as well as EdDSA — participant wallets sign with P-256.
    /// </summary>
    public static System.Text.Json.Nodes.JsonObject? GetVerificationJwk(DidDocument doc, string? kid)
    {
        VerificationMethod? vm =
            (kid is not null ? doc.VerificationMethod.FirstOrDefault(v => v.Id == kid) : null)
            ?? doc.VerificationMethod.FirstOrDefault();
        return vm?.PublicKeyJwk;
    }

    public static string? GetCredentialServiceEndpoint(DidDocument doc) =>
        doc.Service.FirstOrDefault(s => s.Type == "CredentialService")?.ServiceEndpoint;

    /// <summary>
    /// The public origin (scheme://host[:port]) of a did:web identifier — e.g.
    /// <c>did:web:auth-windx.cluster.swms-cloud.com</c> -> <c>https://auth-windx.cluster.swms-cloud.com</c>.
    /// Used to advertise our own protocol endpoints: behind an HTTPS reverse proxy the app never sees
    /// the public scheme/host, so we derive it from our DID instead of the request context.
    /// </summary>
    public static string DidWebToOrigin(string did, bool useHttps = true)
    {
        const string prefix = "did:web:";
        if (!did.StartsWith(prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"Only did:web is supported, got '{did}'.");
        var host = Uri.UnescapeDataString(did[prefix.Length..].Split(':')[0]);
        return $"{(useHttps ? "https" : "http")}://{host}";
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
