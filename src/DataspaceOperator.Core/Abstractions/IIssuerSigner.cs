using System.Text.Json.Nodes;

namespace DataspaceOperator.Core.Abstractions;

/// <summary>
/// Signs on behalf of the operator's issuer identity. Abstracts WHERE the private key lives:
/// a local key (from a seed) or a remote signer where the key never leaves (e.g. Vault Transit / KMS).
/// The public part is exposed for the DID document.
/// </summary>
public interface IIssuerSigner
{
    string IssuerDid { get; }
    string KeyId { get; }
    /// <summary>Public key as an OKP/Ed25519 JWK (for the DID document verification method).</summary>
    JsonObject PublicJwk { get; }
    Task<byte[]> SignAsync(byte[] message, CancellationToken ct = default);
}
