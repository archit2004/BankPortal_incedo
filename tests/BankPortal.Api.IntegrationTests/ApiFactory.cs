using System.Net.Http.Headers;
using System.Net.Http.Json;
using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using BankPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

// Boots the real API in memory against a dedicated SQL Server database that is recreated on every run.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DbName = "BankPortal_IntegrationTests";
    public const string AdminEmail = "it-admin@bankportal.test";
    public const string AdminPassword = "IntegrationAdmin#2026";
    public const string UserPassword = "Str0ng!Passw0rd#";

    public static readonly string ConnectionString;

    public TestClock Clock { get; } = new();

    static ApiFactory()
    {
        ConnectionString = BuildConnectionString();

        // Environment variables override appsettings and user secrets, so the app can never
        // fall back to the development database while the tests run.
        Env("ConnectionStrings__Default", ConnectionString);
        Env("Jwt__Key", "integration-tests-signing-key-0123456789-abcdef");
        Env("Jwt__Issuer", "BankPortal");
        Env("Jwt__Audience", "BankPortal.Web");
        Env("Jobs__Enabled", "false");                       // the tests trigger the job explicitly
        Env("RateLimit__AuthPermitPerMinute", "100000");
        Env("Seed__AdminEmail", AdminEmail);
        Env("Seed__AdminPassword", AdminPassword);
        Env("Logging__LogLevel__Default", "Warning");
    }

    private static void Env(string name, string value) => Environment.SetEnvironmentVariable(name, value);

    // Same server and credentials as development (user secrets), or BANKPORTAL_IT_CONNECTION in CI,
    // always pointed at the integration-test database.
    private static string BuildConnectionString()
    {
        var baseCs = Environment.GetEnvironmentVariable("BANKPORTAL_IT_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets(typeof(Program).Assembly, optional: true)
                .Build()["ConnectionStrings:Default"];

        if (string.IsNullOrWhiteSpace(baseCs))
            throw new InvalidOperationException(
                "No SQL Server connection string found. Set the ConnectionStrings:Default user secret on " +
                "BankPortal.Api, or the BANKPORTAL_IT_CONNECTION environment variable.");

        return new SqlConnectionStringBuilder(baseCs) { InitialCatalog = DbName }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var db = new BankDbContext(options))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        // Safety net: refuse to run if the app is not pointed at the test database.
        using var scope = Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<BankDbContext>().Database.GetDbConnection().Database;
        if (!string.Equals(actual, DbName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Integration tests are pointed at '{actual}', not '{DbName}'. Aborting.");
    }

    // WebApplicationFactory already implements IAsyncDisposable, so the xunit hook is explicit.
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // the Development branch seeds roles and the admin user
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task<TestUser> RegisterUserAsync(string prefix = "user")
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@it.test";
        var client = NewClient();
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Integration User", email, password = UserPassword });
        var tokens = await res.ReadOkAsync<AuthResult>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var accounts = await (await client.GetAsync("/api/accounts")).ReadOkAsync<List<AccountDto>>();
        return new TestUser(email, client, accounts[0].Id, tokens);
    }

    public async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var tokens = await res.ReadOkAsync<AuthResult>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<BankDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<BankDbContext>());
    }

    public Task<int> CountAccrualsAsync(Guid fdId) =>
        WithDbAsync(db => db.InterestAccruals.CountAsync(a => a.FixedDepositId == fdId));

    public Task<decimal> SumAccrualsAsync(Guid fdId) =>
        WithDbAsync(db => db.InterestAccruals.Where(a => a.FixedDepositId == fdId).SumAsync(a => a.Amount));
}