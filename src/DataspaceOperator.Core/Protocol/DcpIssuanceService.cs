using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// Issues credentials to participants. This is the "issuer logic" we own instead of the EDC engine:
/// it maps participant data -&gt; credentialSubject, signs a JWT-VC, records it, and hands it back
/// for delivery to the holder's own wallet.
/// </summary>
public sealed class DcpIssuanceService(
    IIssuerKeyProvider keys,
    IParticipantStore participants,
    ICredentialStore credentials,
    StatusListService statusList)
{
    public sealed record IssuedResult(string CredentialType, string Jwt, Guid Id);

    /// <summary>Mint + record a credential of the given type for a participant.</summary>
    public async Task<IssuedResult> IssueAsync(string holderDid, string credentialType, CancellationToken ct = default)
    {
        var participant = await participants.GetByDidAsync(holderDid, ct)
            ?? throw new InvalidOperationException($"Unknown participant '{holderDid}'.");

        var claims = BuildSubjectClaims(credentialType, participant);
        var index = statusList.Allocate();
        var status = statusList.StatusEntryFor(index);

        var validity = TimeSpan.FromDays(365);
        var jwt = VerifiableCredentials.IssueJwtVc(
            keys.SigningKey, keys.IssuerDid, keys.KeyId,
            subjectDid: holderDid,
            types: [credentialType],
            credentialSubjectClaims: claims,
            validity: validity,
            credentialStatus: status);

        var record = new IssuedCredential
        {
            HolderDid = holderDid,
            CredentialType = credentialType,
            Jwt = jwt,
            StatusListIndex = index,
            Lifecycle = CredentialLifecycle.Issued,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(validity),
        };
        await credentials.AddAsync(record, ct);
        return new IssuedResult(credentialType, jwt, record.Id);
    }

    /// <summary>Revoke a credential: flip the status-list bit and mark the record.</summary>
    public async Task RevokeAsync(Guid credentialId, CancellationToken ct = default)
    {
        var cred = await credentials.GetAsync(credentialId, ct)
            ?? throw new InvalidOperationException($"Unknown credential '{credentialId}'.");
        statusList.Revoke(cred.StatusListIndex);
        await credentials.SetLifecycleAsync(credentialId, CredentialLifecycle.Revoked, ct);
    }

    private static JsonObject BuildSubjectClaims(string credentialType, Participant p) => credentialType switch
    {
        "MembershipCredential" => new JsonObject
        {
            ["holderIdentifier"] = p.Bpn,
            ["memberOf"] = "Dataspace",
            ["membershipType"] = "FullMember",
            ["since"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        },
        "DataExchangeGovernanceCredential" => new JsonObject
        {
            ["holderIdentifier"] = p.Bpn,
            ["contractVersion"] = "1.0.0",
            ["contractTemplate"] = "https://public.example.org/contracts/DataExchangeGovernance.v1.pdf",
        },
        _ => new JsonObject { ["holderIdentifier"] = p.Bpn },
    };
}
