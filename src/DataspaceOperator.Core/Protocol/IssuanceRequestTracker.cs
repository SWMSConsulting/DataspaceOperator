using System.Collections.Concurrent;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// Tracks holder-initiated DCP credential requests between the <c>CredentialRequestMessage</c>
/// (which returns an issuer-assigned <c>issuerPid</c>) and the asynchronous <c>CredentialMessage</c>
/// delivery + request-status polling.
///
/// In-memory: issuance is short-lived and re-triggerable, so restart-persistence isn't required.
/// </summary>
public sealed class IssuanceRequestTracker
{
    public enum RequestState { Received, Issued, Rejected }

    public sealed class Pending(string issuerPid, string holderPid, string holderDid, string credentialType)
    {
        public string IssuerPid { get; } = issuerPid;
        public string HolderPid { get; } = holderPid;
        public string HolderDid { get; } = holderDid;
        public string CredentialType { get; } = credentialType;
        public RequestState State { get; set; } = RequestState.Received;
        public string? Error { get; set; }
    }

    private readonly ConcurrentDictionary<string, Pending> _byIssuerPid = new();

    /// <summary>Credential type of the offer we last sent to a holder, keyed by holder DID.</summary>
    private readonly ConcurrentDictionary<string, (string Type, DateTimeOffset At)> _offeredType = new();

    /// <summary>How long an unanswered offer stays usable for correlating an incoming request.</summary>
    private static readonly TimeSpan OfferTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Remember which credential type we offered a holder. The tractusx IdentityHub does not echo
    /// the credential-object id back in its <c>CredentialRequestMessage</c> - it sends
    /// <c>credentials: [{"id": null}]</c> - so the request alone cannot say what was asked for.
    /// Correlating on the offer we just sent is what makes anything other than the first supported
    /// type issuable at all.
    /// </summary>
    public void RememberOffer(string holderDid, string credentialType) =>
        _offeredType[holderDid] = (credentialType, DateTimeOffset.UtcNow);

    /// <summary>Credential type most recently offered to this holder, if the offer is still fresh.</summary>
    public string? OfferedType(string holderDid) =>
        _offeredType.TryGetValue(holderDid, out var e) && DateTimeOffset.UtcNow - e.At < OfferTtl ? e.Type : null;

    public Pending Create(string holderPid, string holderDid, string credentialType)
    {
        var issuerPid = Guid.NewGuid().ToString();
        var p = new Pending(issuerPid, holderPid, holderDid, credentialType);
        _byIssuerPid[issuerPid] = p;
        return p;
    }

    public Pending? Get(string issuerPid) => _byIssuerPid.TryGetValue(issuerPid, out var p) ? p : null;
}
