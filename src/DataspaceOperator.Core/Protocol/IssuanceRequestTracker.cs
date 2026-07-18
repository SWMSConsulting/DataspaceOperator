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

    public Pending Create(string holderPid, string holderDid, string credentialType)
    {
        var issuerPid = Guid.NewGuid().ToString();
        var p = new Pending(issuerPid, holderPid, holderDid, credentialType);
        _byIssuerPid[issuerPid] = p;
        return p;
    }

    public Pending? Get(string issuerPid) => _byIssuerPid.TryGetValue(issuerPid, out var p) ? p : null;
}
