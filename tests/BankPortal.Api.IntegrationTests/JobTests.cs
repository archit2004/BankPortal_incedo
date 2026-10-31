using System.Net;
using BankPortal.Application.FixedDeposits;
using BankPortal.Application.Jobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class JobTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<JobRunResult> RunJob(HttpClient admin, string? date = null)
    {
        var res = await admin.PostAsync("/api/admin/jobs/run" + (date is null ? "" : $"?date={date}"), null);
        return await res.ReadOkAsync<JobRunResult>();
    }

    [Fact]
    public async Task Job_AccruesDaily_IsRerunnable_AndMaturesExactlyOnce()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("job");
        await u.DepositOkAsync(200_000m);
        var fd = await u.OpenFdOkAsync(100_000m, 12);
        var admin = await Factory.AdminClientAsync();
        var totalDays = fd.MaturityDate.DayNumber - fd.StartDate.DayNumber;

        // Day 30: thirty accrual rows, and a second run changes nothing
        Factory.Clock.SetIstDate(D0.AddDays(30));
        var run1 = await RunJob(admin);
        Assert.Equal(0, run1.Failures);
        Assert.Equal(30, await Factory.CountAccrualsAsync(fd.Id));

        var run2 = await RunJob(admin);
        Assert.Equal(0, run2.Failures);
        Assert.Equal(0, run2.AccrualsCreated);
        Assert.Equal(0, run2.FdsMatured);
        Assert.Equal(30, await Factory.CountAccrualsAsync(fd.Id));
        Assert.Equal(100_000m, await u.BalanceAsync());   // not matured yet

        // Maturity day: backfills the remaining days, pays out once, marks the FD Matured
        Factory.Clock.SetIstDate(fd.MaturityDate);
        var run3 = await RunJob(admin);
        Assert.Equal(0, run3.Failures);
        Assert.True(run3.FdsMatured >= 1);

        Assert.Equal(totalDays, await Factory.CountAccrualsAsync(fd.Id));
        // Compound-consistent accrual: the daily rows add up to exactly maturity minus principal
        Assert.Equal(fd.MaturityAmount - fd.Principal, await Factory.SumAccrualsAsync(fd.Id));
        Assert.Equal("Matured", (await u.FdDetailAsync(fd.Id)).Fd.Status);
        Assert.Equal(207_185.90m, await u.BalanceAsync());

        // Running again pays nothing more and creates nothing
        var run4 = await RunJob(admin);
        Assert.Equal(0, run4.AccrualsCreated);
        Assert.Equal(0, run4.FdsMatured);
        Assert.Equal(0, run4.Failures);
        Assert.Equal(207_185.90m, await u.BalanceAsync());

        var entity = $"FixedDeposit:{fd.Id}";
        Assert.Equal(1, await Factory.WithDbAsync(db =>
            db.AuditLog.CountAsync(a => a.Action == "FD_MATURED" && a.Entity == entity)));
        Assert.Equal(1, await Factory.WithDbAsync(db =>
            db.Transactions.CountAsync(t => t.IdempotencyKey == "maturity-" + fd.Id)));
    }

    [Fact]
    public async Task Job_AfterTheCustomerClosedAtMaturity_FillsAccrualsButNeverPaysTwice()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("jobclosed");
        await u.DepositOkAsync(200_000m);
        var fd = await u.OpenFdOkAsync(100_000m, 12);
        var admin = await Factory.AdminClientAsync();
        var totalDays = fd.MaturityDate.DayNumber - fd.StartDate.DayNumber;

        Factory.Clock.SetIstDate(fd.MaturityDate);
        await (await u.CloseFdAsync(fd.Id)).ReadOkAsync<CloseResultDto>();
        Assert.Equal(207_185.90m, await u.BalanceAsync());

        var run = await RunJob(admin);
        Assert.Equal(0, run.Failures);

        Assert.Equal(totalDays, await Factory.CountAccrualsAsync(fd.Id));
        Assert.Equal(207_185.90m, await u.BalanceAsync());   // no second credit

        var entity = $"FixedDeposit:{fd.Id}";
        Assert.Equal(0, await Factory.WithDbAsync(db =>
            db.AuditLog.CountAsync(a => a.Action == "FD_MATURED" && a.Entity == entity)));
    }

    [Fact]
    public async Task ManualTrigger_RejectsBadInput_AndCustomers()
    {
        Factory.Clock.SetIstDate(D0);
        var admin = await Factory.AdminClientAsync();
        var customer = await Factory.RegisterUserAsync("jobauth");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/jobs/run?date=2999-01-01", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/jobs/run?date=31-10-2026", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.PostAsync("/api/admin/jobs/run", null)).StatusCode);
    }
}