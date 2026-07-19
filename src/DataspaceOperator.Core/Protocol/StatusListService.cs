using System.IO.Compression;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// W3C Bitstring StatusList for revocation, backed by an <see cref="IStatusListStore"/> so that
/// allocations and revocations survive restarts. Each issued credential gets an index; revoking
/// sets the bit. Verifiers fetch the signed StatusList credential and check the bit.
/// </summary>
public sealed class StatusListService(IIssuerSigner signer, IStatusListStore store, string statusListUrl)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string StatusListUrl => statusListUrl;

    public async Task<int> AllocateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = await store.LoadAsync(ct);
            var index = s.NextIndex++;
            await store.SaveAsync(s, ct);
            return index;
        }
        finally { _gate.Release(); }
    }

    public async Task RevokeAsync(int index, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var s = await store.LoadAsync(ct);
            SetBit(s, index, true);
            await store.SaveAsync(s, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsRevokedAsync(int index, CancellationToken ct = default)
    {
        var s = await store.LoadAsync(ct);
        var (b, bit) = (index / 8, index % 8);
        return b < s.Bits.Length && (s.Bits[b] & (1 << bit)) != 0;
    }

    /// <summary>A credentialStatus entry to embed into an issued VC. Pure — no state.</summary>
    public JsonObject StatusEntryFor(int index) => new()
    {
        ["id"] = $"{statusListUrl}#{index}",
        ["type"] = "BitstringStatusListEntry",
        ["statusPurpose"] = "revocation",
        ["statusListIndex"] = index.ToString(),
        ["statusListCredential"] = statusListUrl,
    };

    /// <summary>The signed StatusList credential served at the status-list URL.</summary>
    public async Task<string> BuildStatusListCredentialJwtAsync(CancellationToken ct = default)
    {
        var s = await store.LoadAsync(ct);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(s.Bits, 0, s.Bits.Length);
        // W3C BitstringStatusList: encodedList is a multibase-encoded base64url value. The leading
        // 'u' is the multibase code for base64url-no-pad; verifiers (EDC) chop it off and use the
        // base64url decoder. Without it they fall back to a standard base64 decoder and choke on '-'.
        var encodedList = "u" + Base64Url.Encode(ms.ToArray());

        var subject = new JsonObject
        {
            ["type"] = "BitstringStatusList",
            ["statusPurpose"] = "revocation",
            ["encodedList"] = encodedList,
        };
        return await VerifiableCredentials.IssueJwtVcAsync(
            signer,
            subjectDid: statusListUrl,
            types: ["BitstringStatusListCredential"],
            credentialSubjectClaims: subject,
            validity: TimeSpan.FromDays(1),
            credentialId: statusListUrl,
            ct: ct);
    }

    private static void SetBit(StatusListState s, int index, bool value)
    {
        var (b, bit) = (index / 8, index % 8);
        if (b >= s.Bits.Length)
        {
            var grown = new byte[b + 1];
            Array.Copy(s.Bits, grown, s.Bits.Length);
            s.Bits = grown;
        }
        if (value) s.Bits[b] |= (byte)(1 << bit);
        else s.Bits[b] &= (byte)~(1 << bit);
    }
}
