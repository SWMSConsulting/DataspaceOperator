using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Xaf.Module.BusinessObjects;

// XAF EF Core business objects. They give the operator a CRUD admin UI (participants, trusted
// issuers, credentials, BPN mappings). Properties are `virtual` so EF Core change-tracking
// proxies raise notifications automatically. Keys come from BaseObject.

[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Name))]
public class ParticipantEntity : BaseObject
{
    public virtual string? Name { get; set; }
    public virtual string? Bpn { get; set; }
    public virtual string? Did { get; set; }
    public virtual string? CredentialServiceUrl { get; set; }
    public virtual ParticipantState State { get; set; } = ParticipantState.Draft;
    public virtual DateTime? OnboardedUtc { get; set; }
}

[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Bpn))]
public class BpnDidEntryEntity : BaseObject
{
    public virtual string? Bpn { get; set; }
    public virtual string? Did { get; set; }
}

[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Did))]
public class TrustedIssuerEntity : BaseObject
{
    public virtual string? Did { get; set; }
    /// <summary>Comma-separated credential types; empty means all types ("*").</summary>
    public virtual string? SupportedTypesCsv { get; set; }
    public virtual bool IsOwnIssuer { get; set; }
}

[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(CredentialType))]
public class IssuedCredentialEntity : BaseObject
{
    public virtual string? HolderDid { get; set; }
    public virtual string? CredentialType { get; set; }
    public virtual string? Jwt { get; set; }
    public virtual int StatusListIndex { get; set; }
    public virtual CredentialLifecycle Lifecycle { get; set; } = CredentialLifecycle.Issued;
    public virtual DateTime IssuedUtc { get; set; }
    public virtual DateTime? ExpiresUtc { get; set; }
}
