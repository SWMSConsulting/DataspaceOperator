using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Crypto;

/// <summary>An issuer key provider built from an already-loaded key.</summary>
public sealed class StaticIssuerKeyProvider : IIssuerKeyProvider
{
    public StaticIssuerKeyProvider(string issuerDid, Ed25519Key signingKey, string? keyId = null)
    {
        IssuerDid = issuerDid;
        KeyId = keyId ?? $"{issuerDid}#key-1";
        SigningKey = signingKey;
    }

    public string IssuerDid { get; }
    public string KeyId { get; }
    public Ed25519Key SigningKey { get; }
}

/// <summary>
/// Builds an <see cref="IIssuerKeyProvider"/> from a secret store. The private key seed lives in the
/// vault/secret store — never in code. If no secret is found, a fresh key is generated (dev only,
/// yields an unstable DID).
/// </summary>
public static class IssuerKeyFactory
{
    /// <summary>Default secret name / configuration path of the issuer signing-key seed (base64).</summary>
    public const string DefaultSecretName = "Issuer:PrivateSeedBase64";

    public static async Task<IIssuerKeyProvider> CreateAsync(
        ISecretStore secrets, string issuerDid, string secretName = DefaultSecretName, CancellationToken ct = default)
    {
        var seed = await secrets.GetSecretAsync(secretName, ct);
        var key = string.IsNullOrEmpty(seed)
            ? Ed25519Key.Generate()
            : Ed25519Key.FromPrivateSeed(Convert.FromBase64String(seed));
        return new StaticIssuerKeyProvider(issuerDid, key);
    }
}
