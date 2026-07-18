using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using Xunit;

namespace DataspaceOperator.Core.Tests;

public class VpVerifierTests
{
    // Minimal in-memory doubles so the verifier can resolve DIDs and check trust without HTTP.
    private sealed class InMemResolver : IDidResolver
    {
        private readonly Dictionary<string, DidDocument> _docs = new(StringComparer.Ordinal);
        public void Add(string did, Ed25519Key key)
        {
            _docs[did] = new DidDocument
            {
                Id = did,
                VerificationMethod =
                [
                    new VerificationMethod { Id = $"{did}#key-1", Controller = did, PublicKeyJwk = key.ToPublicJwk() }
                ],
            };
        }
        public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default) =>
            Task.FromResult(_docs.GetValueOrDefault(did));
    }

    private sealed class TrustStore(params string[] trusted) : ITrustedIssuerStore
    {
        private readonly HashSet<string> _trusted = new(trusted, StringComparer.Ordinal);
        public Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrustedIssuer>>([]);
        public Task<bool> IsTrustedAsync(string issuerDid, string type, CancellationToken ct = default) =>
            Task.FromResult(_trusted.Contains(issuerDid));
        public Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Trusts an issuer only for a specific set of credential types (per-type scoping).</summary>
    private sealed class TypedTrustStore(string issuer, params string[] types) : ITrustedIssuerStore
    {
        private readonly HashSet<string> _types = new(types, StringComparer.Ordinal);
        public Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrustedIssuer>>([]);
        public Task<bool> IsTrustedAsync(string issuerDid, string type, CancellationToken ct = default) =>
            Task.FromResult(issuerDid == issuer && _types.Contains(type));
        public Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default) => Task.CompletedTask;
    }

    private const string IssuerDid = "did:web:issuer.example";
    private const string HolderDid = "did:web:alice.example";

    private static (string vp, InMemResolver resolver, Ed25519Key issuerKey, Ed25519Key holderKey) BuildMembershipVp()
    {
        var issuerKey = Ed25519Key.Generate();
        var holderKey = Ed25519Key.Generate();
        var resolver = new InMemResolver();
        resolver.Add(IssuerDid, issuerKey);
        resolver.Add(HolderDid, holderKey);

        var vc = VerifiableCredentials.IssueJwtVc(
            issuerKey, IssuerDid, $"{IssuerDid}#key-1",
            subjectDid: HolderDid,
            types: ["MembershipCredential"],
            credentialSubjectClaims: new() { ["holderIdentifier"] = "BPNL0001" },
            validity: TimeSpan.FromDays(30));

        var vp = VerifiableCredentials.BuildVpJwt(holderKey, HolderDid, $"{HolderDid}#key-1", [vc], audience: IssuerDid);
        return (vp, resolver, issuerKey, holderKey);
    }

    [Fact]
    public async Task Valid_membership_vp_is_accepted()
    {
        var (vp, resolver, _, _) = BuildMembershipVp();
        var verifier = new VpVerifier(resolver, new TrustStore(IssuerDid));

        var result = await verifier.VerifyMembershipAsync(vp);

        Assert.True(result.Success, result.Error);
        Assert.Equal(HolderDid, result.HolderDid);
        Assert.Contains(result.Credentials, c => c.Types.Contains("MembershipCredential"));
    }

    [Fact]
    public async Task Untrusted_issuer_is_rejected()
    {
        var (vp, resolver, _, _) = BuildMembershipVp();
        var verifier = new VpVerifier(resolver, new TrustStore("did:web:someone.else")); // issuer not trusted

        var result = await verifier.VerifyMembershipAsync(vp);

        Assert.False(result.Success);
        Assert.Contains("trusted issuer", result.Error);
    }

    [Fact]
    public async Task Issuer_trusted_for_presented_type_is_accepted()
    {
        var (vp, resolver, _, _) = BuildMembershipVp();
        var verifier = new VpVerifier(resolver, new TypedTrustStore(IssuerDid, "MembershipCredential"));

        Assert.True((await verifier.VerifyMembershipAsync(vp)).Success);
    }

    [Fact]
    public async Task Issuer_not_trusted_for_presented_type_is_rejected()
    {
        // issuer is trusted, but only for a DIFFERENT credential type than the one presented
        var (vp, resolver, _, _) = BuildMembershipVp();
        var verifier = new VpVerifier(resolver, new TypedTrustStore(IssuerDid, "DataExchangeGovernanceCredential"));

        var result = await verifier.VerifyMembershipAsync(vp);

        Assert.False(result.Success);
        Assert.Contains("trusted issuer", result.Error);
    }

    [Fact]
    public async Task Tampered_vp_signature_is_rejected()
    {
        var (vp, resolver, _, _) = BuildMembershipVp();
        // flip a character in the VP signature segment
        var parts = vp.Split('.');
        parts[2] = parts[2][0] == 'A' ? "B" + parts[2][1..] : "A" + parts[2][1..];
        var tampered = string.Join('.', parts);

        var verifier = new VpVerifier(resolver, new TrustStore(IssuerDid));
        var result = await verifier.VerifyMembershipAsync(tampered);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Vp_without_membership_is_rejected_for_bdrs()
    {
        var issuerKey = Ed25519Key.Generate();
        var holderKey = Ed25519Key.Generate();
        var resolver = new InMemResolver();
        resolver.Add(IssuerDid, issuerKey);
        resolver.Add(HolderDid, holderKey);

        var otherVc = VerifiableCredentials.IssueJwtVc(
            issuerKey, IssuerDid, $"{IssuerDid}#key-1", HolderDid,
            types: ["DataExchangeGovernanceCredential"],
            credentialSubjectClaims: new() { ["holderIdentifier"] = "BPNL0001" },
            validity: TimeSpan.FromDays(30));
        var vp = VerifiableCredentials.BuildVpJwt(holderKey, HolderDid, $"{HolderDid}#key-1", [otherVc], IssuerDid);

        var verifier = new VpVerifier(resolver, new TrustStore(IssuerDid));
        var result = await verifier.VerifyMembershipAsync(vp);

        Assert.False(result.Success);
        Assert.Contains("MembershipCredential", result.Error);
    }
}
