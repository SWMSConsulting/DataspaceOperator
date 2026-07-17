using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// The BDRS directory. BPN and DID are attributes of a participant, so the directory is a
/// live projection over the participants (no separate table). Read access is authorized by a
/// MembershipCredential VP at the endpoint (see <see cref="Crypto.VpVerifier"/>).
/// </summary>
public sealed class BdrsDirectoryService(IParticipantStore participants)
{
    /// <summary>The full BPN -&gt; DID map. The endpoint gzip-compresses it, as connectors expect.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetDirectoryAsync(CancellationToken ct = default)
    {
        var list = await participants.ListAsync(ct);
        return list
            .Where(p => !string.IsNullOrEmpty(p.Bpn) && !string.IsNullOrEmpty(p.Did))
            .GroupBy(p => p.Bpn, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Did, StringComparer.Ordinal);
    }
}
