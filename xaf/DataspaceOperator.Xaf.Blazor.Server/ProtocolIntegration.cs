using DevExpress.ExpressApp;
using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Domain;
using DataspaceOperator.Core.Protocol;
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
        services.AddSingleton<IIssuerKeyProvider>(_ => new ConfigIssuerKeyProvider(config));
        services.AddSingleton<DidDocumentBuilder>();
        services.AddSingleton<IssuerMetadata>();
        services.AddSingleton(sp => new StatusListService(
            sp.GetRequiredService<IIssuerKeyProvider>(), $"{issuerDid}/status-lists/revocation"));
        services.AddSingleton<IDidResolver, OperatorDidResolver>();

        // store adapters over the XAF object space (per-request)
        services.AddScoped<IParticipantStore, XafParticipantStore>();
        services.AddScoped<IBpnDidStore, XafBpnDidStore>();
        services.AddScoped<ITrustedIssuerStore, XafTrustedIssuerStore>();
        services.AddScoped<ICredentialStore, XafCredentialStore>();

        services.AddScoped<VpVerifier>();
        services.AddScoped<BdrsDirectoryService>();
        services.AddScoped<DcpIssuanceService>();
        return services;
    }
}

/// <summary>Issuer signing key from configuration. Provide a stable base64 seed in production.</summary>
public sealed class ConfigIssuerKeyProvider : IIssuerKeyProvider
{
    public ConfigIssuerKeyProvider(IConfiguration config)
    {
        IssuerDid = config["Issuer:Did"] ?? "did:web:issuer.localhost";
        KeyId = $"{IssuerDid}#key-1";
        var seed = config["Issuer:PrivateSeedBase64"];
        SigningKey = string.IsNullOrEmpty(seed)
            ? Ed25519Key.Generate()
            : Ed25519Key.FromPrivateSeed(Convert.FromBase64String(seed));
    }

    public string IssuerDid { get; }
    public string KeyId { get; }
    public Ed25519Key SigningKey { get; }
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

public sealed class XafBpnDidStore(INonSecuredObjectSpaceFactory factory) : IBpnDidStore
{
    public Task UpsertAsync(BpnDidEntry entry, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(BpnDidEntryEntity));
        var e = os.GetObjectsQuery<BpnDidEntryEntity>().FirstOrDefault(x => x.Bpn == entry.Bpn)
                ?? os.CreateObject<BpnDidEntryEntity>();
        e.Bpn = entry.Bpn; e.Did = entry.Did;
        os.CommitChanges();
        return Task.CompletedTask;
    }

    public Task RemoveByBpnAsync(string bpn, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(BpnDidEntryEntity));
        var e = os.GetObjectsQuery<BpnDidEntryEntity>().FirstOrDefault(x => x.Bpn == bpn);
        if (e is not null) { os.Delete(e); os.CommitChanges(); }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetDirectoryAsync(CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(BpnDidEntryEntity));
        var map = os.GetObjectsQuery<BpnDidEntryEntity>()
            .Where(x => x.Bpn != null && x.Did != null)
            .ToList()
            .ToDictionary(x => x.Bpn!, x => x.Did!, StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(map);
    }
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

public sealed class XafCredentialStore(INonSecuredObjectSpaceFactory factory) : ICredentialStore
{
    public Task AddAsync(IssuedCredential credential, CancellationToken ct = default)
    {
        using var os = factory.CreateNonSecuredObjectSpace(typeof(IssuedCredentialEntity));
        var e = os.CreateObject<IssuedCredentialEntity>();
        e.HolderDid = credential.HolderDid; e.CredentialType = credential.CredentialType; e.Jwt = credential.Jwt;
        e.StatusListIndex = credential.StatusListIndex; e.Lifecycle = credential.Lifecycle;
        e.IssuedUtc = credential.IssuedUtc.UtcDateTime; e.ExpiresUtc = credential.ExpiresUtc?.UtcDateTime;
        os.CommitChanges();
        return Task.CompletedTask;
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
        var list = os.GetObjectsQuery<IssuedCredentialEntity>().Where(x => x.HolderDid == holderDid)
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
        Id = e.ID, HolderDid = e.HolderDid ?? "", CredentialType = e.CredentialType ?? "",
        Jwt = e.Jwt ?? "", StatusListIndex = e.StatusListIndex, Lifecycle = e.Lifecycle,
        IssuedUtc = e.IssuedUtc, ExpiresUtc = e.ExpiresUtc,
    };
}
