using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Protocol;

/// <summary>Builds this operator's issuer DID document (served at /.well-known/did.json).</summary>
public sealed class DidDocumentBuilder(IIssuerSigner signer)
{
    public DidDocument BuildIssuerDocument(string? credentialServiceUrl = null)
    {
        var doc = new DidDocument
        {
            Context = new[]
            {
                "https://www.w3.org/ns/did/v1",
                "https://w3id.org/security/suites/jws-2020/v1",
            },
            Id = signer.IssuerDid,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = signer.KeyId,
                    Type = "JsonWebKey2020",
                    Controller = signer.IssuerDid,
                    PublicKeyJwk = (System.Text.Json.Nodes.JsonObject)signer.PublicJwk.DeepClone(),
                }
            ],
            Authentication = [signer.KeyId],
            AssertionMethod = [signer.KeyId],
        };
        if (credentialServiceUrl is not null)
        {
            doc.Service.Add(new DidService
            {
                Id = $"{signer.IssuerDid}#issuer-service",
                Type = "IssuerService",
                ServiceEndpoint = credentialServiceUrl,
            });
        }
        return doc;
    }
}
