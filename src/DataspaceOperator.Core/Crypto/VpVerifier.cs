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
    /// <summary>Spec deviations tolerated because strict enforcement is off. Worth logging.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static VerifiedPresentation Fail(string error) => new(false, null, [], error);
}

/// <summary>Verification strictness. Defaults are chosen so an existing dataspace keeps working.</summary>
public sealed class VpVerifierOptions
{
    /// <summary>
    /// Reject a presentation whose signing key is not declared under the required verification
    /// relationship. DCP demands this, but IdentityHub-published DID documents currently ship an
    /// EMPTY <c>authentication</c> array — turning this on before those are fixed locks everyone out.
    /// Default false: the gap is reported via <see cref="VerifiedPresentation.Warnings"/> instead.
    /// </summary>
    public bool EnforceVerificationRelationships { get; set; }
}

/// <summary>
/// Verifies a Verifiable Presentation (VP-JWT) and its contained VCs.
/// This is the crypto that protects the BDRS directory read: only a caller who can present a
/// valid MembershipCredential (issued by a trusted issuer) is authorized.
/// </summary>
public sealed class VpVerifier(
    IDidResolver didResolver,
    ITrustedIssuerStore trustedIssuers,
    VpVerifierOptions? options = null)
{
    private readonly VpVerifierOptions _options = options ?? new VpVerifierOptions();

    /// <summary>Non-fatal findings from the last verification (relationship gaps while lenient).</summary>
    private readonly List<string> _warnings = [];

    /// <summary>Full VP verification: holder signature, each VC's issuer signature, and trust.</summary>
    public async Task<VerifiedPresentation> VerifyAsync(string vpJwt, CancellationToken ct = default)
    {
        _warnings.Clear();

        Jws.Parsed vp;
        try { vp = Jws.Parse(vpJwt); }
        catch (Exception ex) { return VerifiedPresentation.Fail($"VP is not a valid JWS: {ex.Message}"); }

        var holderDid = (string?)vp.Payload["iss"];
        if (string.IsNullOrEmpty(holderDid))
            return VerifiedPresentation.Fail("VP has no 'iss' (holder DID).");

        // 1) verify the holder's signature over the VP
        var holderDoc = await didResolver.ResolveAsync(holderDid, ct);
        if (holderDoc is null) return VerifiedPresentation.Fail($"Cannot resolve holder DID '{holderDid}'.");
        // DCP Presentation Validation step 3: the VP's verification method MUST carry `authentication`.
        var holderJwk = DidWebResolver.GetVerificationJwk(
            holderDoc, vp.Kid, VerificationRelationship.Authentication,
            _options.EnforceVerificationRelationships, out var holderWarning);
        if (holderWarning is not null) _warnings.Add(holderWarning);
        if (holderJwk is null)
            return VerifiedPresentation.Fail(
                holderWarning ?? $"Holder DID document has no usable verification key for kid '{vp.Kid}'.");
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

            // 2b) DCP Presentation Validation step 4: the DID inside the VC's verification method
            //      MUST equal the VC's `issuer`. Previously unchecked — a mismatching kid merely
            //      fell through to another key instead of failing.
            if (vc.Kid is not null)
            {
                var kidDid = vc.Kid.Split('#')[0];
                if (!string.Equals(kidDid, info.IssuerDid, StringComparison.Ordinal))
                    return VerifiedPresentation.Fail(
                        $"VC verification method '{vc.Kid}' does not belong to issuer '{info.IssuerDid}'.");
            }

            // 2c) verify the issuer's signature over the VC (key must carry `assertionMethod`)
            var issuerDoc = await didResolver.ResolveAsync(info.IssuerDid, ct);
            if (issuerDoc is null) return VerifiedPresentation.Fail($"Cannot resolve issuer DID '{info.IssuerDid}'.");
            var issuerJwk = DidWebResolver.GetVerificationJwk(
                issuerDoc, vc.Kid, VerificationRelationship.AssertionMethod,
                _options.EnforceVerificationRelationships, out var issuerWarning);
            if (issuerWarning is not null) _warnings.Add(issuerWarning);
            if (issuerJwk is null)
                return VerifiedPresentation.Fail(
                    issuerWarning ?? $"Issuer DID document has no usable verification key for kid '{vc.Kid}'.");
            if (!JwkVerifier.Verify(issuerJwk, vc.Algorithm, vc.SigningInput, vc.Signature))
                return VerifiedPresentation.Fail("VC signature is invalid.");

            // 2d) trust: is this issuer trusted for this credential type?
            var isTrusted = false;
            foreach (var type in info.Types)
            {
                if (type == "VerifiableCredential") continue;
                if (await trustedIssuers.IsTrustedAsync(info.IssuerDid, type, ct)) { isTrusted = true; break; }
            }
            if (!isTrusted)
                return VerifiedPresentation.Fail($"Issuer '{info.IssuerDid}' is not a trusted issuer for the presented credential.");

            // 2e) expiry
            var exp = (long?)vc.Payload["exp"];
            if (exp is not null && DateTimeOffset.FromUnixTimeSeconds(exp.Value) < DateTimeOffset.UtcNow)
                return VerifiedPresentation.Fail("Contained VC is expired.");

            verified.Add(info);
        }

        return new VerifiedPresentation(true, holderDid, verified, null) { Warnings = [.. _warnings] };
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
