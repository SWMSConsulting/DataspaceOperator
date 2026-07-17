using System.Collections.Concurrent;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.ProtocolHost;

/// <summary>In-memory status list state (ephemeral — for the demo host only).</summary>
public sealed class InMemoryStatusListStore : IStatusListStore
{
    private readonly StatusListState _state = new();
    public Task<StatusListState> LoadAsync(CancellationToken ct = default) => Task.FromResult(_state);
    public Task SaveAsync(StatusListState state, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class InMemoryParticipantStore : IParticipantStore
{
    private readonly ConcurrentDictionary<string, Participant> _byDid = new(StringComparer.Ordinal);

    public Task<Participant?> GetByDidAsync(string did, CancellationToken ct = default) =>
        Task.FromResult(_byDid.GetValueOrDefault(did));

    public Task<IReadOnlyList<Participant>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Participant>>(_byDid.Values.ToList());

    public Task UpsertAsync(Participant participant, CancellationToken ct = default)
    {
        _byDid[participant.Did] = participant;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryTrustedIssuerStore : ITrustedIssuerStore
{
    private readonly ConcurrentDictionary<string, TrustedIssuer> _byDid = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TrustedIssuer>>(_byDid.Values.ToList());

    public Task<bool> IsTrustedAsync(string issuerDid, string credentialType, CancellationToken ct = default)
    {
        if (!_byDid.TryGetValue(issuerDid, out var ti)) return Task.FromResult(false);
        var ok = ti.SupportedTypes.Count == 0 || ti.SupportedTypes.Contains(credentialType);
        return Task.FromResult(ok);
    }

    public Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default)
    {
        _byDid[issuer.Did] = issuer;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<Guid, IssuedCredential> _byId = new();

    public Task<Guid> AddAsync(IssuedCredential credential, CancellationToken ct = default)
    {
        _byId[credential.Id] = credential;
        return Task.FromResult(credential.Id);
    }

    public Task<IssuedCredential?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<IReadOnlyList<IssuedCredential>> ListByHolderAsync(string holderDid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IssuedCredential>>(
            _byId.Values.Where(c => c.HolderDid == holderDid).ToList());

    public Task SetLifecycleAsync(Guid id, CredentialLifecycle lifecycle, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(id, out var c)) c.Lifecycle = lifecycle;
        return Task.CompletedTask;
    }
}

/// <summary>
/// DID resolver with a local registry (our own issuer DID + DIDs registered for the demo)
/// and an HTTP did:web fallback for real participant wallets.
/// </summary>
public sealed class CompositeDidResolver(DidWebResolver httpFallback) : IDidResolver
{
    private readonly ConcurrentDictionary<string, DidDocument> _local = new(StringComparer.Ordinal);

    public void Register(string did, DidDocument doc) => _local[did] = doc;

    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default) =>
        _local.TryGetValue(did, out var doc) ? Task.FromResult<DidDocument?>(doc) : httpFallback.ResolveAsync(did, ct);
}
