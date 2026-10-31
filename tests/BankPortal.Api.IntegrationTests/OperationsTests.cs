using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class OperationsTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Live_Returns200()
    {
        var res = await Factory.NewClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns200_WhenTheDatabaseIsReachable()
    {
        var res = await Factory.NewClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task CorrelationId_IsEchoedWhenValid_AndReplacedWhenInvalid()
    {
        var client = Factory.NewClient();

        var good = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        good.Headers.Add("X-Correlation-Id", "test-corr-123");
        var goodRes = await client.SendAsync(good);
        Assert.Equal("test-corr-123", goodRes.Headers.GetValues("X-Correlation-Id").Single());

        var bad = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        bad.Headers.Add("X-Correlation-Id", "bad id with spaces!");
        var badRes = await client.SendAsync(bad);
        var replaced = badRes.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("bad id with spaces!", replaced);
        Assert.Equal(32, replaced.Length);

        var none = await client.GetAsync("/health/live");
        Assert.Equal(32, none.Headers.GetValues("X-Correlation-Id").Single().Length);
    }

    [Fact]
    public async Task ErrorBodies_CarryTheSameCorrelationIdAsTheResponseHeader()
    {
        var u = await Factory.RegisterUserAsync("corr");

        var res = await u.Client.GetAsync($"/api/accounts/{Guid.NewGuid()}/dashboard");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(res.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
    }
}