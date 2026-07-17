using DevExpress.ExpressApp;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using DataspaceOperator.Core.Protocol;
using DataspaceOperator.Endpoints;
using DataspaceOperator.Xaf.Module.BusinessObjects;

namespace DataspaceOperator.Xaf.Blazor.Server;

/// <summary>
/// Wires the (framework-independent) dataspace protocol core into the XAF host:
/// - issuer key from configuration,
/// - DID resolution (our own issuer DID locally, participants via did:web over HTTP),
/// - store adapters backed by the XAF EF Core object space.
/// </summary>
public static class ProtocolIntegration
{
    public static IServiceCollection AddDataspaceProtocol(this IServiceCollection services, IConfiguration config)
    {
        var issuerDid = config["Issuer:Did"] ?? "did:web:issuer.localhost";

        services.AddHttpClient();
        // Issuer signing key from the secret store (env/user-secrets/appsettings; vault-ready seam).
        services.AddSingleton<IIssuerKeyProvider>(_ =>
            IssuerKeyFactory.CreateAsync(new ConfigurationSecretStore(config), issuerDid).GetAwaiter().GetResult());
        services.AddSingleton<DidDocumentBuilder>();
        services.AddSingleton<IssuerMetadata>();
        // Persistent status list (revocation survives restarts).
        services.AddScoped<IStatusListStore, XafStatusListStore>();
        services.AddScoped(sp => new StatusListService(
            sp.GetRequiredService<IIssuerKeyProvider>(), sp.GetRequiredService<IStatusListStore>(),
            $"{issuerDid}/status-lists/revocation"));
        services.AddSingleton<IDidResolver, OperatorDidResolver>();

        // store adapters over the XAF object space (per-request)
        services.AddScoped<IParticipantStore, XafParticipantStore>();
        services.AddScoped<ITrustedIssuerStore, XafTrustedIssuerStore>();
        services.AddScoped<ICredentialDefinitionStore, XafCredentialDefinitionStore>();
        services.AddScoped<ICredentialStore, XafCredentialStore>();
        services.AddHttpClient<ICredentialDeliveryService, HttpCredentialDeliveryService>();

        services.AddScoped<VpVerifier>();
        services.AddScoped<BdrsDirectoryService>();
        services.AddScoped<DcpIssuanceService>();
        return services;
    }
}

/// <summary>Resolves our own issuer DID locally; everything else via did:web over HTTP.</summary>
public sealed class OperatorDidResolver(IIssuerKeyProvider keys, DidDocumentBuilder builder, IHttpClientFactory httpFactory)
    : IDidResolver
{
    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.Equals(did, keys.IssuerDid, StringComparison.Ordinal))
            return Task.FromResult<DidDocument?>(builder.BuildIssuerDocument());
        var http = new DidWebResolver(httpFactory.CreateClient(), useHttps: false);
        return http.ResolveAsync(did, ct);
    }
}

// --- store adapters: map XAF EF entities <-> framework-independent Core domain types ---

public sealed class XafParticipantStore(INonSecuredObjectSpaceFactory factory) : IParticipantStore
{
    public Task<Participant?> GetByDidAsync(string did, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(ParticipantEntity));
        var e = os.GetObjectsQuery<ParticipantEntity>().FirstOrDefault(x => x.Did == did);
        return Task.FromResult(e is null ? null : Map(e));
    }

    public Task<IReadOnlyList<Participant>> ListAsync(CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(ParticipantEntity));
        var list = os.GetObjectsQuery<ParticipantEntity>().ToList().Select(Map).ToList();
        return Task.FromResult<IReadOnlyList<Participant>>(list);
    }

    public Task UpsertAsync(Participant p, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(ParticipantEntity));
        var e = os.GetObjectsQuery<ParticipantEntity>().FirstOrDefault(x => x.Did == p.Did)
                ?? os.CreateObject<ParticipantEntity>();
        e.Name = p.Name; e.Bpn = p.Bpn; e.Did = p.Did;
        e.CredentialServiceUrl = p.CredentialServiceUrl; e.State = p.State; e.OnboardedUtc = p.OnboardedUtc?.UtcDateTime;
        os.CommitChanges();
        return Task.CompletedTask;
    }

    private static Participant Map(ParticipantEntity e) => new()
    {
        Name = e.Name ?? "", Bpn = e.Bpn ?? "", Did = e.Did ?? "",
        CredentialServiceUrl = e.CredentialServiceUrl, State = e.State,
        OnboardedUtc = e.OnboardedUtc,
    };
}

public sealed class XafTrustedIssuerStore(INonSecuredObjectSpaceFactory factory) : ITrustedIssuerStore
{
    public Task<IReadOnlyList<TrustedIssuer>> ListAsync(CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(TrustedIssuerEntity));
        var list = os.GetObjectsQuery<TrustedIssuerEntity>().ToList().Select(Map).ToList();
        return Task.FromResult<IReadOnlyList<TrustedIssuer>>(list);
    }

    public Task<bool> IsTrustedAsync(string issuerDid, string credentialType, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(TrustedIssuerEntity));
        var e = os.GetObjectsQuery<TrustedIssuerEntity>().FirstOrDefault(x => x.Did == issuerDid);
        if (e is null) return Task.FromResult(false);
        var types = Split(e.SupportedTypesCsv);
        return Task.FromResult(types.Count == 0 || types.Contains(credentialType));
    }

    public Task UpsertAsync(TrustedIssuer issuer, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(TrustedIssuerEntity));
        var e = os.GetObjectsQuery<TrustedIssuerEntity>().FirstOrDefault(x => x.Did == issuer.Did)
                ?? os.CreateObject<TrustedIssuerEntity>();
        e.Did = issuer.Did; e.IsOwnIssuer = issuer.IsOwnIssuer;
        e.SupportedTypesCsv = string.Join(',', issuer.SupportedTypes);
        os.CommitChanges();
        return Task.CompletedTask;
    }

    private static List<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static TrustedIssuer Map(TrustedIssuerEntity e) => new()
    {
        Did = e.Did ?? "", IsOwnIssuer = e.IsOwnIssuer, SupportedTypes = Split(e.SupportedTypesCsv),
    };
}

public sealed class XafCredentialDefinitionStore(INonSecuredObjectSpaceFactory factory) : ICredentialDefinitionStore
{
    public Task<CredentialDefinition?> GetByTypeAsync(string credentialType, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(CredentialDefinitionEntity));
        var e = os.GetObjectsQuery<CredentialDefinitionEntity>().FirstOrDefault(x => x.CredentialType == credentialType);
        return Task.FromResult(e is null ? null : Map(e));
    }

    public Task<IReadOnlyList<CredentialDefinition>> ListAsync(CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(CredentialDefinitionEntity));
        var list = os.GetObjectsQuery<CredentialDefinitionEntity>().ToList().Select(Map).ToList();
        return Task.FromResult<IReadOnlyList<CredentialDefinition>>(list);
    }

    private static CredentialDefinition Map(CredentialDefinitionEntity e) => new()
    {
        CredentialType = e.CredentialType ?? "",
        ContextUrl = e.ContextUrl,
        ClaimTemplateJson = string.IsNullOrWhiteSpace(e.ClaimTemplateJson) ? "{}" : e.ClaimTemplateJson!,
        ValiditySeconds = e.ValiditySeconds,
    };
}

public sealed class XafStatusListStore(INonSecuredObjectSpaceFactory factory) : IStatusListStore
{
    public Task<StatusListState> LoadAsync(CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(StatusListStateEntity));
        var e = os.GetObjectsQuery<StatusListStateEntity>().FirstOrDefault();
        var state = new StatusListState();
        if (e is not null)
        {
            state.NextIndex = e.NextIndex;
            if (e.Bits is { Length: > 0 }) state.Bits = e.Bits;
        }
        return Task.FromResult(state);
    }

    public Task SaveAsync(StatusListState state, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(StatusListStateEntity));
        var e = os.GetObjectsQuery<StatusListStateEntity>().FirstOrDefault() ?? os.CreateObject<StatusListStateEntity>();
        e.NextIndex = state.NextIndex;
        e.Bits = state.Bits;
        os.CommitChanges();
        return Task.CompletedTask;
    }
}

public sealed class XafCredentialStore(INonSecuredObjectSpaceFactory factory) : ICredentialStore
{
    public Task<Guid> AddAsync(IssuedCredential credential, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(IssuedCredentialEntity));
        var e = os.CreateObject<IssuedCredentialEntity>();
        // link to the holder participant (1-n association)
        e.Participant = os.GetObjectsQuery<ParticipantEntity>().FirstOrDefault(x => x.Did == credential.HolderDid);
        e.CredentialType = credential.CredentialType; e.Jwt = credential.Jwt;
        e.StatusListIndex = credential.StatusListIndex; e.Lifecycle = credential.Lifecycle;
        e.IssuedUtc = credential.IssuedUtc.UtcDateTime; e.ExpiresUtc = credential.ExpiresUtc?.UtcDateTime;
        e.DeliveryStatus = credential.DeliveryStatus; e.DeliveredUtc = credential.DeliveredUtc?.UtcDateTime;
        os.CommitChanges();
        return Task.FromResult(e.ID);
    }

    public Task<IssuedCredential?> GetAsync(Guid id, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(IssuedCredentialEntity));
        var e = os.GetObjectsQuery<IssuedCredentialEntity>().FirstOrDefault(x => x.ID == id);
        return Task.FromResult(e is null ? null : Map(e));
    }

    public Task<IReadOnlyList<IssuedCredential>> ListByHolderAsync(string holderDid, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(IssuedCredentialEntity));
        var list = os.GetObjectsQuery<IssuedCredentialEntity>()
            .Where(x => x.Participant != null && x.Participant.Did == holderDid)
            .ToList().Select(Map).ToList();
        return Task.FromResult<IReadOnlyList<IssuedCredential>>(list);
    }

    public Task SetLifecycleAsync(Guid id, CredentialLifecycle lifecycle, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(IssuedCredentialEntity));
        var e = os.GetObjectsQuery<IssuedCredentialEntity>().FirstOrDefault(x => x.ID == id);
        if (e is not null) { e.Lifecycle = lifecycle; os.CommitChanges(); }
        return Task.CompletedTask;
    }

    private static IssuedCredential Map(IssuedCredentialEntity e) => new()
    {
        Id = e.ID, HolderDid = e.Participant?.Did ?? "", CredentialType = e.CredentialType ?? "",
        Jwt = e.Jwt ?? "", StatusListIndex = e.StatusListIndex, Lifecycle = e.Lifecycle,
        IssuedUtc = e.IssuedUtc, ExpiresUtc = e.ExpiresUtc,
        DeliveryStatus = e.DeliveryStatus, DeliveredUtc = e.DeliveredUtc,
    };
}
