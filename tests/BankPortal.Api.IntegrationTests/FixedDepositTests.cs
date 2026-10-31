using System.Net;
using System.Net.Http.Json;
using BankPortal.Application.FixedDeposits;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class FixedDepositTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task RateCard_ReturnsTheThreeSeededTiers()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("rates");

        var rates = await (await u.Client.GetAsync("/api/fixed-deposits/rates")).ReadOkAsync<List<RateDto>>();

        Assert.Equal(new[] { 6, 12, 24 }, rates.Select(r => r.MinTenureMonths));
        Assert.Equal(new[] { 6.00m, 7.00m, 7.25m }, rates.Select(r => r.AnnualRatePercent));
    }

    [Fact]
    public async Task Open_UsesTheRateCard_DebitsSavings_AndWritesTheLedger()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("open");
        await u.DepositOkAsync(200_000m);

        var fd = await u.OpenFdOkAsync(100_000m, 12);

        Assert.Equal("Active", fd.Status);
        Assert.Equal(7m, fd.AnnualRatePercent);                       // from the rate card, never from the client
        Assert.Equal(107_185.90m, fd.MaturityAmount);                 // 100000 x (1 + 0.07/4)^4
        Assert.Equal(D0, fd.StartDate);
        Assert.Equal(new DateOnly(2027, 2, 10), fd.MaturityDate);
        Assert.Equal(100_000m, await u.BalanceAsync());

        var ledger = (await u.TransactionsAsync()).Items;
        var open = ledger.First();
        Assert.Equal("FdOpen", open.Type);
        Assert.Equal(-100_000m, open.Amount);
        Assert.Equal($"FD {fd.Id}", open.Reference);
    }

    [Fact]
    public async Task Open_Replay_ReturnsTheSameFd_AndDebitsOnce()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("openreplay");
        await u.DepositOkAsync(200_000m);
        var key = TestUser.NewKey();

        var first = await (await u.OpenFdAsync(50_000m, 12, key)).ReadOkAsync<FdDto>();
        var second = await (await u.OpenFdAsync(50_000m, 12, key)).ReadOkAsync<FdDto>();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(150_000m, await u.BalanceAsync());
        Assert.Single(await u.ListFdsAsync());
    }

    [Fact]
    public async Task Open_KeyReuseWithDifferentAmountOrTenure_Returns409()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("openkey");
        await u.DepositOkAsync(200_000m);
        var key = TestUser.NewKey();
        await (await u.OpenFdAsync(50_000m, 12, key)).ReadOkAsync<FdDto>();

        Assert.Equal(HttpStatusCode.Conflict, (await u.OpenFdAsync(60_000m, 12, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await u.OpenFdAsync(50_000m, 24, key)).StatusCode);
        Assert.Equal(150_000m, await u.BalanceAsync());
    }

    [Fact]
    public async Task Open_InvalidRequests_Return400_AndMoveNoMoney()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("openbad");
        await u.DepositOkAsync(50_000m);

        Assert.Equal(HttpStatusCode.BadRequest, (await u.OpenFdAsync(5_000m, 3)).StatusCode);        // no rate for 3 months
        Assert.Equal(HttpStatusCode.BadRequest, (await u.OpenFdAsync(500m, 12)).StatusCode);         // below the minimum
        Assert.Equal(HttpStatusCode.BadRequest, (await u.OpenFdAsync(60_000m, 12)).StatusCode);      // more than the balance
        Assert.Equal(HttpStatusCode.BadRequest, (await u.OpenFdAsync(1_000.123m, 12)).StatusCode);   // 3 decimals
        Assert.Equal(HttpStatusCode.BadRequest,
            (await u.SendAsync(HttpMethod.Post, $"/api/accounts/{u.AccountId}/fixed-deposits",
                new { amount = 5_000m, tenureMonths = 12 })).StatusCode);                            // no idempotency key
        Assert.Equal(HttpStatusCode.BadRequest, (await u.CloseFdAsync(Guid.NewGuid(), "")).StatusCode);

        Assert.Equal(50_000m, await u.BalanceAsync());
        Assert.Empty(await u.ListFdsAsync());
    }

    [Fact]
    public async Task PrematureClose_After200Days_AppliesTheTierRateMinusPenalty()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("early");
        await u.DepositOkAsync(200_000m);
        var fd = await u.OpenFdOkAsync(100_000m, 12);

        Factory.Clock.SetIstDate(D0.AddDays(200));

        // 6 completed months -> the 6-11 month tier (6.00%), minus the 1% penalty = 5.00%
        var q = (await u.FdDetailAsync(fd.Id)).CloseQuote!;
        Assert.Equal(200, q.DaysHeld);
        Assert.Equal(5m, q.EffectiveRatePercent);
        Assert.False(q.InterestForfeited);
        Assert.Equal(2_760.15m, q.Interest);
        Assert.Equal(556.94m, q.Penalty);
        Assert.Equal(102_760.15m, q.Payout);

        var key = TestUser.NewKey();
        var closed = await (await u.CloseFdAsync(fd.Id, key)).ReadOkAsync<CloseResultDto>();
        Assert.Equal("Closed", closed.Fd.Status);
        Assert.Equal(102_760.15m, closed.Payout);
        Assert.Equal(556.94m, closed.Fd.PenaltyApplied);
        Assert.Equal(202_760.15m, closed.BalanceAfter);   // 100,000 left in savings + the payout
        Assert.Equal(202_760.15m, await u.BalanceAsync());

        // Same key again: same transaction, no second payout
        var replay = await (await u.CloseFdAsync(fd.Id, key)).ReadOkAsync<CloseResultDto>();
        Assert.Equal(closed.TransactionId, replay.TransactionId);
        Assert.Equal(202_760.15m, await u.BalanceAsync());

        // A new key on an already closed FD is refused
        Assert.Equal(HttpStatusCode.Conflict, (await u.CloseFdAsync(fd.Id)).StatusCode);
        Assert.Equal(202_760.15m, await u.BalanceAsync());
    }

    [Fact]
    public async Task PrematureClose_WithinSevenDays_PaysNoInterest()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("early3");
        await u.DepositOkAsync(100_000m);
        var fd = await u.OpenFdOkAsync(100_000m, 12);

        Factory.Clock.SetIstDate(D0.AddDays(3));

        var q = (await u.FdDetailAsync(fd.Id)).CloseQuote!;
        Assert.True(q.InterestForfeited);
        Assert.Equal(100_000m, q.Payout);

        var closed = await (await u.CloseFdAsync(fd.Id)).ReadOkAsync<CloseResultDto>();
        Assert.Equal(0m, closed.Interest);
        Assert.Equal(0m, closed.Penalty);
        Assert.Equal(100_000m, await u.BalanceAsync());
    }

    [Fact]
    public async Task CloseAtMaturity_PaysTheFullMaturityAmount_WithNoPenalty()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("mature");
        await u.DepositOkAsync(200_000m);
        var fd = await u.OpenFdOkAsync(100_000m, 12);

        Factory.Clock.SetIstDate(fd.MaturityDate);

        var q = (await u.FdDetailAsync(fd.Id)).CloseQuote!;
        Assert.Equal(107_185.90m, q.Payout);
        Assert.Equal(7_185.90m, q.Interest);
        Assert.Equal(0m, q.Penalty);

        var closed = await (await u.CloseFdAsync(fd.Id)).ReadOkAsync<CloseResultDto>();
        Assert.Equal("Matured", closed.Fd.Status);
        Assert.Null(closed.Fd.PenaltyApplied);
        Assert.Equal(207_185.90m, await u.BalanceAsync());
    }

    [Fact]
    public async Task Ownership_AnotherCustomerCannotSeeOpenOrCloseTheFd()
    {
        Factory.Clock.SetIstDate(D0);
        var owner = await Factory.RegisterUserAsync("fdowner");
        await owner.DepositOkAsync(50_000m);
        var fd = await owner.OpenFdOkAsync(20_000m, 12);
        var other = await Factory.RegisterUserAsync("fdother");
        await other.DepositOkAsync(50_000m);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetFdAsync(fd.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.CloseFdAsync(fd.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.SendAsync(HttpMethod.Post, $"/api/accounts/{owner.AccountId}/fixed-deposits",
                new { amount = 1_000m, tenureMonths = 12 }, TestUser.NewKey())).StatusCode);

        Assert.Equal("Active", (await owner.FdDetailAsync(fd.Id)).Fd.Status);
        Assert.Equal(30_000m, await owner.BalanceAsync());
        Assert.Equal(50_000m, await other.BalanceAsync());
    }

    [Fact]
    public async Task List_ShowsOnlyTheCallersOwnDeposits()
    {
        Factory.Clock.SetIstDate(D0);
        var a = await Factory.RegisterUserAsync("lista");
        var b = await Factory.RegisterUserAsync("listb");
        await a.DepositOkAsync(10_000m);
        var fd = await a.OpenFdOkAsync(2_000m, 12);

        var mine = await a.ListFdsAsync();
        Assert.Equal(fd.Id, Assert.Single(mine).Id);
        Assert.Empty(await b.ListFdsAsync());
    }

    [Fact]
    public async Task FiveParallelOpens_CompetingForTheBalance_NeverSpendMoreThanTheBalance()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("parfd");
        await u.DepositOkAsync(100_000m);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => u.OpenFdWithRetryAsync(30_000m, 12, TestUser.NewKey()))));

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.BadRequest));
        Assert.Equal(10_000m, await u.BalanceAsync());

        var fds = await u.ListFdsAsync();
        Assert.Equal(3, fds.Count);
        Assert.Equal(90_000m, fds.Sum(f => f.Principal));
    }
}