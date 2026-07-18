using System.Text;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// DCP credential delivery over HTTP: resolves the holder's CredentialService endpoint from its
/// DID document and POSTs a DCP <c>CredentialMessage</c>, authenticated with an issuer self-issued
/// token (aud = holder DID).
///
/// Body shape (matched against the eclipse-edc IdentityHub Storage API): the message MUST carry
/// <c>issuerPid</c>, <c>holderPid</c> and <c>status:"ISSUED"</c>, and each credential's
/// <c>format</c> must map to a known <c>CredentialFormat</c> (we use the enum name <c>VC1_0_JWT</c>).
/// </summary>
public sealed class HttpCredentialDeliveryService(
    IDidResolver didResolver,
    IIssuerSigner signer,
    HttpClient http) : ICredentialDeliveryService
{
    public const string Format = "VC1_0_JWT";

    public async Task<DeliveryResult> DeliverAsync(
        string holderDid, IReadOnlyList<CredentialToDeliver> credentials,
        string issuerPid, string holderPid, CancellationToken ct = default)
    {
        var doc = await didResolver.ResolveAsync(holderDid, ct);
        if (doc is null) return DeliveryResult.Fail(null, $"cannot resolve holder DID '{holderDid}'");

        var credentialService = DidWebResolver.GetCredentialServiceEndpoint(doc);
        if (string.IsNullOrEmpty(credentialService))
            return DeliveryResult.Fail(null, "holder DID document has no CredentialService endpoint");

        var url = credentialService.TrimEnd('/') + "/credentials";

        var creds = new JsonArray();
        foreach (var c in credentials)
            creds.Add(new JsonObject { ["credentialType"] = c.CredentialType, ["payload"] = c.Jwt, ["format"] = Format });

        var message = new JsonObject
        {
            ["@context"] = new JsonArray { "https://w3id.org/dspace-dcp/v1.0/dcp.jsonld" },
            ["type"] = "CredentialMessage",
            ["issuerPid"] = issuerPid,
            ["holderPid"] = holderPid,
            ["status"] = "ISSUED",
            ["credentials"] = creds,
        };

        var token = await SelfIssuedToken.IssueAsync(signer, holderDid, ct: ct);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return DeliveryResult.Ok(url);
            var body = await response.Content.ReadAsStringAsync(ct);
            return DeliveryResult.Fail(url, $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return DeliveryResult.Fail(url, ex.Message);
        }
    }
}
