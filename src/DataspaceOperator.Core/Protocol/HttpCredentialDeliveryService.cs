using System.Text;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// DCP credential delivery over HTTP: resolves the holder's CredentialService endpoint from its
/// DID document and POSTs a DCP CredentialMessage, authenticated with an issuer self-issued token.
///
/// NOTE: the exact CredentialMessage schema + token semantics depend on the target wallet's
/// contract — align via live-capture against the concrete wallet (see the concept docs).
/// </summary>
public sealed class HttpCredentialDeliveryService(
    IDidResolver didResolver,
    IIssuerKeyProvider keys,
    HttpClient http) : ICredentialDeliveryService
{
    public async Task<DeliveryResult> DeliverAsync(
        string holderDid, IReadOnlyList<CredentialToDeliver> credentials, CancellationToken ct = default)
    {
        var doc = await didResolver.ResolveAsync(holderDid, ct);
        if (doc is null) return DeliveryResult.Fail(null, $"cannot resolve holder DID '{holderDid}'");

        var credentialService = DidWebResolver.GetCredentialServiceEndpoint(doc);
        if (string.IsNullOrEmpty(credentialService))
            return DeliveryResult.Fail(null, "holder DID document has no CredentialService endpoint");

        var url = credentialService.TrimEnd('/') + "/credentials";

        var creds = new JsonArray();
        foreach (var c in credentials)
            creds.Add(new JsonObject { ["credentialType"] = c.CredentialType, ["payload"] = c.Jwt, ["format"] = "vc1_0_jwt" });

        var message = new JsonObject
        {
            ["@context"] = new JsonArray { "https://w3id.org/dspace-dcp/v1.0/dcp.jsonld" },
            ["type"] = "CredentialMessage",
            ["credentials"] = creds,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + IssueSelfToken(holderDid));

        try
        {
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? DeliveryResult.Ok(url)
                : DeliveryResult.Fail(url, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return DeliveryResult.Fail(url, ex.Message);
        }
    }

    private string IssueSelfToken(string audience)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new JsonObject { ["typ"] = "JWT", ["kid"] = keys.KeyId };
        var payload = new JsonObject
        {
            ["iss"] = keys.IssuerDid,
            ["sub"] = keys.IssuerDid,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
        };
        return Jws.Sign(header, payload, keys.SigningKey);
    }
}
