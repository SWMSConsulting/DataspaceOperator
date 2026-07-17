using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// The BDRS directory. Read access is authorized by a MembershipCredential VP (handled at the
/// endpoint via <see cref="Crypto.VpVerifier"/>); this service just holds/serves the map.
/// The write side is ours to design (operator onboarding / admin UI).
/// </summary>
public sealed class BdrsDirectoryService(IBpnDidStore store)
{
    /// <summary>The full BPN -&gt; DID map. The endpoint gzip-compresses it, as connectors expect.</summary>
    public Task<IReadOnlyDictionary<string, string>> GetDirectoryAsync(CancellationToken ct = default) =>
        store.GetDirectoryAsync(ct);

    public Task RegisterAsync(string bpn, string did, CancellationToken ct = default) =>
        store.UpsertAsync(new BpnDidEntry { Bpn = bpn, Did = did }, ct);

    public Task RemoveAsync(string bpn, CancellationToken ct = default) =>
        store.RemoveByBpnAsync(bpn, ct);
}
