using System.Text;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Crypto;
using Xunit;

namespace DataspaceOperator.Core.Tests;

public class CryptoTests
{
    [Fact]
    public void Ed25519_sign_verify_roundtrip()
    {
        var key = Ed25519Key.Generate();
        var msg = Encoding.UTF8.GetBytes("hello dataspace");
        var sig = key.Sign(msg);

        Assert.True(key.Verify(msg, sig));
        Assert.False(key.Verify(Encoding.UTF8.GetBytes("tampered"), sig));
    }

    [Fact]
    public void Ed25519_public_key_roundtrips_through_jwk()
    {
        var key = Ed25519Key.Generate();
        var jwk = key.ToPublicJwk();
        var restored = Ed25519Key.FromPublicJwk(jwk);

        var msg = Encoding.UTF8.GetBytes("verify with restored public key");
        Assert.True(restored.Verify(msg, key.Sign(msg)));
    }

    [Fact]
    public void Jws_sign_then_verify()
    {
        var key = Ed25519Key.Generate();
        var header = new JsonObject { ["typ"] = "JWT", ["kid"] = "did:web:x#key-1" };
        var payload = new JsonObject { ["iss"] = "did:web:x", ["claim"] = 42 };

        var compact = Jws.Sign(header, payload, key);
        var parsed = Jws.Parse(compact);

        Assert.Equal("EdDSA", parsed.Algorithm);
        Assert.Equal("did:web:x#key-1", parsed.Kid);
        Assert.True(Jws.Verify(parsed, key));
    }

    [Fact]
    public void Jws_verify_fails_for_wrong_key()
    {
        var signer = Ed25519Key.Generate();
        var other = Ed25519Key.Generate();
        var compact = Jws.Sign(new JsonObject(), new JsonObject { ["a"] = 1 }, signer);

        Assert.False(Jws.Verify(Jws.Parse(compact), other));
    }

    [Theory]
    [InlineData("did:web:example.com", "http://example.com/.well-known/did.json")]
    [InlineData("did:web:example.com:issuer:1", "http://example.com/issuer/1/did.json")]
    public void DidWeb_to_url(string did, string expected)
    {
        Assert.Equal(expected, DidWebResolver.DidWebToUrl(did, useHttps: false));
    }
}
