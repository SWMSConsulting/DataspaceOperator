using System.IO.Compression;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// A minimal W3C Bitstring StatusList for revocation. Each issued credential gets an index;
/// revoking sets the bit. Verifiers fetch the signed StatusList credential and check the bit.
/// In-memory bitstring here — persist it in the real deployment.
/// </summary>
public sealed class StatusListService(IIssuerKeyProvider keys, string statusListUrl)
{
    private readonly object _lock = new();
    private byte[] _bits = new byte[16 * 1024]; // 128k entries
    private int _next;

    public string StatusListUrl => statusListUrl;

    public int Allocate()
    {
        lock (_lock) return _next++;
    }

    public void Revoke(int index) => Set(index, true);

    public bool IsRevoked(int index)
    {
        lock (_lock)
        {
            var (b, bit) = (index / 8, index % 8);
            return b < _bits.Length && (_bits[b] & (1 << bit)) != 0;
        }
    }

    private void Set(int index, bool value)
    {
        lock (_lock)
        {
            var (b, bit) = (index / 8, index % 8);
            if (b >= _bits.Length) Array.Resize(ref _bits, b + 1);
            if (value) _bits[b] |= (byte)(1 << bit);
            else _bits[b] &= (byte)~(1 << bit);
        }
    }

    /// <summary>A credentialStatus entry to embed into an issued VC.</summary>
    public JsonObject StatusEntryFor(int index) => new()
    {
        ["id"] = $"{statusListUrl}#{index}",
        ["type"] = "BitstringStatusListEntry",
        ["statusPurpose"] = "revocation",
        ["statusListIndex"] = index.ToString(),
        ["statusListCredential"] = statusListUrl,
    };

    /// <summary>The signed StatusList credential served at the status-list URL.</summary>
    public string BuildStatusListCredentialJwt()
    {
        byte[] snapshot;
        lock (_lock) snapshot = (byte[])_bits.Clone();

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(snapshot, 0, snapshot.Length);
        var encodedList = Base64Url.Encode(ms.ToArray());

        var subject = new JsonObject
        {
            ["type"] = "BitstringStatusList",
            ["statusPurpose"] = "revocation",
            ["encodedList"] = encodedList,
        };
        return VerifiableCredentials.IssueJwtVc(
            keys.SigningKey, keys.IssuerDid, keys.KeyId,
            subjectDid: statusListUrl,
            types: ["BitstringStatusListCredential"],
            credentialSubjectClaims: subject,
            validity: TimeSpan.FromDays(1),
            credentialId: statusListUrl);
    }
}
