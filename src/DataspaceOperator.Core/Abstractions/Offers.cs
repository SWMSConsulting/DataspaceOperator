namespace DataspaceOperator.Core.Abstractions;

public sealed record OfferResult(bool Success, string? Endpoint, string? Error)
{
    public static OfferResult Ok(string endpoint) => new(true, endpoint, null);
    public static OfferResult Fail(string? endpoint, string error) => new(false, endpoint, error);
}

/// <summary>
/// Issuer-initiated DCP entry point: send a <c>CredentialOfferMessage</c> to a holder's
/// CredentialService offers endpoint. On receipt the holder wallet automatically initiates the
/// holder-side credential request against our IssuerService — driving the full DCP issuance flow
/// from a single operator action, without needing the holder's management API key.
/// </summary>
public interface ICredentialOfferService
{
    Task<OfferResult> SendOfferAsync(string holderDid, string credentialType, CancellationToken ct = default);
}
