namespace DataspaceOperator.Core.Abstractions;

/// <summary>A credential to hand to a holder's wallet.</summary>
public sealed record CredentialToDeliver(string CredentialType, string Jwt);

public sealed record DeliveryResult(bool Success, string? Endpoint, string? Error)
{
    public static DeliveryResult Ok(string endpoint) => new(true, endpoint, null);
    public static DeliveryResult Fail(string? endpoint, string error) => new(false, endpoint, error);
}

/// <summary>
/// Delivers issued credentials to the holder's own wallet: resolve the holder DID, find its
/// CredentialService endpoint, and POST a DCP CredentialMessage. Best-effort — if the wallet is
/// unreachable, issuance still succeeds and the delivery is marked failed.
/// </summary>
public interface ICredentialDeliveryService
{
    /// <summary>
    /// Deliver a DCP <c>CredentialMessage</c> to the holder's CredentialService storage endpoint.
    /// <paramref name="holderPid"/>/<paramref name="issuerPid"/> correlate the delivery to the
    /// holder-initiated credential request (the IdentityHub storage API requires both).
    /// </summary>
    Task<DeliveryResult> DeliverAsync(
        string holderDid, IReadOnlyList<CredentialToDeliver> credentials,
        string issuerPid, string holderPid, CancellationToken ct = default);
}
