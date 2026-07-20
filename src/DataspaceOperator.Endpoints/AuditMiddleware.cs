using System.Diagnostics;
using System.Text;
using DataspaceOperator.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataspaceOperator.Endpoints;

/// <summary>
/// Records every call against the dataspace protocol endpoints into the operator's audit trail.
/// Endpoints can enrich the entry through <see cref="HttpContext.Items"/>:
/// <list type="bullet">
///   <item><c>audit.did</c> — the participant the call belongs to,</item>
///   <item><c>audit.kind</c> — a friendlier name than the path-derived default,</item>
///   <item><c>audit.detail</c> — outcome/error detail.</item>
/// </list>
/// </summary>
public static class AuditMiddleware
{
    public const string DidKey = "audit.did";
    public const string KindKey = "audit.kind";
    public const string DetailKey = "audit.detail";

    private const int MaxBodyChars = 4096;

    public static IApplicationBuilder UseDataspaceAudit(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            if (!IsProtocolPath(path))
            {
                await next();
                return;
            }

            // Buffer the body so the endpoint can still read it after we capture it.
            string? body = null;
            if (HasCapturableBody(ctx.Request))
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
                var raw = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
                body = raw.Length > MaxBodyChars ? raw[..MaxBodyChars] + "…(truncated)" : raw;
            }

            var started = DateTimeOffset.UtcNow;
            var sw = Stopwatch.StartNew();
            try
            {
                await next();
            }
            finally
            {
                sw.Stop();
                // Never let auditing break the protocol call.
                try
                {
                    var store = ctx.RequestServices.GetService<IAuditStore>();
                    if (store is not null)
                    {
                        var record = new AuditRecord(
                            started,
                            ctx.Items.TryGetValue(KindKey, out var k) ? k as string ?? KindFor(path) : KindFor(path),
                            ctx.Request.Method,
                            path + (ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : ""),
                            ctx.Response.StatusCode,
                            sw.ElapsedMilliseconds,
                            ctx.Items.TryGetValue(DidKey, out var d) ? d as string : null,
                            body,
                            ctx.Items.TryGetValue(DetailKey, out var t) ? t as string : null);
                        await store.AddAsync(record, ctx.RequestAborted);
                    }
                }
                catch (Exception ex)
                {
                    ctx.RequestServices.GetService<ILoggerFactory>()?
                        .CreateLogger("Audit").LogWarning(ex, "Failed to write audit entry for {Path}", path);
                }
            }
        });

    private static bool IsProtocolPath(string path) =>
        path.StartsWith("/.well-known/did.json", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/directory", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/issuance", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/status-lists", StringComparison.OrdinalIgnoreCase);

    private static bool HasCapturableBody(HttpRequest request) =>
        request.ContentLength is > 0 &&
        (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string KindFor(string path) => path switch
    {
        _ when path.StartsWith("/.well-known/did.json", StringComparison.OrdinalIgnoreCase) => "DID document read",
        _ when path.StartsWith("/api/directory", StringComparison.OrdinalIgnoreCase) => "BDRS directory read",
        _ when path.StartsWith("/api/issuance/credentials", StringComparison.OrdinalIgnoreCase) => "DCP credential request",
        _ when path.StartsWith("/api/issuance/offer", StringComparison.OrdinalIgnoreCase) => "DCP credential offer",
        _ when path.StartsWith("/api/issuance/requests", StringComparison.OrdinalIgnoreCase) => "DCP request status",
        _ when path.StartsWith("/api/issuance", StringComparison.OrdinalIgnoreCase) => "Issuer metadata read",
        _ when path.StartsWith("/status-lists", StringComparison.OrdinalIgnoreCase) => "Status list read",
        _ => "Protocol call",
    };
}
