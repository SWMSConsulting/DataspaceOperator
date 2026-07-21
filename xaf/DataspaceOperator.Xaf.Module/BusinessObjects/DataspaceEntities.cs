using System.Collections.Generic;
using System.Collections.ObjectModel;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Xaf.Module.BusinessObjects;

// XAF EF Core business objects. Properties are `virtual` so EF Core change-tracking proxies raise
// notifications automatically. Keys come from BaseObject.

[NavigationItem("Dataspace")]
[ImageName("BO_Organization")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Name))]
public class ParticipantEntity : BaseObject
{
    public ParticipantEntity()
    {
        // XAF EF Core change tracking requires INotifyCollectionChanged collections.
        Credentials = new ObservableCollection<IssuedCredentialEntity>();
        AuditEntries = new ObservableCollection<AuditEntryEntity>();
    }

    public virtual string? Name { get; set; }
    public virtual string? Bpn { get; set; }          // 1-1: a participant has exactly one BPN
    public virtual string? Did { get; set; }          // and one DID (its own wallet)
    public virtual string? CredentialServiceUrl { get; set; }
    public virtual ParticipantState State { get; set; } = ParticipantState.Draft;

    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss}")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime? OnboardedUtc { get; set; }

    // 1-n: a participant accumulates many issued credentials over time
    public virtual IList<IssuedCredentialEntity> Credentials { get; set; }

    // 1-n: every protocol call this participant made against the central service
    public virtual IList<AuditEntryEntity> AuditEntries { get; set; }
}

/// <summary>
/// Audit trail: one entry per call against a protocol endpoint of the central service. Entries that
/// can be attributed to a participant hang off <see cref="ParticipantEntity.AuditEntries"/>;
/// unattributable calls (e.g. an anonymous DID-document read) are kept with no participant.
/// </summary>
[NavigationItem("Audit")]
[ImageName("BO_Audit_ChangeHistory")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Kind))]
public class AuditEntryEntity : BaseObject
{
    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss}")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime TimestampUtc { get; set; }
    public virtual string? Kind { get; set; }          // e.g. "DCP credential request"
    public virtual string? Method { get; set; }        // GET / POST
    public virtual string? Path { get; set; }          // incl. query string
    public virtual int StatusCode { get; set; }
    public virtual long DurationMs { get; set; }
    public virtual string? ParticipantDid { get; set; } // kept even when no participant matched
    public virtual string? RequestBody { get; set; }
    public virtual string? Detail { get; set; }

    public virtual ParticipantEntity? Participant { get; set; }
}

[NavigationItem("Governance")]
[ImageName("BO_Security_Permission")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Did))]
public class TrustedIssuerEntity : BaseObject
{
    public TrustedIssuerEntity()
    {
        SupportedTypes = new ObservableCollection<CredentialTypeEntity>();
    }

    public virtual string? Did { get; set; }
    public virtual bool IsOwnIssuer { get; set; }

    /// <summary>Credential types this issuer is trusted for (multi-select); empty = all types ("*").</summary>
    public virtual IList<CredentialTypeEntity> SupportedTypes { get; set; }
}

/// <summary>Known credential type names — the pick list for a trusted issuer's SupportedTypes.</summary>
[NavigationItem("Governance")]
[ImageName("BO_Category")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Name))]
public class CredentialTypeEntity : BaseObject
{
    public CredentialTypeEntity()
    {
        TrustedIssuers = new ObservableCollection<TrustedIssuerEntity>();
    }

    public virtual string? Name { get; set; }
    public virtual IList<TrustedIssuerEntity> TrustedIssuers { get; set; }
}

[NavigationItem("Governance")]
[ImageName("BO_Document")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(CredentialType))]
public class CredentialDefinitionEntity : BaseObject
{
    public virtual string? CredentialType { get; set; }
    /// <summary>Optional extra JSON-LD @context, e.g. https://w3id.org/catenax/credentials/v1.0.0</summary>
    public virtual string? ContextUrl { get; set; }
    /// <summary>credentialSubject template (JSON) with {bpn}/{did}/{name}/{now} placeholders.</summary>
    public virtual string? ClaimTemplateJson { get; set; }
    public virtual long ValiditySeconds { get; set; } = 31_536_000;
}

[NavigationItem("Dataspace")]
[ImageName("BO_Contract")]
[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(CredentialType))]
public class IssuedCredentialEntity : BaseObject
{
    // n-1: the holder this credential was issued to
    public virtual ParticipantEntity? Participant { get; set; }

    public virtual string? CredentialType { get; set; }
    public virtual string? Jwt { get; set; }
    public virtual int StatusListIndex { get; set; }
    public virtual CredentialLifecycle Lifecycle { get; set; } = CredentialLifecycle.Issued;

    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss}")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime IssuedUtc { get; set; }

    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss}")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime? ExpiresUtc { get; set; }

    public virtual DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.NotAttempted;

    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss}")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime? DeliveredUtc { get; set; }
}

// Internal state (not shown in navigation): the persisted revocation status-list bitstring.
public class StatusListStateEntity : BaseObject
{
    public virtual int NextIndex { get; set; }
    public virtual byte[]? Bits { get; set; }
}
