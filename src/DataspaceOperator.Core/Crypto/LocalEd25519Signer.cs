using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Crypto;

/// <summary>An issuer signer backed by a local Ed25519 key (seed held in memory / from a secret store).</summary>
public sealed class LocalEd25519Signer : IIssuerSigner
{
    private readonly Ed25519Key _key;

    public LocalEd25519Signer(Ed25519Key key, string issuerDid, string? keyId = null)
    {
        _key = key;
        IssuerDid = issuerDid;
        KeyId = keyId ?? $"{issuerDid}#key-1";
        PublicJwk = key.ToPublicJwk();
    }

    public string IssuerDid { get; }
    public string KeyId { get; }
    public JsonObject PublicJwk { get; }

    public Task<byte[]> SignAsync(byte[] message, CancellationToken ct = default) =>
        Task.FromResult(_key.Sign(message));

    public static LocalEd25519Signer FromKeyProvider(IIssuerKeyProvider keys) =>
        new(keys.SigningKey, keys.IssuerDid, keys.KeyId);
}
