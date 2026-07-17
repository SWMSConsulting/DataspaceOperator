using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Abstractions;

/// <summary>
/// Provides this operator's issuer signing key + identity. The XAF layer or a secret store
/// implements this; the private key never leaves the implementation.
/// </summary>
public interface IIssuerKeyProvider
{
    /// <summary>The issuer's DID, e.g. did:web:dataspace-issuer.example.</summary>
    string IssuerDid { get; }

    /// <summary>The verification method id, e.g. did:web:...#key-1.</summary>
    string KeyId { get; }

    /// <summary>The signing key (must be able to sign).</summary>
    Ed25519Key SigningKey { get; }
}

/// <summary>Resolves a DID into its DID document. Default impl: did:web over HTTP.</summary>
public interface IDidResolver
{
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);
}

public interface IParticipantStore
{
    Task<Participant?> GetByDidAsync(string did, CancellationToken ct = default);
    Task<IReadOnlyList<Participant>> ListAsync(CancellationToken ct = default);
    Task UpsertAsync(Participant participant, CancellationToken ct = default);
}

public interface ITrustedIssuerStore
{
    Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default);
    Task<bool> IsTrustedAsync(string issuerDid, string credentialType, CancellationToken ct = default);
    Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default);
}

public interface ICredentialDefinitionStore
{
    Task<CredentialDefinition?> GetByTypeAsync(string credentialType, CancellationToken ct = default);
    Task<IReadOnlyList<CredentialDefinition>> ListAsync(CancellationToken ct = default);
}

public interface ICredentialStore
{
    /// <summary>Persists the credential and returns its stored id (assigned by the store).</summary>
    Task<Guid> AddAsync(IssuedCredential credential, CancellationToken ct = default);
    Task<IssuedCredential?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<IssuedCredential>> ListByHolderAsync(string holderDid, CancellationToken ct = default);
    Task SetLifecycleAsync(Guid id, CredentialLifecycle lifecycle, CancellationToken ct = default);
}
