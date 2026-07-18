using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataspaceOperator.Endpoints;

/// <summary>
/// Maps the four participant-facing dataspace protocol endpoints. These — and only these —
/// are the interoperability contract of the central service.
/// </summary>
public static class ProtocolEndpoints
{
    public static IEndpointRouteBuilder MapDataspaceProtocol(this IEndpointRouteBuilder app)
    {
        MapDidWeb(app);
        MapBdrsDirectory(app);
        MapDcpIssuance(app);
        MapStatusList(app);
        return app;
    }

    // (A) did:web — W3C. Publishes an "IssuerService" endpoint so holder wallets can discover where
    // to send DCP CredentialRequestMessages. Behind an HTTPS reverse proxy the app never sees the
    // public scheme/host, so the endpoint is derived from our own did:web identifier, not the request.
    private static void MapDidWeb(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/did.json", (DidDocumentBuilder builder, IIssuerSigner signer) =>
        {
            var issuerService = DidWebResolver.DidWebToOrigin(signer.IssuerDid) + "/api/issuance";
            var doc = builder.BuildIssuerDocument(issuerService);
            return Results.Json(doc, contentType: "application/did+json");
        });
    }

    // (B) BDRS directory read — contract from the tractusx connector (BdrsClientImpl):
    //     GET {base}/bpn-directory, Authorization: Bearer <Membership-VP>, gzip response.
    private static void MapBdrsDirectory(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/directory/bpn-directory", async (
            HttpContext ctx, VpVerifier verifier, BdrsDirectoryService bdrs, CancellationToken ct) =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();

            var vpJwt = auth["Bearer ".Length..].Trim();
            var verification = await verifier.VerifyMembershipAsync(vpJwt, ct);
            if (!verification.Success)
                return Results.Json(new { error = verification.Error }, statusCode: StatusCodes.Status401Unauthorized);

            var map = await bdrs.GetDirectoryAsync(ct);
            var json = JsonSerializer.SerializeToUtf8Bytes(map);

            // connectors send Accept-Encoding: gzip and expect a gzip body
            ctx.Response.Headers.ContentEncoding = "gzip";
            ctx.Response.ContentType = "application/json";
            await using var gz = new GZipStream(ctx.Response.Body, CompressionLevel.Fastest);
            await gz.WriteAsync(json, ct);
            return Results.Empty;
        });
    }

    // (C) DCP issuance — real holder-initiated flow.
    //   1. Holder resolves our did:web -> "IssuerService" endpoint -> POST {endpoint}/credentials
    //      a CredentialRequestMessage {holderPid, credentials:[{id}]}, Bearer = holder SI-token.
    //   2. We verify the token, assign an issuerPid, return 201 + Location, and then asynchronously
    //      issue + deliver a CredentialMessage {issuerPid, holderPid, status:ISSUED} to the holder's
    //      CredentialService storage endpoint (correlated by holderPid on the holder side).
    private static void MapDcpIssuance(IEndpointRouteBuilder app)
    {
        // Issuer Metadata (DCP): advertises the credential objects we can issue.
        app.MapGet("/api/issuance/.well-known/vci", (IssuerMetadata meta, IIssuerSigner signer) =>
            Results.Json(meta.Build(DidWebResolver.DidWebToOrigin(signer.IssuerDid))));

        // Credential Request (holder wallet -> issuer).
        app.MapPost("/api/issuance/credentials", async (
            HttpContext ctx, IServiceScopeFactory scopeFactory,
            IDidResolver didResolver, IParticipantStore participants, IIssuerSigner signer,
            IssuanceRequestTracker tracker, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("DcpIssuance");

            var auth = ctx.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { error = "Authorization Bearer token required" }, statusCode: 401);
            var token = auth["Bearer ".Length..].Trim();

            JsonObject? body;
            try { body = (await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ct)) as JsonObject; }
            catch { body = null; }
            if (body is null)
                return Results.Json(new { error = "invalid JSON body" }, statusCode: 400);

            // verify the holder self-issued token: iss==sub==holder DID, aud==our DID, did:web signature.
            var verified = await SelfIssuedToken.VerifyAsync(token, signer.IssuerDid, didResolver, ct);
            if (verified is null)
                return Results.Json(new { error = "ID token verification failed" }, statusCode: 401);
            var holderDid = verified.Issuer;

            var participant = await participants.GetByDidAsync(holderDid, ct);
            if (participant is null)
                return Results.Json(new { error = $"unknown participant '{holderDid}'" }, statusCode: 401);

            var holderPid = (string?)body["holderPid"];
            if (string.IsNullOrEmpty(holderPid))
                return Results.Json(new { error = "holderPid is required" }, statusCode: 400);

            var credentialType = ResolveRequestedType(body);
            if (credentialType is null)
                return Results.Json(new { error = "no supported credential requested" }, statusCode: 400);

            var pending = tracker.Create(holderPid, holderDid, credentialType);
            log.LogInformation("DCP request: holder={Holder} holderPid={HolderPid} type={Type} -> issuerPid={IssuerPid}",
                holderDid, holderPid, credentialType, pending.IssuerPid);

            // Fire-and-forget the issue + delivery on a fresh DI scope (outlives this request).
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var issuance = scope.ServiceProvider.GetRequiredService<DcpIssuanceService>();
                var scopeLog = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DcpIssuance");
                try
                {
                    await issuance.IssueForRequestAsync(holderDid, credentialType, holderPid, pending.IssuerPid);
                    pending.State = IssuanceRequestTracker.RequestState.Issued;
                    scopeLog.LogInformation("DCP delivered: issuerPid={IssuerPid} type={Type} to {Holder}",
                        pending.IssuerPid, credentialType, holderDid);
                }
                catch (Exception ex)
                {
                    pending.State = IssuanceRequestTracker.RequestState.Rejected;
                    pending.Error = ex.GetBaseException().Message;
                    scopeLog.LogError(ex, "DCP issuance failed: issuerPid={IssuerPid}", pending.IssuerPid);
                }
            });

            // 201 Created. The issuerPid goes in both the Location header and the body: different
            // IdentityHub versions read one or the other; correlation on the holder is by holderPid.
            var location = $"{DidWebResolver.DidWebToOrigin(signer.IssuerDid)}/api/issuance/requests/{pending.IssuerPid}";
            ctx.Response.StatusCode = StatusCodes.Status201Created;
            ctx.Response.Headers.Location = location;
            await ctx.Response.WriteAsync(pending.IssuerPid, ct);
            return Results.Empty;
        });

        // Credential Request Status resource (DCP CredentialStatus).
        app.MapGet("/api/issuance/requests/{issuerPid}", (string issuerPid, IssuanceRequestTracker tracker) =>
        {
            var p = tracker.Get(issuerPid);
            if (p is null) return Results.NotFound();
            var status = p.State switch
            {
                IssuanceRequestTracker.RequestState.Issued => "ISSUED",
                IssuanceRequestTracker.RequestState.Rejected => "REJECTED",
                _ => "RECEIVED",
            };
            return Results.Json(new JsonObject
            {
                ["@context"] = new JsonArray { "https://w3id.org/dspace-dcp/v1.0/dcp.jsonld" },
                ["type"] = "CredentialStatus",
                ["issuerPid"] = p.IssuerPid,
                ["holderPid"] = p.HolderPid,
                ["status"] = status,
            });
        });
    }

    // The holder references credential-object ids in the request; our metadata uses id == type.
    private static string? ResolveRequestedType(JsonObject request)
    {
        if (request["credentials"] is JsonArray creds)
        {
            foreach (var c in creds)
            {
                var id = (string?)c?["id"] ?? (string?)c?["credentialType"];
                if (id is not null && IssuerMetadata.SupportedTypes.Contains(id))
                    return id;
            }
        }
        // Fallback: a plain {credentialType} body (used by the local demo trigger).
        var t = (string?)request["credentialType"];
        return t is not null && IssuerMetadata.SupportedTypes.Contains(t) ? t : null;
    }

    // (D) Status list — W3C Bitstring StatusList (revocation).
    private static void MapStatusList(IEndpointRouteBuilder app)
    {
        app.MapGet("/status-lists/revocation", async (StatusListService statusList) =>
        {
            var jwt = await statusList.BuildStatusListCredentialJwtAsync();
            return Results.Text(jwt, "application/jwt", Encoding.ASCII);
        });
    }
}
