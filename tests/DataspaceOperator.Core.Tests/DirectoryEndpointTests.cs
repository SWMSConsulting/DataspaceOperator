using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Crypto;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DataspaceOperator.Core.Tests;

/// <summary>
/// End-to-end over real HTTP: a participant proves membership with a VP and reads the BDRS
/// directory. This exercises issuance (signing), DID resolution, trust, VP verification and gzip —
/// exactly the "crown jewel" contract of the central service.
/// </summary>
public class DirectoryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DirectoryEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private const string IssuerDid = "did:web:issuer.localhost";
    private const string HolderDid = "did:web:alice.example";
    private const string Bpn = "BPNL000000000001";

    [Fact]
    public async Task Bdrs_read_with_valid_membership_vp_returns_gzip_map()
    {
        var client = _factory.CreateClient();
        var holderKey = Ed25519Key.Generate();

        // 1) operator registers the participant (BDRS mapping) + its DID doc (holder's own wallet key)
        var reg = await client.PostAsJsonAsync("/admin/participants", new
        {
            name = "Alice GmbH",
            bpn = Bpn,
            did = HolderDid,
            publicKeyJwk = holderKey.ToPublicJwk(),
        });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        // 2) operator issues a MembershipCredential to the participant
        var issue = await client.PostAsync($"/admin/participants/{Uri.EscapeDataString(HolderDid)}/issue?type=MembershipCredential", null);
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        var issueBody = JsonNode.Parse(await issue.Content.ReadAsStringAsync())!.AsObject();
        var vcJwt = (string)issueBody["credential"]!;

        // 3) participant's wallet wraps the VC into a VP signed with ITS OWN key
        var vp = VerifiableCredentials.BuildVpJwt(holderKey, HolderDid, $"{HolderDid}#key-1", [vcJwt], audience: IssuerDid);

        // 4) connector reads the BDRS directory, presenting the VP as bearer
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/directory/bpn-directory");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {vp}");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("gzip", resp.Content.Headers.ContentEncoding.ToString());

        // 5) decompress and assert the BPN -> DID mapping is present
        await using var gz = new GZipStream(await resp.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        var map = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(gz);

        Assert.NotNull(map);
        Assert.True(map!.ContainsKey(Bpn));
        Assert.Equal(HolderDid, map[Bpn]);
    }

    [Fact]
    public async Task Bdrs_read_without_bearer_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/directory/bpn-directory");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
