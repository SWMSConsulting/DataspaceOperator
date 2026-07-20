namespace DataspaceOperator.Core.Abstractions;

/// <summary>
/// One recorded call against a protocol endpoint of the central service. Kept framework-independent
/// so the Core/Endpoints assemblies can record without knowing about the XAF persistence model.
/// </summary>
public sealed record AuditRecord(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Method,
    string Path,
    int StatusCode,
    long DurationMs,
    string? ParticipantDid,
    string? RequestBody,
    string? Detail);

/// <summary>Append-only sink for <see cref="AuditRecord"/>s (the operator's audit trail).</summary>
public interface IAuditStore
{
    Task AddAsync(AuditRecord record, CancellationToken ct = default);
}

/// <summary>No-op sink used where no persistence is wired (e.g. the local demo host).</summary>
public sealed class NullAuditStore : IAuditStore
{
    public Task AddAsync(AuditRecord record, CancellationToken ct = default) => Task.CompletedTask;
}
