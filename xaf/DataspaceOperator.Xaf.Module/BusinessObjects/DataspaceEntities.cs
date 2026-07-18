using System.Collections.Generic;
using System.Collections.ObjectModel;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Xaf.Module.BusinessObjects;

// XAF EF Core business objects. Properties are `virtual` so EF Core change-tracking proxies raise
// notifications automatically. Keys come from BaseObject.

[DefaultClassOptions]
[System.ComponentModel.DefaultProperty(nameof(Name))]
public class ParticipantEntity : BaseObject
{
    public ParticipantEntity()
    {
        // XAF EF Core change tracking requires INotifyCollectionChanged collections.
        Credentials = new ObservableCollection<IssuedCredentialEntity>();
    }

    public virtual string? Name { get; set; }
    public virtual string? Bpn { get; set; }          // 1-1: a participant has exactly one BPN
    public virtual string? Did { get; set; }          // and one DID (its own wallet)
    public virtual string? CredentialServiceUrl { get; set; }
    public virtual ParticipantState State { get; set; } = ParticipantState.Draft;
    public virtual DateTime? OnboardedUtc { get; set; }

    // 1-n: a participant accumulates many issued credentials over time
    public virtual IList<IssuedCredentialEntity> Credentials { get; set; }
}

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
    public virtual DateTime IssuedUtc { get; set; }
    public virtual DateTime? ExpiresUtc { get; set; }
    public virtual DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.NotAttempted;
    public virtual DateTime? DeliveredUtc { get; set; }
}

// Internal state (not shown in navigation): the persisted revocation status-list bitstring.
public class StatusListStateEntity : BaseObject
{
    public virtual int NextIndex { get; set; }
    public virtual byte[]? Bits { get; set; }
}
