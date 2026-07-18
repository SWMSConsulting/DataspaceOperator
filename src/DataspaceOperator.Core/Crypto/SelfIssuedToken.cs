using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// DCP self-issued (SI) tokens. Both directions of the DCP issuance flow authenticate with a compact
/// JWS whose <c>iss == sub ==</c> the sender's DID and <c>aud ==</c> the recipient's DID.
///
/// The IdentityHub token-validation rules (reverse-engineered from eclipse-edc IdentityHub) require:
/// <list type="bullet">
///   <item>iss == sub (IssuerEqualsSubjectRule),</item>
///   <item>a present <c>nbf</c> (NotBeforeValidationRule) and <c>exp</c> (ExpirationIssuedAtValidationRule),</item>
///   <item>a <c>kid</c> header resolvable to the sender's did:web verification key,</item>
///   <item>aud == recipient DID (AudienceValidationRule).</item>
/// </list>
/// </summary>
public static class SelfIssuedToken
{
    private const long ClockSkewSeconds = 60;

    /// <summary>Mint an SI token addressed to <paramref name="audience"/> (the recipient's DID).</summary>
    public static Task<string> IssueAsync(
        IIssuerSigner signer, string audience, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = now.Add(ttl ?? TimeSpan.FromMinutes(5));
        var header = new JsonObject { ["typ"] = "JWT" };
        var payload = new JsonObject
        {
            ["iss"] = signer.IssuerDid,
            ["sub"] = signer.IssuerDid,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString(),
        };
        // Jws.SignAsync sets alg=EdDSA and kid=signer.KeyId.
        return Jws.SignAsync(header, payload, signer, ct);
    }

    public sealed record VerifiedToken(string Issuer, string Subject, string? Audience);

    /// <summary>
    /// Verify an incoming SI token: iss == sub, aud match, nbf/exp validity, and the signature against
    /// the sender's did:web key (resolved by the token <c>kid</c>). Returns null on any failure.
    /// </summary>
    public static async Task<VerifiedToken?> VerifyAsync(
        string compactJws, string expectedAudience, IDidResolver didResolver, CancellationToken ct = default)
    {
        Jws.Parsed jws;
        try { jws = Jws.Parse(compactJws); }
        catch { return null; }

        var iss = (string?)jws.Payload["iss"];
        var sub = (string?)jws.Payload["sub"];
        var aud = (string?)jws.Payload["aud"];
        if (string.IsNullOrEmpty(iss) || !string.Equals(iss, sub, StringComparison.Ordinal))
            return null;
        if (!string.IsNullOrEmpty(expectedAudience) && !string.Equals(aud, expectedAudience, StringComparison.Ordinal))
            return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = AsLong(jws.Payload["exp"]);
        if (exp is not null && now > exp.Value + ClockSkewSeconds) return null;
        var nbf = AsLong(jws.Payload["nbf"]);
        if (nbf is not null && now + ClockSkewSeconds < nbf.Value) return null;

        var doc = await didResolver.ResolveAsync(iss, ct);
        if (doc is null) return null;
        var jwk = DidWebResolver.GetVerificationJwk(doc, jws.Kid);
        if (jwk is null || !JwkVerifier.Verify(jwk, jws.Algorithm, jws.SigningInput, jws.Signature))
            return null;

        return new VerifiedToken(iss, sub!, aud);
    }

    private static long? AsLong(JsonNode? node)
    {
        if (node is null) return null;
        try { return node.GetValue<long>(); }
        catch
        {
            try { return (long)node.GetValue<double>(); }
            catch { return null; }
        }
    }
}
