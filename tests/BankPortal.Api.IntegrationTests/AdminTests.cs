using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BankPortal.Application.Accounts;
using BankPortal.Application.Admin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankPortal.Api.IntegrationTests;

[Collection("api")]
public class AdminTests(ApiFactory factory) : IntegrationTestBase(factory)
{
    private static async Task<DepositsReportDto> Report(HttpClient admin) =>
        await (await admin.GetAsync("/api/admin/reports/deposits")).ReadOkAsync<DepositsReportDto>();

    [Fact]
    public async Task Customers_ListShowsTotals_AndSearchFindsOnlyMatches()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("adm");
        await u.DepositOkAsync(5_000m);
        await u.OpenFdOkAsync(2_000m, 12);
        var admin = await Factory.AdminClientAsync();

        var page = await (await admin.GetAsync($"/api/admin/customers?search={Uri.EscapeDataString(u.Email)}"))
            .ReadOkAsync<PagedResult<AdminCustomerDto>>();

        var c = Assert.Single(page.Items);
        Assert.Equal(u.Email, c.Email);
        Assert.Equal(1, c.AccountCount);
        Assert.Equal(3_000m, c.SavingsBalance);
        Assert.Equal(1, c.ActiveFdCount);
        Assert.Equal(2_000m, c.ActiveFdPrincipal);
        Assert.Equal(5_000m, c.TotalDeposits);

        var none = await (await admin.GetAsync("/api/admin/customers?search=zz-no-such-customer-zz"))
            .ReadOkAsync<PagedResult<AdminCustomerDto>>();
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task DepositsReport_OpeningAnFd_MovesMoneyWithoutChangingTotalDeposits()
    {
        Factory.Clock.SetIstDate(D0);
        var admin = await Factory.AdminClientAsync();
        var before = await Report(admin);

        var u = await Factory.RegisterUserAsync("rep");
        await u.DepositOkAsync(7_000m);
        var afterDeposit = await Report(admin);

        Assert.Equal(before.CustomerCount + 1, afterDeposit.CustomerCount);
        Assert.Equal(before.SavingsBalance + 7_000m, afterDeposit.SavingsBalance);
        Assert.Equal(before.TotalDeposits + 7_000m, afterDeposit.TotalDeposits);

        await u.OpenFdOkAsync(3_000m, 12);
        var afterFd = await Report(admin);

        Assert.Equal(afterDeposit.TotalDeposits, afterFd.TotalDeposits);
        Assert.Equal(afterDeposit.SavingsBalance - 3_000m, afterFd.SavingsBalance);
        Assert.Equal(afterDeposit.ActiveFdPrincipal + 3_000m, afterFd.ActiveFdPrincipal);
        Assert.Equal(afterDeposit.ActiveFdCount + 1, afterFd.ActiveFdCount);
        Assert.Equal(afterFd.SavingsBalance + afterFd.ActiveFdPrincipal, afterFd.TotalDeposits);
    }

    [Fact]
    public async Task FixedDeposits_StatusFilterWorks_AndBadStatusReturns400()
    {
        Factory.Clock.SetIstDate(D0);
        var u = await Factory.RegisterUserAsync("admfd");
        await u.DepositOkAsync(10_000m);
        var fd = await u.OpenFdOkAsync(2_000m, 12);
        var admin = await Factory.AdminClientAsync();

        var active = await (await admin.GetAsync("/api/admin/fixed-deposits?status=Active&pageSize=100"))
            .ReadOkAsync<PagedResult<AdminFdDto>>();
        Assert.All(active.Items, i => Assert.Equal("Active", i.Status));
        var mine = Assert.Single(active.Items, i => i.Id == fd.Id);
        Assert.Equal(365, mine.DaysToMaturity!.Value);

        Factory.Clock.SetIstDate(D0.AddDays(10));
        (await u.CloseFdAsync(fd.Id)).EnsureSuccessStatusCode();

        var activeAfter = await (await admin.GetAsync("/api/admin/fixed-deposits?status=Active&pageSize=100"))
            .ReadOkAsync<PagedResult<AdminFdDto>>();
        Assert.DoesNotContain(activeAfter.Items, i => i.Id == fd.Id);

        var closed = await (await admin.GetAsync("/api/admin/fixed-deposits?status=Closed&pageSize=100"))
            .ReadOkAsync<PagedResult<AdminFdDto>>();
        Assert.Contains(closed.Items, i => i.Id == fd.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/fixed-deposits?status=Bogus")).StatusCode);
    }

    [Fact]
    public async Task ViewingCustomerData_WritesAnAuditRowWithTheAdminsId()
    {
        var admin = await Factory.AdminClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("userId").GetString()!;

        Task<int> Count() => Factory.WithDbAsync(db =>
            db.AuditLog.CountAsync(a => a.Action == "VIEW_CUSTOMERS" && a.ActorId == adminId));

        var before = await Count();
        (await admin.GetAsync("/api/admin/customers")).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, await Count());
    }

    [Fact]
    public async Task AdminEndpoints_RequireTheAdminRole()
    {
        var customer = await Factory.RegisterUserAsync("admauth");
        var anonymous = Factory.NewClient();
        string[] urls = ["/api/admin/customers", "/api/admin/fixed-deposits", "/api/admin/reports/deposits"];

        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Paging_IsClamped()
    {
        var admin = await Factory.AdminClientAsync();

        var page = await (await admin.GetAsync("/api/admin/customers?page=0&pageSize=1000"))
            .ReadOkAsync<PagedResult<AdminCustomerDto>>();

        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
    }
}