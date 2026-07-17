using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    // (A) did:web — W3C
    private static void MapDidWeb(IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/did.json", (DidDocumentBuilder builder) =>
        {
            var doc = builder.BuildIssuerDocument();
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

    // (C) DCP issuance — discovery-based: metadata advertises our own paths.
    private static void MapDcpIssuance(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/issuance/.well-known/vci", (HttpContext ctx, IssuerMetadata meta) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Json(meta.Build(baseUrl));
        });

        // Credential Request (holder wallet -> issuer). Simplified synchronous issuance:
        // in a full DCP flow this is async with a request-status resource.
        app.MapPost("/api/issuance/credentials", async (
            JsonObject request, DcpIssuanceService issuance, CancellationToken ct) =>
        {
            var holderDid = (string?)request["holderDid"] ?? (string?)request["sub"];
            var type = (string?)request["credentialType"] ?? "MembershipCredential";
            if (string.IsNullOrEmpty(holderDid))
                return Results.BadRequest(new { error = "holderDid is required" });

            try
            {
                var result = await issuance.IssueAsync(holderDid, type, ct);
                return Results.Ok(new { result.Id, result.CredentialType, credential = result.Jwt });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    // (D) Status list — W3C Bitstring StatusList (revocation).
    private static void MapStatusList(IEndpointRouteBuilder app)
    {
        app.MapGet("/status-lists/revocation", (StatusListService statusList) =>
        {
            var jwt = statusList.BuildStatusListCredentialJwt();
            return Results.Text(jwt, "application/jwt", Encoding.ASCII);
        });
    }
}
