using System.Text.Json.Nodes;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// An Ed25519 key. Holds the public key always and the private key when this key can sign.
/// Signing/verification via BouncyCastle. This is the only "raw crypto" we own — everything
/// else (JWS/VC/VP) builds on top of it.
/// </summary>
public sealed class Ed25519Key
{
    private readonly Ed25519PublicKeyParameters _public;
    private readonly Ed25519PrivateKeyParameters? _private;

    private Ed25519Key(Ed25519PublicKeyParameters pub, Ed25519PrivateKeyParameters? priv)
    {
        _public = pub;
        _private = priv;
    }

    public bool CanSign => _private is not null;

    /// <summary>Raw 32-byte public key.</summary>
    public byte[] PublicKeyBytes => _public.GetEncoded();

    /// <summary>Raw 32-byte private seed (only if CanSign). Store this in a secret store.</summary>
    public byte[] PrivateSeedBytes => _private?.GetEncoded()
        ?? throw new InvalidOperationException("This key cannot sign (no private key).");

    public static Ed25519Key Generate()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        return new Ed25519Key((Ed25519PublicKeyParameters)pair.Public, (Ed25519PrivateKeyParameters)pair.Private);
    }

    public static Ed25519Key FromPrivateSeed(byte[] seed)
    {
        if (seed.Length != 32) throw new ArgumentException("Ed25519 seed must be 32 bytes.", nameof(seed));
        var priv = new Ed25519PrivateKeyParameters(seed, 0);
        return new Ed25519Key(priv.GeneratePublicKey(), priv);
    }

    public static Ed25519Key FromPublicKey(byte[] publicKey)
    {
        if (publicKey.Length != 32) throw new ArgumentException("Ed25519 public key must be 32 bytes.", nameof(publicKey));
        return new Ed25519Key(new Ed25519PublicKeyParameters(publicKey, 0), null);
    }

    public byte[] Sign(byte[] message)
    {
        if (_private is null) throw new InvalidOperationException("This key cannot sign.");
        var signer = new Ed25519Signer();
        signer.Init(true, _private);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public bool Verify(byte[] message, byte[] signature)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, _public);
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signature);
    }

    /// <summary>Public key as an OKP/Ed25519 JWK (used in DID documents and JWS "jwk").</summary>
    public JsonObject ToPublicJwk() => new()
    {
        ["kty"] = "OKP",
        ["crv"] = "Ed25519",
        ["x"] = Base64Url.Encode(PublicKeyBytes),
    };

    public static Ed25519Key FromPublicJwk(JsonObject jwk)
    {
        var crv = (string?)jwk["crv"];
        if (crv != "Ed25519") throw new NotSupportedException($"Unsupported crv '{crv}', only Ed25519 is supported.");
        var x = (string?)jwk["x"] ?? throw new FormatException("JWK is missing 'x'.");
        return FromPublicKey(Base64Url.Decode(x));
    }
}
