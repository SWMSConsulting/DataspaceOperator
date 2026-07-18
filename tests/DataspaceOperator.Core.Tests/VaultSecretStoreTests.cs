using System.Net;
using System.Text;
using DataspaceOperator.Core.Secrets;
using Xunit;

namespace DataspaceOperator.Core.Tests;

public class VaultSecretStoreTests
{
    // Records requests and returns canned responses keyed by path.
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Token_auth_reads_kv_v2_field()
    {
        var handler = new StubHandler(req =>
            Json("""{"data":{"data":{"seed":"c2VlZC12YWx1ZQ=="},"metadata":{}}}"""));
        var http = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(http, new VaultOptions
        {
            Address = "http://vault:8200", Token = "s.roottoken",
            KvMount = "secret", SecretPath = "dataspace-operator/issuer",
        });

        var value = await store.GetSecretAsync("seed");

        Assert.Equal("c2VlZC12YWx1ZQ==", value);
        var read = handler.Requests.Single();
        Assert.Equal("http://vault:8200/v1/secret/data/dataspace-operator/issuer", read.RequestUri!.ToString());
        Assert.Equal("s.roottoken", read.Headers.GetValues("X-Vault-Token").Single());
    }

    [Fact]
    public async Task Missing_field_returns_null()
    {
        var http = new HttpClient(new StubHandler(_ => Json("""{"data":{"data":{"other":"x"}}}""")));
        var store = new HashiCorpVaultSecretStore(http, new VaultOptions { Token = "t" });

        Assert.Null(await store.GetSecretAsync("seed"));
    }

    [Fact]
    public async Task NotFound_returns_null()
    {
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var store = new HashiCorpVaultSecretStore(http, new VaultOptions { Token = "t" });

        Assert.Null(await store.GetSecretAsync("seed"));
    }

    [Fact]
    public async Task Kubernetes_auth_logs_in_then_reads()
    {
        // fake service-account token file
        var saPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(saPath, "k8s-jwt-token");

        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/auth/kubernetes/login")
                ? Json("""{"auth":{"client_token":"s.k8s-issued"}}""")
                : Json("""{"data":{"data":{"seed":"ZnJvbS1rOHM="}}}"""));
        var http = new HttpClient(handler);
        var store = new HashiCorpVaultSecretStore(http, new VaultOptions
        {
            Address = "http://vault:8200", Token = null,
            KubernetesRole = "dataspace-operator", ServiceAccountTokenPath = saPath,
        });

        var value = await store.GetSecretAsync("seed");

        Assert.Equal("ZnJvbS1rOHM=", value);
        // logged in first, then read the secret with the issued token
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/auth/kubernetes/login", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("s.k8s-issued", handler.Requests[1].Headers.GetValues("X-Vault-Token").Single());

        File.Delete(saPath);
    }
}
