using System.Net;
using BankPortal.Application.Accounts;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class AccountTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Deposit_ThenWithdraw_UpdatesBalanceAndLedger()
    {
        var u = await Factory.RegisterUserAsync("acc");

        var dep = await u.DepositOkAsync(1000.50m, "salary");
        Assert.Equal("Deposit", dep.Type);
        Assert.Equal(1000.50m, dep.Amount);
        Assert.Equal(1000.50m, dep.BalanceAfter);

        var wd = await u.WithdrawOkAsync(250.25m);
        Assert.Equal(-250.25m, wd.Amount);
        Assert.Equal(750.25m, wd.BalanceAfter);

        var dash = await u.DashboardAsync();
        Assert.Equal(750.25m, dash.Account.Balance);
        Assert.Equal(new[] { "Withdrawal", "Deposit" }, dash.RecentTransactions.Select(t => t.Type));
    }

    [Fact]
    public async Task Replay_WithSameIdempotencyKey_ReturnsSameTransaction_AndMovesNoMoney()
    {
        var u = await Factory.RegisterUserAsync("replay");
        var key = TestUser.NewKey();

        var first = await (await u.DepositAsync(100m, key)).ReadOkAsync<TransactionDto>();
        var second = await (await u.DepositAsync(100m, key)).ReadOkAsync<TransactionDto>();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(100m, await u.BalanceAsync());
        Assert.Equal(1, (await u.TransactionsAsync()).TotalCount);
    }

    [Fact]
    public async Task KeyReuse_WithDifferentAmount_Returns409()
    {
        var u = await Factory.RegisterUserAsync("keyamt");
        var key = TestUser.NewKey();
        await (await u.DepositAsync(100m, key)).ReadOkAsync<TransactionDto>();

        var res = await u.DepositAsync(101m, key);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(100m, await u.BalanceAsync());
    }

    [Fact]
    public async Task KeyReuse_ForDifferentOperationType_Returns409()
    {
        var u = await Factory.RegisterUserAsync("keytype");
        var key = TestUser.NewKey();
        await (await u.DepositAsync(100m, key)).ReadOkAsync<TransactionDto>();

        var res = await u.WithdrawAsync(100m, key);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal(100m, await u.BalanceAsync());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(10.123)]        // more than 2 decimals
    [InlineData(10000000.01)]   // above the 10,000,000 cap
    public async Task Deposit_InvalidAmount_Returns400(double amount)
    {
        var u = await Factory.RegisterUserAsync("badamt");

        var res = await u.DepositAsync((decimal)amount);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0m, await u.BalanceAsync());
    }

    [Fact]
    public async Task Deposit_WithoutIdempotencyKey_Returns400()
    {
        var u = await Factory.RegisterUserAsync("nokey");

        var res = await u.SendAsync(HttpMethod.Post, $"/api/accounts/{u.AccountId}/deposit", new { amount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0m, await u.BalanceAsync());
    }

    [Fact]
    public async Task Deposit_WithOverlongIdempotencyKey_Returns400()
    {
        var u = await Factory.RegisterUserAsync("longkey");

        var res = await u.DepositAsync(10m, new string('k', 101));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Withdraw_MoreThanBalance_Returns400_ButExactBalanceSucceeds()
    {
        var u = await Factory.RegisterUserAsync("overdraw");
        await u.DepositOkAsync(100m);

        var tooMuch = await u.WithdrawAsync(100.01m);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal(100m, await u.BalanceAsync());

        await u.WithdrawOkAsync(100m);
        Assert.Equal(0m, await u.BalanceAsync());
    }

    [Fact]
    public async Task Ownership_AnotherCustomerGets404_AndCannotMoveMoney()
    {
        var owner = await Factory.RegisterUserAsync("owner");
        await owner.DepositOkAsync(500m);
        var intruder = await Factory.RegisterUserAsync("intruder");
        var id = owner.AccountId;

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync($"/api/accounts/{id}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync($"/api/accounts/{id}/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.SendAsync(HttpMethod.Post, $"/api/accounts/{id}/deposit", new { amount = 10m }, TestUser.NewKey())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.SendAsync(HttpMethod.Post, $"/api/accounts/{id}/withdraw", new { amount = 10m }, TestUser.NewKey())).StatusCode);

        // A random id looks exactly the same as someone else's id
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.GetAsync($"/api/accounts/{Guid.NewGuid()}/dashboard")).StatusCode);

        Assert.Equal(500m, await owner.BalanceAsync());
    }

    [Fact]
    public async Task Transactions_ArePaged_NewestFirst()
    {
        var u = await Factory.RegisterUserAsync("page");
        for (var i = 1; i <= 5; i++) await u.DepositOkAsync(i);

        var page1 = await u.TransactionsAsync(1, 2);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(new[] { 5m, 4m }, page1.Items.Select(t => t.Amount));

        Assert.Single((await u.TransactionsAsync(3, 2)).Items);
        Assert.Empty((await u.TransactionsAsync(4, 2)).Items);
    }

    [Fact]
    public async Task Transactions_PageSizeIsCappedAt100()
    {
        var u = await Factory.RegisterUserAsync("clamp");
        await u.DepositOkAsync(1m);

        var page = await u.TransactionsAsync(1, 1000);

        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task TwentyParallelWithdrawals_NeverOverdraw_AndTheLedgerMatchesTheBalance()
    {
        var u = await Factory.RegisterUserAsync("par");
        await u.DepositOkAsync(1000m);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => u.MoneyWithRetryAsync("withdraw", 100m, TestUser.NewKey()))));

        // 1000 buys exactly ten withdrawals of 100; the other ten must be refused for insufficient funds.
        Assert.Equal(10, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(10, results.Count(r => r.Status == HttpStatusCode.BadRequest));
        Assert.Equal(0m, await u.BalanceAsync());

        var ledger = (await u.TransactionsAsync()).Items;
        Assert.Equal(11, ledger.Count);   // 1 deposit + 10 withdrawals
        var withdrawals = ledger.Where(t => t.Type == "Withdrawal").ToList();
        Assert.Equal(10, withdrawals.Count);

        // Each success saw a different running balance: 900, 800, ... 0. No two requests spent the same money.
        Assert.Equal(
            Enumerable.Range(0, 10).Select(i => i * 100m),
            withdrawals.Select(t => t.BalanceAfter).OrderBy(x => x));
    }

    [Fact]
    public async Task TenParallelRequestsWithTheSameKey_MoveMoneyExactlyOnce()
    {
        var u = await Factory.RegisterUserAsync("dupkey");
        var key = TestUser.NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => u.MoneyWithRetryAsync("deposit", 100m, key))));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(results.Select(r => r.Tx!.Id).Distinct());
        Assert.Equal(100m, await u.BalanceAsync());
        Assert.Equal(1, (await u.TransactionsAsync()).TotalCount);
    }
}