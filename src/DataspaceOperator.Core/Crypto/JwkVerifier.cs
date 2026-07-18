using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// Verifies a compact-JWS signature against a public JWK, dispatching on the JWS <c>alg</c>:
/// <list type="bullet">
///   <item><c>EdDSA</c> (Ed25519 / OKP) — our own issuer identity, and</item>
///   <item><c>ES256</c> (P-256 / EC) — the key type tractusx IdentityHub uses for participant
///   self-issued tokens and presentations.</item>
/// </list>
/// Verify-only: our issuer keeps signing with Ed25519; this just lets us validate what participants send.
/// </summary>
public static class JwkVerifier
{
    public static bool Verify(JsonObject jwk, string? alg, string signingInput, byte[] signature)
    {
        var input = Encoding.ASCII.GetBytes(signingInput);
        var kty = (string?)jwk["kty"];
        return (alg, kty) switch
        {
            ("EdDSA", "OKP") => VerifyEd25519(jwk, input, signature),
            ("ES256", "EC") => VerifyEs256(jwk, input, signature),
            _ => false,
        };
    }

    private static bool VerifyEd25519(JsonObject jwk, byte[] signingInput, byte[] signature)
    {
        try { return Ed25519Key.FromPublicJwk(jwk).Verify(signingInput, signature); }
        catch { return false; }
    }

    private static bool VerifyEs256(JsonObject jwk, byte[] signingInput, byte[] signature)
    {
        var xB = (string?)jwk["x"];
        var yB = (string?)jwk["y"];
        if (xB is null || yB is null) return false;
        try
        {
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = Base64Url.Decode(xB), Y = Base64Url.Decode(yB) },
            });
            // JWS ES256 signatures are raw R||S (IEEE P1363) over SHA-256.
            return ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch { return false; }
    }
}
