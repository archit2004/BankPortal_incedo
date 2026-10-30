using BankPortal.Application.Accounts;

namespace BankPortal.Application.Admin;

public record AdminCustomerDto(
    Guid Id, string FullName, string Email, DateTime CreatedAt, int AccountCount,
    decimal SavingsBalance, int ActiveFdCount, decimal ActiveFdPrincipal, decimal TotalDeposits);

public record AdminFdDto(
    Guid Id, string CustomerName, string AccountNumber, decimal Principal, decimal AnnualRatePercent,
    int TenureMonths, DateOnly StartDate, DateOnly MaturityDate, int? DaysToMaturity,
    decimal MaturityAmount, string Status);

public record DepositsReportDto(
    DateOnly AsOf, int CustomerCount, int AccountCount, decimal SavingsBalance,
    int ActiveFdCount, decimal ActiveFdPrincipal, decimal TotalDeposits,
    decimal AccruedInterestOnActiveFds);

public interface IAdminService
{
    Task<PagedResult<AdminCustomerDto>> GetCustomersAsync(string actorId, string? search, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<AdminFdDto>> GetFixedDepositsAsync(string actorId, string? status, int page, int pageSize, CancellationToken ct);
    Task<DepositsReportDto> GetDepositsReportAsync(CancellationToken ct);
}