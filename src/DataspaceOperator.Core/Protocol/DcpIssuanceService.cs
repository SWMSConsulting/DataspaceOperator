using System.Text.Json.Nodes;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;

namespace DataspaceOperator.Core.Protocol;

/// <summary>
/// Issues credentials to participants. Data-driven: the credential type is looked up in the
/// <see cref="ICredentialDefinitionStore"/> and the credentialSubject is built from the definition's
/// JSON template ({bpn}/{did}/{name}/{now} placeholders). Falls back to built-in shapes if no
/// definition and no store are available, so the demo host keeps working.
/// </summary>
public sealed class DcpIssuanceService(
    IIssuerSigner signer,
    IParticipantStore participants,
    ICredentialStore credentials,
    StatusListService statusList,
    ICredentialDefinitionStore? definitions = null,
    ICredentialDeliveryService? delivery = null,
    bool includeCredentialStatus = true)
{
    public sealed record IssuedResult(string CredentialType, string Jwt, Guid Id, DeliveryStatus Delivery);

    /// <summary>
    /// Issuer-initiated issue: sign the VC, record it, and (best-effort) push it to the holder wallet.
    /// NOTE: a raw push has no matching holder request, so real tractusx wallets reject the delivery —
    /// use <see cref="IssueForRequestAsync"/> for the holder-initiated DCP flow. Kept for the local
    /// demo host and for recording an operator-issued credential.
    /// </summary>
    public async Task<IssuedResult> IssueAsync(string holderDid, string credentialType, CancellationToken ct = default)
    {
        var (jwt, index, validity) = await BuildVcAsync(holderDid, credentialType, ct);

        var deliveryStatus = DeliveryStatus.NotAttempted;
        DateTimeOffset? deliveredUtc = null;
        if (delivery is not null)
        {
            // Push has no holderPid/issuerPid correlation; pass empty ids (demo/local only).
            var res = await delivery.DeliverAsync(holderDid, [new CredentialToDeliver(credentialType, jwt)], "", "", ct);
            deliveryStatus = res.Success ? DeliveryStatus.Delivered : DeliveryStatus.Failed;
            if (res.Success) deliveredUtc = DateTimeOffset.UtcNow;
        }

        var storedId = await RecordAsync(holderDid, credentialType, jwt, index, validity, deliveryStatus, deliveredUtc, ct);
        return new IssuedResult(credentialType, jwt, storedId, deliveryStatus);
    }

    /// <summary>
    /// Holder-initiated DCP flow: issue the requested credential and deliver a correlated
    /// <c>CredentialMessage</c> (issuerPid/holderPid) to the holder's CredentialService. Delivery is
    /// retried briefly because the holder only accepts the credential once its own request has
    /// transitioned to REQUESTED (a small race after it receives our request response).
    /// </summary>
    public async Task<IssuedResult> IssueForRequestAsync(
        string holderDid, string credentialType, string holderPid, string issuerPid, CancellationToken ct = default)
    {
        var (jwt, index, validity) = await BuildVcAsync(holderDid, credentialType, ct);

        DeliveryResult? last = null;
        var deliveryStatus = DeliveryStatus.Failed;
        DateTimeOffset? deliveredUtc = null;
        if (delivery is not null)
        {
            // brief settle so the holder finishes transitioning its request to REQUESTED
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                last = await delivery.DeliverAsync(
                    holderDid, [new CredentialToDeliver(credentialType, jwt)], issuerPid, holderPid, ct);
                if (last.Success) { deliveryStatus = DeliveryStatus.Delivered; deliveredUtc = DateTimeOffset.UtcNow; break; }
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            }
        }

        var storedId = await RecordAsync(holderDid, credentialType, jwt, index, validity, deliveryStatus, deliveredUtc, ct);
        if (deliveryStatus != DeliveryStatus.Delivered && last is not null)
            throw new InvalidOperationException($"delivery failed: {last.Error}");
        return new IssuedResult(credentialType, jwt, storedId, deliveryStatus);
    }

    /// <summary>Sign a JWT-VC for the holder using the data-driven definition (or a built-in fallback).</summary>
    private async Task<(string Jwt, int Index, TimeSpan Validity)> BuildVcAsync(
        string holderDid, string credentialType, CancellationToken ct)
    {
        var participant = await participants.GetByDidAsync(holderDid, ct)
            ?? throw new InvalidOperationException($"Unknown participant '{holderDid}'.");

        var def = definitions is null ? null : await definitions.GetByTypeAsync(credentialType, ct);

        JsonObject claims;
        string? contextUrl;
        TimeSpan validity;
        if (def is not null)
        {
            claims = BuildClaimsFromTemplate(def.ClaimTemplateJson, participant);
            contextUrl = def.ContextUrl;
            validity = TimeSpan.FromSeconds(def.ValiditySeconds);
        }
        else
        {
            claims = BuildBuiltInClaims(credentialType, participant);
            contextUrl = null;
            validity = TimeSpan.FromDays(365);
        }

        // credentialStatus is opt-out: the tractusx IdentityHub validates the referenced status-list
        // credential as a signed JWT while EDC's revocation service parses the same URL as JSON-LD.
        // Both fetch it indistinguishably, so until that is reconciled a deployment can omit the
        // status entry entirely (no revocation) to keep presentation + verification working.
        var index = includeCredentialStatus ? await statusList.AllocateAsync(ct) : 0;
        var status = includeCredentialStatus ? statusList.StatusEntryFor(index) : null;
        var jwt = await VerifiableCredentials.IssueJwtVcAsync(
            signer,
            subjectDid: holderDid,
            types: [credentialType],
            credentialSubjectClaims: claims,
            validity: validity,
            credentialStatus: status,
            additionalContexts: contextUrl is null ? null : [contextUrl],
            ct: ct);
        return (jwt, index, validity);
    }

    private async Task<Guid> RecordAsync(
        string holderDid, string credentialType, string jwt, int index, TimeSpan validity,
        DeliveryStatus deliveryStatus, DateTimeOffset? deliveredUtc, CancellationToken ct)
    {
        var record = new IssuedCredential
        {
            HolderDid = holderDid,
            CredentialType = credentialType,
            Jwt = jwt,
            StatusListIndex = index,
            Lifecycle = CredentialLifecycle.Issued,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(validity),
            DeliveryStatus = deliveryStatus,
            DeliveredUtc = deliveredUtc,
        };
        return await credentials.AddAsync(record, ct);
    }

    /// <summary>Revoke a credential: flip the status-list bit and mark the record.</summary>
    public async Task RevokeAsync(Guid credentialId, CancellationToken ct = default)
    {
        var cred = await credentials.GetAsync(credentialId, ct)
            ?? throw new InvalidOperationException($"Unknown credential '{credentialId}'.");
        await statusList.RevokeAsync(cred.StatusListIndex, ct);
        await credentials.SetLifecycleAsync(credentialId, CredentialLifecycle.Revoked, ct);
    }

    // --- template rendering ---

    private static JsonObject BuildClaimsFromTemplate(string templateJson, Participant p)
    {
        var vars = Vars(p);
        var parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(templateJson) ? "{}" : templateJson);
        return (Substitute(parsed, vars) as JsonObject) ?? new JsonObject();
    }

    private static Dictionary<string, string> Vars(Participant p) => new()
    {
        ["bpn"] = p.Bpn,
        ["did"] = p.Did,
        ["name"] = p.Name,
        ["now"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    };

    private static JsonNode? Substitute(JsonNode? node, Dictionary<string, string> vars)
    {
        switch (node)
        {
            case JsonObject o:
                var ro = new JsonObject();
                foreach (var kv in o) ro[kv.Key] = Substitute(kv.Value, vars);
                return ro;
            case JsonArray a:
                var ra = new JsonArray();
                foreach (var it in a) ra.Add(Substitute(it, vars));
                return ra;
            case JsonValue v:
                if (v.TryGetValue<string>(out var s))
                {
                    foreach (var kv in vars) s = s.Replace("{" + kv.Key + "}", kv.Value);
                    return JsonValue.Create(s);
                }
                return v.DeepClone();
            default:
                return null;
        }
    }

    // --- built-in fallbacks (used when no CredentialDefinition/store is present) ---

    private static JsonObject BuildBuiltInClaims(string credentialType, Participant p) => credentialType switch
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
