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

    /// <summary>Find the Ed25519 public key for a given verification-method id (kid).</summary>
    public static Ed25519Key? GetKey(DidDocument doc, string? kid)
    {
        var vm = ResolveMethod(doc, kid, VerificationRelationship.Any, enforceRelationship: false, out _);
        if (vm?.PublicKeyJwk is null) return null;
        return Ed25519Key.FromPublicJwk(vm.PublicKeyJwk);
    }

    /// <summary>
    /// The raw public JWK for a verification-method id (kid). Unlike <see cref="GetKey"/> this keeps
    /// the original key type (OKP/EC), so callers can verify ES256 (P-256) signatures as well as
    /// EdDSA — participant wallets sign with P-256.
    /// </summary>
    /// <param name="relationship">
    /// The verification relationship DCP demands for this purpose. <see cref="VerificationRelationship.Any"/>
    /// skips the check entirely.
    /// </param>
    /// <param name="enforceRelationship">
    /// When false (the default) a missing relationship only produces <paramref name="warning"/> instead of
    /// rejecting. Existing IdentityHub-published DID documents carry an EMPTY <c>authentication</c> array,
    /// so enforcing immediately would lock out every current participant. Flip this on once the
    /// published documents declare their relationships.
    /// </param>
    public static System.Text.Json.Nodes.JsonObject? GetVerificationJwk(
        DidDocument doc, string? kid,
        VerificationRelationship relationship = VerificationRelationship.Any,
        bool enforceRelationship = false)
        => GetVerificationJwk(doc, kid, relationship, enforceRelationship, out _);

    /// <inheritdoc cref="GetVerificationJwk(DidDocument,string?,VerificationRelationship,bool)"/>
    public static System.Text.Json.Nodes.JsonObject? GetVerificationJwk(
        DidDocument doc, string? kid, VerificationRelationship relationship, bool enforceRelationship,
        out string? warning)
        => ResolveMethod(doc, kid, relationship, enforceRelationship, out warning)?.PublicKeyJwk;

    /// <summary>
    /// Resolve the verification method to verify a signature with.
    ///
    /// DCP ("Validating Self-Issued ID Tokens", step 3) is explicit and we follow it: a <c>kid</c> that
    /// matches nothing is a rejection, and so is an absent <c>kid</c> when the document holds more than
    /// one method. The previous implementation fell back to <c>VerificationMethod.FirstOrDefault()</c>
    /// in both cases, which silently verified against whichever key happened to be listed first.
    /// </summary>
    private static VerificationMethod? ResolveMethod(
        DidDocument doc, string? kid, VerificationRelationship relationship, bool enforceRelationship,
        out string? warning)
    {
        warning = null;

        VerificationMethod? vm;
        if (kid is not null)
        {
            // A kid that names no method is an error, NOT a reason to guess another key.
            vm = doc.VerificationMethod.FirstOrDefault(v => v.Id == kid);
            if (vm is null) return null;
        }
        else
        {
            // No kid: unambiguous only when the document holds exactly one method.
            if (doc.VerificationMethod.Count != 1) return null;
            vm = doc.VerificationMethod[0];
        }

        if (relationship == VerificationRelationship.Any) return vm;

        var declared = relationship switch
        {
            VerificationRelationship.Authentication => doc.Authentication,
            VerificationRelationship.AssertionMethod => doc.AssertionMethod,
            VerificationRelationship.CapabilityInvocation => doc.CapabilityInvocation,
            _ => null,
        };

        if (declared is not null && declared.Contains(vm.Id, StringComparer.Ordinal)) return vm;

        warning = declared is null || declared.Count == 0
            ? $"DID document '{doc.Id}' declares no '{relationship}' relationship at all; DCP requires it."
            : $"Key '{vm.Id}' is not listed under '{relationship}' in DID document '{doc.Id}'.";

        return enforceRelationship ? null : vm;
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
