using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class AuthTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_CreatesOneEmptyAccount_AndMeShowsCustomerRole()
    {
        var user = await Factory.RegisterUserAsync("reg");

        var accounts = await (await user.Client.GetAsync("/api/accounts")).ReadOkAsync<List<AccountDto>>();
        var account = Assert.Single(accounts);
        Assert.Equal(0m, account.Balance);
        Assert.Equal("Active", account.Status);
        Assert.Equal(12, account.AccountNumber.Length);

        var me = await user.Client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Contains(me.GetProperty("roles").EnumerateArray(), r => r.GetString() == "Customer");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var user = await Factory.RegisterUserAsync("dup");

        var res = await Factory.NewClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Someone", email = user.Email, password = ApiFactory.UserPassword });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("short")]               // under 10 characters
    [InlineData("NoSymbolsHere123")]    // no symbol
    [InlineData("alllowercase!!123")]   // no upper case
    public async Task Register_WeakPassword_Returns400(string password)
    {
        var res = await Factory.NewClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Weak", email = $"weak-{Guid.NewGuid():N}@it.test", password });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        var res = await Factory.NewClient().PostAsJsonAsync("/api/auth/register",
            new { fullName = "Bad", email = "not-an-email", password = ApiFactory.UserPassword });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPasswordOrUnknownUser_Returns401_WithoutRevealingWhichOne()
    {
        var user = await Factory.RegisterUserAsync("login");
        var client = Factory.NewClient();

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong!Passw0rd#" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@it.test", password = ApiFactory.UserPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_AfterFiveFailures_LocksTheAccount_Returns423()
    {
        var user = await Factory.RegisterUserAsync("lock");
        var client = Factory.NewClient();

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "Wrong!Passw0rd#" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var good = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = ApiFactory.UserPassword });
        Assert.Equal(HttpStatusCode.Locked, good.StatusCode);
    }

    [Fact]
    public async Task Refresh_Rotates_AndReplayingAnOldTokenRevokesTheWholeSession()
    {
        var user = await Factory.RegisterUserAsync("rot");
        var client = Factory.NewClient();

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
        var rotated = await first.ReadOkAsync<AuthResult>();
        Assert.NotEqual(user.Tokens.RefreshToken, rotated.RefreshToken);

        // The original token was already used: rejected...
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...and the replay revoked the newer token too (reuse means the token was probably stolen).
        var newer = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, newer.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var user = await Factory.RegisterUserAsync("out");
        var client = Factory.NewClient();

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = user.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.Tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var res = await Factory.NewClient().GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedToken_Returns401()
    {
        var user = await Factory.RegisterUserAsync("tamper");
        var token = user.Tokens.AccessToken;
        var tampered = token[..^2] + (token.EndsWith("AA") ? "BB" : "AA");

        var client = Factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task Roles_AreSeparated_CustomerCannotUseAdmin_AdminCannotUseCustomerEndpoints()
    {
        var user = await Factory.RegisterUserAsync("role");
        var admin = await Factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/api/admin/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/accounts")).StatusCode);
    }
}