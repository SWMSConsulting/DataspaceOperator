using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Endpoints;
using DataspaceOperator.ProtocolHost;

var builder = WebApplication.CreateBuilder(args);

var issuerDid = builder.Configuration["Issuer:Did"] ?? "did:web:issuer.localhost";
var seed = builder.Configuration["Issuer:PrivateSeedBase64"];

// --- issuer identity + stores (in-memory; swap for XAF/EF-backed implementations) ---
builder.Services.AddSingleton<IIssuerKeyProvider>(_ => new InMemoryIssuerKeyProvider(issuerDid, seed));
builder.Services.AddSingleton<IParticipantStore, InMemoryParticipantStore>();
builder.Services.AddSingleton<IBpnDidStore, InMemoryBpnDidStore>();
builder.Services.AddSingleton<ITrustedIssuerStore, InMemoryTrustedIssuerStore>();
builder.Services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();

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
builder.Services.AddSingleton(sp =>
    new StatusListService(sp.GetRequiredService<IIssuerKeyProvider>(), $"{issuerDid}/status-lists/revocation"));

var app = builder.Build();

// --- seed: trust our own issuer + register our issuer DID document for local resolution ---
{
    var keys = app.Services.GetRequiredService<IIssuerKeyProvider>();
    var trusted = app.Services.GetRequiredService<ITrustedIssuerStore>();
    await trusted.UpsertAsync(new TrustedIssuer { Did = keys.IssuerDid, SupportedTypes = [], IsOwnIssuer = true });

    var issuerDoc = app.Services.GetRequiredService<DidDocumentBuilder>().BuildIssuerDocument();
    app.Services.GetRequiredService<CompositeDidResolver>().Register(keys.IssuerDid, issuerDoc);
}

// --- the dataspace protocol endpoints (the interoperability contract) ---
app.MapDataspaceProtocol();

// --- demo/admin surface (NOT protocol; in the real system this is the XAF UI) ---
// Register a participant, seed its BDRS mapping, and register a DID doc so the demo can verify VPs locally.
app.MapPost("/admin/participants", async (
    RegisterParticipantRequest req,
    IParticipantStore participants, BdrsDirectoryService bdrs, CompositeDidResolver resolver, CancellationToken ct) =>
{
    var p = new Participant
    {
        Name = req.Name, Bpn = req.Bpn, Did = req.Did,
        State = ParticipantState.BdrsRegistered,
    };
    await participants.UpsertAsync(p, ct);
    await bdrs.RegisterAsync(req.Bpn, req.Did, ct);

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
        resolver.Register(req.Did, doc);
    }
    return Results.Ok(new { p.Id, p.Did, p.State });
});

// Issue a credential to a registered participant (issuer-initiated).
app.MapPost("/admin/participants/{did}/issue", async (
    string did, string? type, DcpIssuanceService issuance, CancellationToken ct) =>
{
    var result = await issuance.IssueAsync(did, type ?? "MembershipCredential", ct);
    return Results.Ok(new { result.Id, result.CredentialType, credential = result.Jwt });
});

app.Run();

public sealed record RegisterParticipantRequest(string Name, string Bpn, string Did, JsonObject? PublicKeyJwk);

public partial class Program;
