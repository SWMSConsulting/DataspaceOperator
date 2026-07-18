using DataspaceOperator.Core.Abstractions;
using DataspaceOperator.Core.Crypto;
using DataspaceOperator.Core.Secrets;
using Microsoft.Extensions.Configuration;

namespace DataspaceOperator.Endpoints;

/// <summary>
/// Chooses the secret store and builds the issuer key provider. If <c>Vault:Enabled</c> is true the
/// issuer seed is read from HashiCorp Vault; otherwise from configuration (env/user-secrets/appsettings).
/// </summary>
public static class SecretStores
{
    public static Task<IIssuerKeyProvider> BuildIssuerKeyProviderAsync(
        IConfiguration config, HttpClient http, string issuerDid, CancellationToken ct = default)
    {
        if (config.GetValue<bool>("Vault:Enabled"))
        {
            var options = new VaultOptions
            {
                Address = config["Vault:Address"] ?? "http://127.0.0.1:8200",
                Token = config["Vault:Token"],
                KvMount = config["Vault:KvMount"] ?? "secret",
                SecretPath = config["Vault:SecretPath"] ?? "dataspace-operator/issuer",
                KubernetesRole = config["Vault:KubernetesRole"],
                KubernetesAuthMount = config["Vault:KubernetesAuthMount"] ?? "kubernetes",
                Namespace = config["Vault:Namespace"],
            };
            var field = config["Vault:SeedField"] ?? "seed";
            var store = new HashiCorpVaultSecretStore(http, options);
            return IssuerKeyFactory.CreateAsync(store, issuerDid, field, ct);
        }

        return IssuerKeyFactory.CreateAsync(
            new ConfigurationSecretStore(config), issuerDid, IssuerKeyFactory.DefaultSecretName, ct);
    }

    /// <summary>
    /// Builds the issuer signer. If <c>Vault:Transit:Enabled</c> the private key stays in Vault
    /// (Transit signing); otherwise a local Ed25519 key from the seed (Vault KV or configuration).
    /// </summary>
    public static async Task<IIssuerSigner> BuildIssuerSignerAsync(
        IConfiguration config, HttpClient http, string issuerDid, CancellationToken ct = default)
    {
        if (config.GetValue<bool>("Vault:Transit:Enabled"))
        {
            var options = new VaultTransitOptions
            {
                Connection = new VaultConnection
                {
                    Address = config["Vault:Transit:Address"] ?? config["Vault:Address"] ?? "http://127.0.0.1:8200",
                    Token = config["Vault:Transit:Token"] ?? config["Vault:Token"],
                    KubernetesRole = config["Vault:Transit:KubernetesRole"] ?? config["Vault:KubernetesRole"],
                    KubernetesAuthMount = config["Vault:Transit:KubernetesAuthMount"] ?? config["Vault:KubernetesAuthMount"] ?? "kubernetes",
                    Namespace = config["Vault:Transit:Namespace"] ?? config["Vault:Namespace"],
                },
                Mount = config["Vault:Transit:Mount"] ?? "transit",
                KeyName = config["Vault:Transit:KeyName"] ?? "dataspace-issuer",
            };
            return await VaultTransitSigner.CreateAsync(http, options, issuerDid, ct: ct);
        }

        var keyProvider = await BuildIssuerKeyProviderAsync(config, http, issuerDid, ct);
        return LocalEd25519Signer.FromKeyProvider(keyProvider);
    }
}
