using BankPortal.Application.Accounts;
using BankPortal.Application.Admin;
using BankPortal.Domain;
using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BankPortal.Infrastructure.Admin;

public class AdminService(BankDbContext db, TimeProvider clock) : IAdminService
{
    public async Task<PagedResult<AdminCustomerDto>> GetCustomersAsync(
        string actorId, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search is { Length: > 100 }) throw new DomainException("Search text is too long.");

        var query = db.Customers.AsNoTracking();
        if (search is not null)
            query = query.Where(c => c.FullName.Contains(search) || c.Email.Contains(search));

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(c => c.FullName).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FullName, c.Email, c.CreatedAt,
                AccountCount = c.Accounts.Count(),
                Savings = c.Accounts.Sum(a => (decimal?)a.Balance) ?? 0m,
                FdCount = db.FixedDeposits.Count(f => f.Account.CustomerId == c.Id && f.Status == FdStatus.Active),
                FdPrincipal = db.FixedDeposits
                    .Where(f => f.Account.CustomerId == c.Id && f.Status == FdStatus.Active)
                    .Sum(f => (decimal?)f.Principal) ?? 0m
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AdminCustomerDto(
            r.Id, r.FullName, r.Email, r.CreatedAt, r.AccountCount,
            r.Savings, r.FdCount, r.FdPrincipal, r.Savings + r.FdPrincipal)).ToList();

        await AuditAsync(actorId, "VIEW_CUSTOMERS", "Customer",
            $"search={search ?? "-"}; page={page}; pageSize={pageSize}; results={items.Count}", ct);

        return new PagedResult<AdminCustomerDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<AdminFdDto>> GetFixedDepositsAsync(
        string actorId, string? status, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        FdStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<FdStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
                throw new DomainException("Status must be Active, Matured, Closed or All.");
            filter = parsed;
        }

        var query = db.FixedDeposits.AsNoTracking();
        if (filter is { } st) query = query.Where(f => f.Status == st);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderBy(f => f.MaturityDate).ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new
            {
                f.Id,
                CustomerName = f.Account.Customer.FullName,
                f.Account.AccountNumber,
                f.Principal, f.AnnualRatePercent, f.TenureMonths,
                f.StartDate, f.MaturityDate, f.MaturityAmount, f.Status
            })
            .ToListAsync(ct);

        var today = BankCalendar.Today(clock);
        var items = rows.Select(r => new AdminFdDto(
            r.Id, r.CustomerName, r.AccountNumber, r.Principal, r.AnnualRatePercent, r.TenureMonths,
            r.StartDate, r.MaturityDate,
            r.Status == FdStatus.Active ? r.MaturityDate.DayNumber - today.DayNumber : null,
            r.MaturityAmount, r.Status.ToString())).ToList();

        await AuditAsync(actorId, "VIEW_FIXED_DEPOSITS", "FixedDeposit",
            $"status={filter?.ToString() ?? "All"}; page={page}; pageSize={pageSize}; results={items.Count}", ct);

        return new PagedResult<AdminFdDto>(items, page, pageSize, total);
    }

    public async Task<DepositsReportDto> GetDepositsReportAsync(CancellationToken ct)
    {
        var customers = await db.Customers.CountAsync(ct);
        var accounts = await db.Accounts.CountAsync(ct);
        var savings = await db.Accounts.SumAsync(a => (decimal?)a.Balance, ct) ?? 0m;

        var active = db.FixedDeposits.Where(f => f.Status == FdStatus.Active);
        var fdCount = await active.CountAsync(ct);
        var fdPrincipal = await active.SumAsync(f => (decimal?)f.Principal, ct) ?? 0m;

        var accrued = await db.InterestAccruals
            .Where(a => db.FixedDeposits.Any(f => f.Id == a.FixedDepositId && f.Status == FdStatus.Active))
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        return new DepositsReportDto(
            BankCalendar.Today(clock), customers, accounts, savings,
            fdCount, fdPrincipal, savings + fdPrincipal, accrued);
    }

    private async Task AuditAsync(string actorId, string action, string entity, string details, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry(actorId, action, entity, details));
        await db.SaveChangesAsync(ct);
    }
}