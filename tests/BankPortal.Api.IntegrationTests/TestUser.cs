using System.Net;
using System.Net.Http.Json;
using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using BankPortal.Application.FixedDeposits;

namespace BankPortal.Api.IntegrationTests;

// A registered customer with an authenticated client and helpers for the common calls.
public sealed class TestUser(string email, HttpClient client, Guid accountId, AuthResult tokens)
{
    public string Email { get; } = email;
    public HttpClient Client { get; } = client;
    public Guid AccountId { get; } = accountId;
    public AuthResult Tokens { get; } = tokens;

    public static string NewKey() => Guid.NewGuid().ToString();

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? key = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return Client.SendAsync(req);
    }

    // ----- savings -----
    public Task<HttpResponseMessage> DepositAsync(decimal amount, string? key = null, string? reference = null) =>
        SendAsync(HttpMethod.Post, $"/api/accounts/{AccountId}/deposit", new { amount, reference }, key ?? NewKey());

    public Task<HttpResponseMessage> WithdrawAsync(decimal amount, string? key = null, string? reference = null) =>
        SendAsync(HttpMethod.Post, $"/api/accounts/{AccountId}/withdraw", new { amount, reference }, key ?? NewKey());

    public async Task<TransactionDto> DepositOkAsync(decimal amount, string? reference = null) =>
        await (await DepositAsync(amount, null, reference)).ReadOkAsync<TransactionDto>();

    public async Task<TransactionDto> WithdrawOkAsync(decimal amount, string? reference = null) =>
        await (await WithdrawAsync(amount, null, reference)).ReadOkAsync<TransactionDto>();

    public async Task<DashboardDto> DashboardAsync() =>
        await (await Client.GetAsync($"/api/accounts/{AccountId}/dashboard")).ReadOkAsync<DashboardDto>();

    public async Task<decimal> BalanceAsync() => (await DashboardAsync()).Account.Balance;

    public async Task<PagedResult<TransactionDto>> TransactionsAsync(int page = 1, int pageSize = 100) =>
        await (await Client.GetAsync($"/api/accounts/{AccountId}/transactions?page={page}&pageSize={pageSize}"))
            .ReadOkAsync<PagedResult<TransactionDto>>();

    // Retries only on 409 (lost an optimistic-concurrency race), always with the SAME idempotency key,
    // exactly as a well-behaved client would.
    public async Task<(HttpStatusCode Status, TransactionDto? Tx)> MoneyWithRetryAsync(
        string kind, decimal amount, string key, int maxAttempts = 200)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            var res = await SendAsync(HttpMethod.Post, $"/api/accounts/{AccountId}/{kind}", new { amount }, key);
            if (res.StatusCode == HttpStatusCode.Conflict)
            {
                await Task.Delay(Random.Shared.Next(2, 25));
                continue;
            }
            var tx = res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<TransactionDto>() : null;
            return (res.StatusCode, tx);
        }
        return (HttpStatusCode.Conflict, null);
    }

    // ----- fixed deposits -----
    public Task<HttpResponseMessage> OpenFdAsync(decimal amount, int tenureMonths, string? key = null) =>
        SendAsync(HttpMethod.Post, $"/api/accounts/{AccountId}/fixed-deposits", new { amount, tenureMonths }, key ?? NewKey());

    public async Task<FdDto> OpenFdOkAsync(decimal amount, int tenureMonths) =>
        await (await OpenFdAsync(amount, tenureMonths)).ReadOkAsync<FdDto>();

    public async Task<HttpStatusCode> OpenFdWithRetryAsync(decimal amount, int tenureMonths, string key, int maxAttempts = 200)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            var res = await OpenFdAsync(amount, tenureMonths, key);
            if (res.StatusCode == HttpStatusCode.Conflict)
            {
                await Task.Delay(Random.Shared.Next(2, 25));
                continue;
            }
            return res.StatusCode;
        }
        return HttpStatusCode.Conflict;
    }

    public Task<HttpResponseMessage> GetFdAsync(Guid id) => Client.GetAsync($"/api/fixed-deposits/{id}");

    public async Task<FdDetailDto> FdDetailAsync(Guid id) => await (await GetFdAsync(id)).ReadOkAsync<FdDetailDto>();

    public Task<HttpResponseMessage> CloseFdAsync(Guid id, string? key = null) =>
        SendAsync(HttpMethod.Post, $"/api/fixed-deposits/{id}/close", null, key ?? NewKey());

    public async Task<List<FdDto>> ListFdsAsync() =>
        await (await Client.GetAsync("/api/fixed-deposits")).ReadOkAsync<List<FdDto>>();
}