namespace DataspaceOperator.Core.Domain;

/// <summary>Lifecycle of a dataspace participant, driven by the onboarding state machine.</summary>
public enum ParticipantState
{
    Draft,
    PreconditionsChecked,
    PreconditionFailed,
    BdrsRegistered,
    HolderRegistered,
    OfferSent,
    CredentialIssued,
    Active,
    Suspended,
    Offboarded,
    Failed,
}

public enum CredentialLifecycle
{
    Requested,
    Issued,
    Suspended,
    Revoked,
    Expired,
}

/// <summary>A dataspace participant. The participant brings its own DID + wallet.</summary>
public sealed class Participant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Bpn { get; set; } = "";
    public string Did { get; set; } = "";
    /// <summary>Resolved from the participant's DID document during precondition check.</summary>
    public string? CredentialServiceUrl { get; set; }
    public ParticipantState State { get; set; } = ParticipantState.Draft;
    public DateTimeOffset? OnboardedUtc { get; set; }
}

/// <summary>A BPN &lt;-&gt; DID mapping. Served (read) to connectors via the BDRS directory endpoint.</summary>
public sealed class BpnDidEntry
{
    public string Bpn { get; set; } = "";
    public string Did { get; set; } = "";
}

/// <summary>An issuer DID whose credentials this dataspace trusts (governance list).</summary>
public sealed class TrustedIssuer
{
    public string Did { get; set; } = "";
    /// <summary>Credential types accepted from this issuer; empty means "*".</summary>
    public IReadOnlyList<string> SupportedTypes { get; set; } = [];
    public bool IsOwnIssuer { get; set; }
}

/// <summary>A credential this operator issued to a participant.</summary>
public sealed class IssuedCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HolderDid { get; set; } = "";
    public string CredentialType { get; set; } = "";
    /// <summary>The compact JWT-VC (the actual signed credential).</summary>
    public string Jwt { get; set; } = "";
    public int StatusListIndex { get; set; }
    public CredentialLifecycle Lifecycle { get; set; } = CredentialLifecycle.Issued;
    public DateTimeOffset IssuedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresUtc { get; set; }
}
