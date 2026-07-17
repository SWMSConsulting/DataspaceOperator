using System.Text.Json.Nodes;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// Builds W3C Verifiable Credentials / Presentations in JWT form (VC-DM 1.1, "VC1_0_JWT" style).
/// We deliberately use JWT-VC (not JSON-LD/LD-Proofs) — far simpler to sign and verify correctly.
/// </summary>
public static class VerifiableCredentials
{
    /// <summary>Issue a signed JWT-VC. Returns the compact JWS.</summary>
    public static string IssueJwtVc(
        Ed25519Key issuerKey,
        string issuerDid,
        string keyId,
        string subjectDid,
        IReadOnlyList<string> types,
        JsonObject credentialSubjectClaims,
        TimeSpan validity,
        JsonObject? credentialStatus = null,
        string? credentialId = null,
        IReadOnlyList<string>? additionalContexts = null)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = now.Add(validity);
        credentialId ??= $"urn:uuid:{Guid.NewGuid()}";

        var subject = new JsonObject { ["id"] = subjectDid };
        foreach (var kvp in credentialSubjectClaims)
            subject[kvp.Key] = kvp.Value?.DeepClone();

        var vcTypes = new JsonArray { "VerifiableCredential" };
        foreach (var t in types) if (t != "VerifiableCredential") vcTypes.Add(t);

        var context = new JsonArray { "https://www.w3.org/2018/credentials/v1" };
        if (additionalContexts is not null)
            foreach (var c in additionalContexts) if (!string.IsNullOrWhiteSpace(c)) context.Add(c);

        var vc = new JsonObject
        {
            ["@context"] = context,
            ["id"] = credentialId,
            ["type"] = vcTypes,
            ["issuer"] = issuerDid,
            ["issuanceDate"] = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["expirationDate"] = exp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["credentialSubject"] = subject,
        };
        if (credentialStatus is not null) vc["credentialStatus"] = credentialStatus;

        var header = new JsonObject { ["typ"] = "JWT", ["kid"] = keyId };
        var payload = new JsonObject
        {
            ["iss"] = issuerDid,
            ["sub"] = subjectDid,
            ["jti"] = credentialId,
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds(),
            ["vc"] = vc,
        };
        return Jws.Sign(header, payload, issuerKey);
    }

    /// <summary>Wrap one or more VC-JWTs into a signed Verifiable Presentation (VP-JWT).</summary>
    public static string BuildVpJwt(
        Ed25519Key holderKey,
        string holderDid,
        string keyId,
        IReadOnlyList<string> vcJwts,
        string audience)
    {
        var now = DateTimeOffset.UtcNow;
        var vcArray = new JsonArray();
        foreach (var vc in vcJwts) vcArray.Add(vc);

        var vp = new JsonObject
        {
            ["@context"] = new JsonArray { "https://www.w3.org/2018/credentials/v1" },
            ["type"] = new JsonArray { "VerifiablePresentation" },
            ["holder"] = holderDid,
            ["verifiableCredential"] = vcArray,
        };

        var header = new JsonObject { ["typ"] = "JWT", ["kid"] = keyId };
        var payload = new JsonObject
        {
            ["iss"] = holderDid,
            ["sub"] = holderDid,
            ["aud"] = audience,
            ["jti"] = $"urn:uuid:{Guid.NewGuid()}",
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["vp"] = vp,
        };
        return Jws.Sign(header, payload, holderKey);
    }

    /// <summary>The parsed content of a VC we care about for verification.</summary>
    public sealed record VcInfo(string IssuerDid, string SubjectDid, IReadOnlyList<string> Types, JsonObject? CredentialStatus);

    public static VcInfo ReadVc(Jws.Parsed vcJws)
    {
        var vc = vcJws.Payload["vc"]?.AsObject()
            ?? throw new FormatException("JWT-VC payload has no 'vc' claim.");
        var issuer = (string?)vc["issuer"] ?? (string?)vcJws.Payload["iss"] ?? "";
        var subject = (string?)vc["credentialSubject"]?["id"] ?? (string?)vcJws.Payload["sub"] ?? "";
        var types = (vc["type"]?.AsArray() ?? new JsonArray())
            .Select(n => (string?)n ?? "").Where(s => s.Length > 0).ToList();
        return new VcInfo(issuer, subject, types, vc["credentialStatus"]?.AsObject());
    }
}
