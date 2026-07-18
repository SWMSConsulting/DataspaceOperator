using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Secrets;
using Xunit;

namespace DataspaceOperator.Core.Tests;

/// <summary>
/// Verifies the Vault Transit signer against a fake Transit engine that performs REAL Ed25519
/// signing with a key that never leaves the fake server — so a valid, verifiable JWS proves the
/// request/response handling (public_key fetch + "vault:v1:" signature) is correct.
/// </summary>
public class VaultTransitSignerTests
{
    private sealed class FakeTransit(Ed25519Key key) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/transit/keys/"))
            {
                var pub = Convert.ToBase64String(key.PublicKeyBytes);
                return Ok("{\"data\":{\"keys\":{\"1\":{\"public_key\":\"" + pub + "\"}}}}");
            }
            if (request.Method == HttpMethod.Post && path.Contains("/transit/sign/"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                var input = Convert.FromBase64String((string)body["input"]!);
                var sig = Convert.ToBase64String(key.Sign(input));
                return Ok("{\"data\":{\"signature\":\"vault:v1:" + sig + "\"}}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Ok(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task Transit_signed_credential_verifies_against_fetched_public_key()
    {
        var vaultKey = Ed25519Key.Generate();               // the "key that never leaves Vault"
        var http = new HttpClient(new FakeTransit(vaultKey));
        var signer = await VaultTransitSigner.CreateAsync(
            http,
            new VaultTransitOptions { Connection = new VaultConnection { Address = "http://vault:8200", Token = "t" } },
            issuerDid: "did:web:issuer.example");

        // public JWK fetched from Transit must equal the actual public key
        Assert.Equal(Base64Url.Encode(vaultKey.PublicKeyBytes), (string)signer.PublicJwk["x"]!);

        var vc = await VerifiableCredentials.IssueJwtVcAsync(
            signer, subjectDid: "did:web:alice",
            types: ["MembershipCredential"],
            credentialSubjectClaims: new() { ["holderIdentifier"] = "BPNL0001" },
            validity: TimeSpan.FromDays(30));

        var parsed = Jws.Parse(vc);
        Assert.Equal("EdDSA", parsed.Algorithm);
        Assert.Equal("did:web:issuer.example#key-1", parsed.Kid);
        // the Transit-produced signature verifies with the fetched public key
        Assert.True(Jws.Verify(parsed, Ed25519Key.FromPublicJwk(signer.PublicJwk)));
    }
}
