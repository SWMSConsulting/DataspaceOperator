using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Endpoints;
using DataspaceOperator.ProtocolHost;

var builder = WebApplication.CreateBuilder(args);

var issuerDid = builder.Configuration["Issuer:Did"] ?? "did:web:issuer.localhost";

// --- issuer signer (Vault Transit = key stays in Vault; else local key from Vault-KV/config) ---
// The signer may retain this HttpClient (Vault Transit) for the app lifetime; do not dispose it.
var signerHttp = new HttpClient();
var issuerSigner = await SecretStores.BuildIssuerSignerAsync(builder.Configuration, signerHttp, issuerDid);
builder.Services.AddSingleton<IIssuerSigner>(issuerSigner);

// --- stores (in-memory; swap for XAF/EF-backed implementations) ---
builder.Services.AddSingleton<IParticipantStore, InMemoryParticipantStore>();
builder.Services.AddSingleton<ITrustedIssuerStore, InMemoryTrustedIssuerStore>();
builder.Services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();
builder.Services.AddSingleton<IStatusListStore, InMemoryStatusListStore>();

// --- DID resolution: local registry + http did:web fallback ---
builder.Services.AddHttpClient();
builder.Services.AddSingleton<CompositeDidResolver>(sp =>
    new CompositeDidResolver(new DidWebResolver(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), useHttps: false)));
builder.Services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<CompositeDidResolver>());

// --- crypto/protocol services ---
builder.Services.AddSingleton<VpVerifier>();
builder.Services.AddSingleton<DidDocumentBuilder>();
builder.Services.AddSingleton<BdrsDirectoryService>();
builder.Services.AddSingleton<IssuerMetadata>();
builder.Services.AddSingleton<DcpIssuanceService>();
builder.Services.AddSingleton<IssuanceRequestTracker>();
builder.Services.AddHttpClient<ICredentialDeliveryService, HttpCredentialDeliveryService>();
builder.Services.AddHttpClient<ICredentialOfferService, HttpCredentialOfferService>();
builder.Services.AddSingleton<WalletSink>();
builder.Services.AddSingleton(sp =>
    new StatusListService(sp.GetRequiredService<IIssuerSigner>(), sp.GetRequiredService<IStatusListStore>(), $"{issuerDid}/status-lists/revocation"));

var app = builder.Build();

// --- seed: trust our own issuer + register our issuer DID document for local resolution ---
{
    var signer = app.Services.GetRequiredService<IIssuerSigner>();
    var trusted = app.Services.GetRequiredService<ITrustedIssuerStore>();
    await trusted.UpsertAsync(new TrustedIssuer { Did = signer.IssuerDid, SupportedTypes = [], IsOwnIssuer = true });

    var issuerDoc = app.Services.GetRequiredService<DidDocumentBuilder>().BuildIssuerDocument();
    app.Services.GetRequiredService<CompositeDidResolver>().Register(signer.IssuerDid, issuerDoc);
}

// --- the dataspace protocol endpoints (the interoperability contract) ---
app.MapDataspaceProtocol();

// --- demo/admin surface (NOT protocol; in the real system this is the XAF UI) ---
// Register a participant, seed its BDRS mapping, and register a DID doc so the demo can verify VPs locally.
app.MapPost("/admin/participants", async (
    RegisterParticipantRequest req,
    IParticipantStore participants, CompositeDidResolver resolver, CancellationToken ct) =>
{
    var p = new Participant
    {
        Name = req.Name, Bpn = req.Bpn, Did = req.Did,
        State = ParticipantState.BdrsRegistered,
    };
    await participants.UpsertAsync(p, ct);   // BPN↔DID is now part of the participant → BDRS projects over it

    if (req.PublicKeyJwk is not null)
    {
        var doc = new DidDocument
        {
            Id = req.Did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = $"{req.Did}#key-1", Controller = req.Did, PublicKeyJwk = req.PublicKeyJwk,
                }
            ],
            AssertionMethod = [$"{req.Did}#key-1"],
            Authentication = [$"{req.Did}#key-1"],
        };
        // Publish the participant's own wallet endpoint so the issuer can deliver to it (DCP).
        if (req.CredentialServiceUrl is not null)
        {
            doc.Service.Add(new DidService
            {
                Id = $"{req.Did}#credential-service",
                Type = "CredentialService",
                ServiceEndpoint = req.CredentialServiceUrl,
            });
        }
        resolver.Register(req.Did, doc);
    }
    return Results.Ok(new { p.Id, p.Did, p.State });
});

// --- test wallet receiver (stands in for a participant's real CredentialService) ---
app.MapPost("/wallet/{id}/credentials", (string id, JsonObject message, WalletSink sink) =>
{
    var jwts = (message["credentials"]?.AsArray() ?? [])
        .Select(n => (string?)n?["payload"]).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
    sink.Add(id, jwts);
    return Results.Ok(new { received = jwts.Count });
});
app.MapGet("/wallet/{id}/credentials", (string id, WalletSink sink) => Results.Ok(sink.Get(id)));

// Issue a credential to a registered participant (issuer-initiated).
app.MapPost("/admin/participants/{did}/issue", async (
    string did, string? type, DcpIssuanceService issuance, CancellationToken ct) =>
{
    var result = await issuance.IssueAsync(did, type ?? "MembershipCredential", ct);
    return Results.Ok(new { result.Id, result.CredentialType, delivery = result.Delivery.ToString(), credential = result.Jwt });
});

app.Run();

public sealed record RegisterParticipantRequest(string Name, string Bpn, string Did, JsonObject? PublicKeyJwk, string? CredentialServiceUrl = null);

/// <summary>In-memory stand-in for a participant wallet's credential storage (for delivery tests).</summary>
public sealed class WalletSink
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> _byWallet = new();
    public void Add(string id, IEnumerable<string> jwts)
    {
        var list = _byWallet.GetOrAdd(id, _ => new List<string>());
        lock (list) list.AddRange(jwts);
    }
    public IReadOnlyList<string> Get(string id) =>
        _byWallet.TryGetValue(id, out var l) ? l.ToArray() : Array.Empty<string>();
}

public partial class Program;
