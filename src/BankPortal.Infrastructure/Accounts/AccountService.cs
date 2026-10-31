using BankPortal.Application.Accounts;
using BankPortal.Domain;
using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BankPortal.Infrastructure.Accounts;

public class AccountService(BankDbContext db) : IAccountService
{
    private const int MaxAttempts = 3;
    private const decimal MaxAmount = 10_000_000m;

    // Every query goes through this filter: a user can only ever see their own accounts.
    private IQueryable<Account> OwnedAccounts(string userId) =>
        db.Accounts.Where(a => a.Customer.UserId == userId);

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(string userId, CancellationToken ct)
    {
        var accounts = await OwnedAccounts(userId).AsNoTracking()
            .OrderBy(a => a.AccountNumber).ToListAsync(ct);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<DashboardDto> GetDashboardAsync(string userId, Guid accountId, CancellationToken ct)
    {
        var account = await OwnedAccounts(userId).AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new NotFoundException("Account not found.");

        var recent = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(10).ToListAsync(ct);

        return new DashboardDto(ToDto(account), recent.Select(ToDto).ToList());
    }

    public async Task<PagedResult<TransactionDto>> GetTransactionsAsync(
        string userId, Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (!await OwnedAccounts(userId).AnyAsync(a => a.Id == accountId, ct))
            throw new NotFoundException("Account not found.");

        var query = db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<TransactionDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public Task<TransactionDto> DepositAsync(string userId, Guid accountId, decimal amount,
        string? reference, string idempotencyKey, CancellationToken ct) =>
        ExecuteAsync(userId, accountId, amount, reference, idempotencyKey, TransactionType.Deposit, ct);

    public Task<TransactionDto> WithdrawAsync(string userId, Guid accountId, decimal amount,
        string? reference, string idempotencyKey, CancellationToken ct) =>
        ExecuteAsync(userId, accountId, amount, reference, idempotencyKey, TransactionType.Withdrawal, ct);

    private async Task<TransactionDto> ExecuteAsync(string userId, Guid accountId, decimal amount,
        string? reference, string key, TransactionType type, CancellationToken ct)
    {
        ValidateAmount(amount);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new DomainException("A valid Idempotency-Key header (max 100 characters) is required.");
        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (reference is { Length: > 200 }) throw new DomainException("Reference is too long.");

        var signed = type == TransactionType.Deposit ? amount : -amount;

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();   // fresh state on every attempt

            var account = await OwnedAccounts(userId).SingleOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new NotFoundException("Account not found.");

            // Replay of a request we already processed: return the original result, move no money.
            var existing = await db.Transactions.AsNoTracking()
                .SingleOrDefaultAsync(t => t.AccountId == accountId && t.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.Type != type || existing.Amount != signed)
                    throw new ConflictException("This Idempotency-Key was already used for a different request.");
                return ToDto(existing);
            }

            var tx = type == TransactionType.Deposit
                ? account.Deposit(amount, reference, key)
                : account.Withdraw(amount, reference, key);

            try
            {
                await db.SaveChangesAsync(ct);   // balance update + ledger row, one DB transaction
                return ToDto(tx);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Someone else changed this account first: reload and re-evaluate (balance check runs again).
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts
                && ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 })
            {
                // Same key submitted concurrently: loop again, the replay branch will find it.
            }
        }
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Amount must be positive.");
        if (amount != decimal.Round(amount, 2)) throw new DomainException("Amount cannot have more than 2 decimal places.");
        if (amount > MaxAmount) throw new DomainException($"Amount cannot exceed {MaxAmount:N0}.");
    }

    private static AccountDto ToDto(Account a) =>
        new(a.Id, a.AccountNumber, a.Balance, a.Status.ToString());

    private static TransactionDto ToDto(Transaction t) =>
        new(t.Id, t.Type.ToString(), t.Amount, t.BalanceAfter, t.Reference, t.CreatedAt);
}