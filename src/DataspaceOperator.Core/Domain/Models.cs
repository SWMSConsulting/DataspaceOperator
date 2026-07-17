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

/// <summary>Whether the issued credential was delivered to the holder's own wallet (CredentialService).</summary>
public enum DeliveryStatus
{
    NotAttempted,
    Delivered,
    Failed,
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

/// <summary>
/// A template for an issuable credential type. Lets the operator add new credential types
/// (Catena-X framework/use-case/role credentials, …) without code changes: the claim template
/// is JSON with {bpn}/{did}/{name}/{now} placeholders filled from the participant at issuance.
/// </summary>
public sealed class CredentialDefinition
{
    public string CredentialType { get; set; } = "";
    /// <summary>Optional extra JSON-LD @context (e.g. https://w3id.org/catenax/credentials/v1.0.0).</summary>
    public string? ContextUrl { get; set; }
    /// <summary>credentialSubject template as JSON, with {bpn}/{did}/{name}/{now} placeholders.</summary>
    public string ClaimTemplateJson { get; set; } = "{}";
    public long ValiditySeconds { get; set; } = 31_536_000; // 1 year
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
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.NotAttempted;
    public DateTimeOffset? DeliveredUtc { get; set; }
}
