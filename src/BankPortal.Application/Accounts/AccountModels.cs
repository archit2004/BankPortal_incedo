using System.ComponentModel.DataAnnotations;

namespace BankPortal.Application.Accounts;

public record AccountDto(Guid Id, string AccountNumber, decimal Balance, string Status);

public record TransactionDto(
    Guid Id, string Type, decimal Amount, decimal BalanceAfter, string? Reference, DateTime CreatedAt);

public record DashboardDto(AccountDto Account, IReadOnlyList<TransactionDto> RecentTransactions);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public class MoneyRequest
{
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    [MaxLength(200)] public string? Reference { get; set; }
}

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(string userId, CancellationToken ct);
    Task<DashboardDto> GetDashboardAsync(string userId, Guid accountId, CancellationToken ct);
    Task<PagedResult<TransactionDto>> GetTransactionsAsync(string userId, Guid accountId, int page, int pageSize, CancellationToken ct);
    Task<TransactionDto> DepositAsync(string userId, Guid accountId, decimal amount, string? reference, string idempotencyKey, CancellationToken ct);
    Task<TransactionDto> WithdrawAsync(string userId, Guid accountId, decimal amount, string? reference, string idempotencyKey, CancellationToken ct);
}