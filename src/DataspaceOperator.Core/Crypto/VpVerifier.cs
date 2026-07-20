using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Crypto;

/// <summary>Result of verifying a Verifiable Presentation.</summary>
public sealed record VerifiedPresentation(
    bool Success,
    string? HolderDid,
    IReadOnlyList<VerifiableCredentials.VcInfo> Credentials,
    string? Error)
{
    public static VerifiedPresentation Fail(string error) => new(false, null, [], error);
}

/// <summary>
/// Verifies a Verifiable Presentation (VP-JWT) and its contained VCs.
/// This is the crypto that protects the BDRS directory read: only a caller who can present a
/// valid MembershipCredential (issued by a trusted issuer) is authorized.
/// </summary>
public sealed class VpVerifier(IDidResolver didResolver, ITrustedIssuerStore trustedIssuers)
{
    /// <summary>Full VP verification: holder signature, each VC's issuer signature, and trust.</summary>
    public async Task<VerifiedPresentation> VerifyAsync(string vpJwt, CancellationToken ct = default)
    {
        Jws.Parsed vp;
        try { vp = Jws.Parse(vpJwt); }
        catch (Exception ex) { return VerifiedPresentation.Fail($"VP is not a valid JWS: {ex.Message}"); }

        var holderDid = (string?)vp.Payload["iss"];
        if (string.IsNullOrEmpty(holderDid))
            return VerifiedPresentation.Fail("VP has no 'iss' (holder DID).");

        // 1) verify the holder's signature over the VP
        var holderDoc = await didResolver.ResolveAsync(holderDid, ct);
        if (holderDoc is null) return VerifiedPresentation.Fail($"Cannot resolve holder DID '{holderDid}'.");
        var holderJwk = DidWebResolver.GetVerificationJwk(holderDoc, vp.Kid);
        if (holderJwk is null) return VerifiedPresentation.Fail("Holder DID document has no usable verification key.");
        if (!JwkVerifier.Verify(holderJwk, vp.Algorithm, vp.SigningInput, vp.Signature))
            return VerifiedPresentation.Fail("VP signature is invalid.");

        // 2) extract and verify each contained VC
        var vcNodes = vp.Payload["vp"]?["verifiableCredential"]?.AsArray();
        if (vcNodes is null || vcNodes.Count == 0)
            return VerifiedPresentation.Fail("VP contains no verifiableCredential.");

        var verified = new List<VerifiableCredentials.VcInfo>();
        foreach (var node in vcNodes)
        {
            var vcJwt = (string?)node;
            if (string.IsNullOrEmpty(vcJwt)) return VerifiedPresentation.Fail("VC entry is not a JWT string.");

            Jws.Parsed vc;
            try { vc = Jws.Parse(vcJwt); }
            catch (Exception ex) { return VerifiedPresentation.Fail($"Contained VC is not a valid JWS: {ex.Message}"); }

            var info = VerifiableCredentials.ReadVc(vc);

            // 2a) the VC must be about the presenter (holder-binding)
            if (!string.Equals(info.SubjectDid, holderDid, StringComparison.Ordinal))
                return VerifiedPresentation.Fail("VC subject does not match the presenting holder.");

            // 2b) verify the issuer's signature over the VC
            var issuerDoc = await didResolver.ResolveAsync(info.IssuerDid, ct);
            if (issuerDoc is null) return VerifiedPresentation.Fail($"Cannot resolve issuer DID '{info.IssuerDid}'.");
            var issuerJwk = DidWebResolver.GetVerificationJwk(issuerDoc, vc.Kid);
            if (issuerJwk is null) return VerifiedPresentation.Fail("Issuer DID document has no usable verification key.");
            if (!JwkVerifier.Verify(issuerJwk, vc.Algorithm, vc.SigningInput, vc.Signature))
                return VerifiedPresentation.Fail("VC signature is invalid.");

            // 2c) trust: is this issuer trusted for this credential type?
            var isTrusted = false;
            foreach (var type in info.Types)
            {
                if (type == "VerifiableCredential") continue;
                if (await trustedIssuers.IsTrustedAsync(info.IssuerDid, type, ct)) { isTrusted = true; break; }
            }
            if (!isTrusted)
                return VerifiedPresentation.Fail($"Issuer '{info.IssuerDid}' is not a trusted issuer for the presented credential.");

            // 2d) expiry
            var exp = (long?)vc.Payload["exp"];
            if (exp is not null && DateTimeOffset.FromUnixTimeSeconds(exp.Value) < DateTimeOffset.UtcNow)
                return VerifiedPresentation.Fail("Contained VC is expired.");

            verified.Add(info);
        }

        return new VerifiedPresentation(true, holderDid, verified, null);
    }

    /// <summary>Verify that the VP proves a valid MembershipCredential (BDRS read authorization).</summary>
    public async Task<VerifiedPresentation> VerifyMembershipAsync(string vpJwt, CancellationToken ct = default)
    {
        var result = await VerifyAsync(vpJwt, ct);
        if (!result.Success) return result;
        var hasMembership = result.Credentials.Any(c => c.Types.Contains("MembershipCredential"));
        return hasMembership
            ? result
            : VerifiedPresentation.Fail("Presentation does not contain a MembershipCredential.");
    }
}
