using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// Issuer Metadata for DCP issuance. The wallet reads this to discover our endpoints —
/// which means WE choose the paths; only the message formats + this metadata are the contract.
/// </summary>
public sealed class IssuerMetadata(IIssuerKeyProvider keys)
{
    public JsonObject Build(string baseUrl) => new()
    {
        ["credentialIssuer"] = keys.IssuerDid,
        ["credentialEndpoint"] = $"{baseUrl}/api/issuance/credentials",
        ["credentialRequestStatusEndpoint"] = $"{baseUrl}/api/issuance/requests",
        ["statusListEndpoint"] = $"{baseUrl}/status-lists/revocation",
        ["credentialsSupported"] = new JsonArray
        {
            Supported("MembershipCredential"),
            Supported("DataExchangeGovernanceCredential"),
        },
    };

    private static JsonObject Supported(string type) => new()
    {
        ["type"] = new JsonArray { "VerifiableCredential", type },
        ["format"] = "VC1_0_JWT",
    };
}
