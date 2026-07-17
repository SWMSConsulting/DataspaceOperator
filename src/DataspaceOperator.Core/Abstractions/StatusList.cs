namespace DataspaceOperator.Core.Abstractions;

/// <summary>Persisted state of the revocation status list: the bitstring + next free index.</summary>
public sealed class StatusListState
{
    public const int DefaultSizeBytes = 16 * 1024; // 128k entries (W3C herd-privacy minimum)

    public byte[] Bits { get; set; } = new byte[DefaultSizeBytes];
    public int NextIndex { get; set; }
}

/// <summary>Persists the status list so revocations survive restarts.</summary>
public interface IStatusListStore
{
    Task<StatusListState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(StatusListState state, CancellationToken ct = default);
}
