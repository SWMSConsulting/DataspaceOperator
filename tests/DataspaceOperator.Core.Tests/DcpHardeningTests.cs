using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using Xunit;

namespace DataspaceOperator.Core.Tests;

/// <summary>
/// Covers the DCP-conformance hardening: strict key resolution (no silent fallback), verification
/// relationships, and the issuer self-consistency check the verifier was missing.
/// </summary>
public class DcpHardeningTests
{
    private const string IssuerDid = "did:web:issuer.example";
    private const string HolderDid = "did:web:alice.example";
    private const string OtherDid = "did:web:evil.example";

    private static DidDocument DocWith(string did, params (string Id, Ed25519Key Key)[] methods)
    {
        return new DidDocument
        {
            Id = did,
            VerificationMethod = [.. methods.Select(m => new VerificationMethod
            {
                Id = m.Id, Controller = did, PublicKeyJwk = m.Key.ToPublicJwk(),
            })],
        };
    }

    // --- strict kid resolution -------------------------------------------------------------

    [Fact]
    public void Kid_that_matches_no_method_is_rejected_instead_of_falling_back()
    {
        var key = Ed25519Key.Generate();
        var doc = DocWith(IssuerDid, ($"{IssuerDid}#key-1", key));

        // Previously this returned key-1 regardless of what the kid named.
        var jwk = DidWebResolver.GetVerificationJwk(doc, $"{IssuerDid}#does-not-exist");

        Assert.Null(jwk);
    }

    [Fact]
    public void Missing_kid_is_rejected_when_the_document_holds_several_keys()
    {
        var doc = DocWith(IssuerDid,
            ($"{IssuerDid}#key-1", Ed25519Key.Generate()),
            ($"{IssuerDid}#key-2", Ed25519Key.Generate()));

        Assert.Null(DidWebResolver.GetVerificationJwk(doc, kid: null));
    }

    [Fact]
    public void Missing_kid_is_fine_when_the_document_holds_exactly_one_key()
    {
        var doc = DocWith(IssuerDid, ($"{IssuerDid}#key-1", Ed25519Key.Generate()));

        Assert.NotNull(DidWebResolver.GetVerificationJwk(doc, kid: null));
    }

    // --- verification relationships --------------------------------------------------------

    [Fact]
    public void Undeclared_relationship_is_tolerated_but_reported_while_lenient()
    {
        // Mirrors reality: IdentityHub publishes documents with an EMPTY authentication array.
        var doc = DocWith(HolderDid, ($"{HolderDid}#key-1", Ed25519Key.Generate()));

        var jwk = DidWebResolver.GetVerificationJwk(
            doc, $"{HolderDid}#key-1", VerificationRelationship.Authentication,
            enforceRelationship: false, out var warning);

        Assert.NotNull(jwk);
        Assert.NotNull(warning);
        Assert.Contains("Authentication", warning);
    }

    [Fact]
    public void Undeclared_relationship_is_rejected_once_enforcement_is_on()
    {
        var doc = DocWith(HolderDid, ($"{HolderDid}#key-1", Ed25519Key.Generate()));

        var jwk = DidWebResolver.GetVerificationJwk(
            doc, $"{HolderDid}#key-1", VerificationRelationship.Authentication,
            enforceRelationship: true, out var warning);

        Assert.Null(jwk);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Declared_relationship_passes_even_under_enforcement()
    {
        var doc = DocWith(HolderDid, ($"{HolderDid}#key-1", Ed25519Key.Generate()));
        doc.Authentication.Add($"{HolderDid}#key-1");

        var jwk = DidWebResolver.GetVerificationJwk(
            doc, $"{HolderDid}#key-1", VerificationRelationship.Authentication,
            enforceRelationship: true, out var warning);

        Assert.NotNull(jwk);
        Assert.Null(warning);
    }

    [Fact]
    public void Operator_did_document_declares_capability_invocation()
    {
        // DCP requires it for the Self-Issued ID Tokens we mint on every credential delivery.
        var signer = new TestSigner(Ed25519Key.Generate(), IssuerDid);
        var doc = new Protocol.DidDocumentBuilder(signer).BuildIssuerDocument();

        Assert.Contains(signer.KeyId, doc.CapabilityInvocation);
        Assert.Contains(signer.KeyId, doc.Authentication);
        Assert.Contains(signer.KeyId, doc.AssertionMethod);
    }

    // --- issuer self-consistency (DCP Presentation Validation, step 4) ----------------------

    [Fact]
    public async Task Vc_signed_under_a_kid_from_another_did_is_rejected()
    {
        var issuerKey = Ed25519Key.Generate();
        var holderKey = Ed25519Key.Generate();

        var resolver = new StubResolver();
        resolver.Add(DocWith(IssuerDid, ($"{IssuerDid}#key-1", issuerKey)));
        resolver.Add(DocWith(HolderDid, ($"{HolderDid}#key-1", holderKey)));

        // issuer claims to be IssuerDid, but the kid names a key of a different DID
        var vc = VerifiableCredentials.IssueJwtVc(
            issuerKey, IssuerDid, keyId: $"{OtherDid}#key-1",
            subjectDid: HolderDid,
            types: ["MembershipCredential"],
            credentialSubjectClaims: new() { ["holderIdentifier"] = "BPNL0001" },
            validity: TimeSpan.FromDays(30));
        var vp = VerifiableCredentials.BuildVpJwt(
            holderKey, HolderDid, $"{HolderDid}#key-1", [vc], audience: IssuerDid);

        var verifier = new VpVerifier(resolver, new AlwaysTrusted());
        var result = await verifier.VerifyMembershipAsync(vp);

        Assert.False(result.Success);
        Assert.Contains("does not belong to issuer", result.Error);
    }

    [Fact]
    public async Task Matching_kid_still_verifies()
    {
        var issuerKey = Ed25519Key.Generate();
        var holderKey = Ed25519Key.Generate();

        var resolver = new StubResolver();
        resolver.Add(DocWith(IssuerDid, ($"{IssuerDid}#key-1", issuerKey)));
        resolver.Add(DocWith(HolderDid, ($"{HolderDid}#key-1", holderKey)));

        var vc = VerifiableCredentials.IssueJwtVc(
            issuerKey, IssuerDid, keyId: $"{IssuerDid}#key-1",
            subjectDid: HolderDid,
            types: ["MembershipCredential"],
            credentialSubjectClaims: new() { ["holderIdentifier"] = "BPNL0001" },
            validity: TimeSpan.FromDays(30));
        var vp = VerifiableCredentials.BuildVpJwt(
            holderKey, HolderDid, $"{HolderDid}#key-1", [vc], audience: IssuerDid);

        var result = await new VpVerifier(resolver, new AlwaysTrusted()).VerifyMembershipAsync(vp);

        Assert.True(result.Success, result.Error);
        // The stub documents declare no relationships, so the tolerated gaps must be reported.
        Assert.NotEmpty(result.Warnings);
    }

    // --- doubles ---------------------------------------------------------------------------

    private sealed class StubResolver : IDidResolver
    {
        private readonly Dictionary<string, DidDocument> _docs = new(StringComparer.Ordinal);
        public void Add(DidDocument doc) => _docs[doc.Id] = doc;
        public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default) =>
            Task.FromResult(_docs.GetValueOrDefault(did));
    }

    private sealed class AlwaysTrusted : ITrustedIssuerStore
    {
        public Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrustedIssuer>>([]);
        public Task<bool> IsTrustedAsync(string issuerDid, string type, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestSigner(Ed25519Key key, string did) : IIssuerSigner
    {
        public string IssuerDid => did;
        public string KeyId => $"{did}#key-1";
        public System.Text.Json.Nodes.JsonObject PublicJwk => key.ToPublicJwk();
        public Task<byte[]> SignAsync(byte[] data, CancellationToken ct = default) =>
            Task.FromResult(key.Sign(data));
    }
}
