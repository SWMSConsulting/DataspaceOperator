using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Crypto;

/// <summary>
/// Compact JWS (RFC 7515) with EdDSA/Ed25519. This is the wire format for JWT-VCs and VP-JWTs.
/// We build/verify it ourselves on top of <see cref="Ed25519Key"/> — no heavyweight JOSE stack needed.
/// </summary>
public static class Jws
{
    public sealed record Parsed(JsonObject Header, JsonObject Payload, string SigningInput, byte[] Signature)
    {
        public string? Kid => (string?)Header["kid"];
        public string? Algorithm => (string?)Header["alg"];
    }

    public static string Sign(JsonObject header, JsonObject payload, Ed25519Key key)
    {
        header["alg"] = "EdDSA";
        var h = Base64Url.Encode(header.ToJsonString());
        var p = Base64Url.Encode(payload.ToJsonString());
        var signingInput = $"{h}.{p}";
        var sig = key.Sign(Encoding.ASCII.GetBytes(signingInput));
        return $"{signingInput}.{Base64Url.Encode(sig)}";
    }

    /// <summary>Sign via an <see cref="IIssuerSigner"/> (local or remote, e.g. Vault Transit).</summary>
    public static async Task<string> SignAsync(JsonObject header, JsonObject payload, IIssuerSigner signer, CancellationToken ct = default)
    {
        header["alg"] = "EdDSA";
        header["kid"] = signer.KeyId;
        var h = Base64Url.Encode(header.ToJsonString());
        var p = Base64Url.Encode(payload.ToJsonString());
        var signingInput = $"{h}.{p}";
        var sig = await signer.SignAsync(Encoding.ASCII.GetBytes(signingInput), ct);
        return $"{signingInput}.{Base64Url.Encode(sig)}";
    }

    public static Parsed Parse(string compact)
    {
        var parts = compact.Split('.');
        if (parts.Length != 3) throw new FormatException("Not a compact JWS (expected 3 dot-separated parts).");
        var header = JsonNode.Parse(Base64Url.DecodeToString(parts[0]))!.AsObject();
        var payload = JsonNode.Parse(Base64Url.DecodeToString(parts[1]))!.AsObject();
        return new Parsed(header, payload, $"{parts[0]}.{parts[1]}", Base64Url.Decode(parts[2]));
    }

    /// <summary>Verify the signature of an already-parsed JWS against a known public key.</summary>
    public static bool Verify(Parsed jws, Ed25519Key publicKey)
    {
        if (jws.Algorithm != "EdDSA") return false;
        return publicKey.Verify(Encoding.ASCII.GetBytes(jws.SigningInput), jws.Signature);
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };
}
