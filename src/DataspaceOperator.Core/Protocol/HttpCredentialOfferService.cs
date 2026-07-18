using System.Text;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// Sends a DCP <c>CredentialOfferMessage</c> to a holder's CredentialService offers endpoint.
///
/// The offer carries a <c>CredentialObject</c> (id + credentialType + profile). On receipt the
/// tractusx IdentityHub stores the offer and its CredentialOfferHandler automatically initiates a
/// holder-side credential request (deriving the format from the profile), which then flows back to
/// our IssuerService. The offer POST is authenticated with an issuer self-issued token (aud = holder).
/// </summary>
public sealed class HttpCredentialOfferService(
    IDidResolver didResolver,
    IIssuerSigner signer,
    HttpClient http) : ICredentialOfferService
{
    public async Task<OfferResult> SendOfferAsync(string holderDid, string credentialType, CancellationToken ct = default)
    {
        var doc = await didResolver.ResolveAsync(holderDid, ct);
        if (doc is null) return OfferResult.Fail(null, $"cannot resolve holder DID '{holderDid}'");

        var credentialService = DidWebResolver.GetCredentialServiceEndpoint(doc);
        if (string.IsNullOrEmpty(credentialService))
            return OfferResult.Fail(null, "holder DID document has no CredentialService endpoint");

        var url = credentialService.TrimEnd('/') + "/offers";

        var message = new JsonObject
        {
            ["@context"] = new JsonArray { "https://w3id.org/dspace-dcp/v1.0/dcp.jsonld" },
            ["type"] = "CredentialOfferMessage",
            ["issuer"] = signer.IssuerDid,
            ["credentials"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = IssuerMetadata.CredentialObjectId(credentialType),
                    ["type"] = "CredentialObject",
                    ["credentialType"] = credentialType,
                    ["bindingMethods"] = new JsonArray { "did:web" },
                    ["profile"] = IssuerMetadata.Profile,
                },
            },
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
            if (response.IsSuccessStatusCode) return OfferResult.Ok(url);
            var body = await response.Content.ReadAsStringAsync(ct);
            return OfferResult.Fail(url, $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return OfferResult.Fail(url, ex.Message);
        }
    }
}
