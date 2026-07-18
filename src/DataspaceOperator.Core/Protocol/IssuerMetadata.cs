using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// DCP Issuer Metadata. A holder wallet reads this to discover the credentials we offer. The
/// <c>credentialsSupported</c> entries are DCP <c>CredentialObject</c>s; their <c>id</c> is what a
/// holder references in a <c>CredentialRequestMessage</c>. We use <c>id == credentialType</c> so the
/// issuer can map a requested id straight back to the credential type to issue.
/// </summary>
public sealed class IssuerMetadata(IIssuerSigner signer)
{
    /// <summary>Credential types we can issue (id == type). Keep in sync with the trusted-issuer setup.</summary>
    public static readonly string[] SupportedTypes = ["MembershipCredential", "DataExchangeGovernanceCredential"];

    // eclipse-edc CredentialProfile: "vc11-sl2021/jwt" maps to CredentialFormat.VC1_0_JWT.
    public const string Profile = "vc11-sl2021/jwt";

    public JsonObject Build(string baseUrl)
    {
        var supported = new JsonArray();
        foreach (var type in SupportedTypes) supported.Add(CredentialObject(type));
        return new JsonObject
        {
            ["@context"] = new JsonArray { "https://w3id.org/dspace-dcp/v1.0/dcp.jsonld" },
            ["type"] = "IssuerMetadata",
            ["issuer"] = signer.IssuerDid,
            ["credentialsSupported"] = supported,
        };
    }

    public static string CredentialObjectId(string credentialType) => credentialType;

    private static JsonObject CredentialObject(string credentialType) => new()
    {
        ["id"] = CredentialObjectId(credentialType),
        ["type"] = "CredentialObject",
        ["credentialType"] = credentialType,
        ["bindingMethods"] = new JsonArray { "did:web" },
        ["profile"] = Profile,
    };
}
