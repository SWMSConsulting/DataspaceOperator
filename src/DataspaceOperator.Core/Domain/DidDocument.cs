using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DataspaceOperator.Core.Domain;

/// <summary>Minimal W3C DID document model (did:web).</summary>
public sealed class DidDocument
{
    [JsonPropertyName("@context")]
    public object Context { get; set; } = new[] { "https://www.w3.org/ns/did/v1" };

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("verificationMethod")]
    public List<VerificationMethod> VerificationMethod { get; set; } = [];

    [JsonPropertyName("authentication")]
    public List<string> Authentication { get; set; } = [];

    [JsonPropertyName("assertionMethod")]
    public List<string> AssertionMethod { get; set; } = [];

    [JsonPropertyName("service")]
    public List<DidService> Service { get; set; } = [];
}

public sealed class VerificationMethod
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "JsonWebKey2020";

    [JsonPropertyName("controller")]
    public string Controller { get; set; } = "";

    [JsonPropertyName("publicKeyJwk")]
    public JsonObject? PublicKeyJwk { get; set; }
}

public sealed class DidService
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("serviceEndpoint")]
    public string ServiceEndpoint { get; set; } = "";
}
