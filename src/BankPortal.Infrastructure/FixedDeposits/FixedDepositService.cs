using BankPortal.Application.Accounts;
using BankPortal.Application.FixedDeposits;
using BankPortal.Domain;
using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BankPortal.Infrastructure.FixedDeposits;

public class FixedDepositService(BankDbContext db, TimeProvider clock) : IFixedDepositService
{
    private const int MaxAttempts = 3;
    private const decimal MinAmount = 1_000m;
    private const decimal MaxAmount = 100_000_000m;

    private IQueryable<Account> OwnedAccounts(string userId) =>
        db.Accounts.Where(a => a.Customer.UserId == userId);

    private IQueryable<FixedDeposit> OwnedFds(string userId) =>
        db.FixedDeposits.Where(f => f.Account.Customer.UserId == userId);

    public async Task<IReadOnlyList<RateDto>> GetRatesAsync(CancellationToken ct)
    {
        var today = BankCalendar.Today(clock);
        var card = await db.RateCard.AsNoTracking().ToListAsync(ct);
        return card.Where(e => e.EffectiveFrom <= today)
            .GroupBy(e => (e.MinTenureMonths, e.MaxTenureMonths))
            .Select(g => g.OrderByDescending(e => e.EffectiveFrom).First())
            .OrderBy(e => e.MinTenureMonths)
            .Select(e => new RateDto(e.MinTenureMonths, e.MaxTenureMonths, e.AnnualRatePercent, e.EffectiveFrom))
            .ToList();
    }

    public async Task<IReadOnlyList<FdDto>> ListAsync(string userId, CancellationToken ct)
    {
        var fds = await OwnedFds(userId).AsNoTracking()
            .OrderByDescending(f => f.StartDate).ThenByDescending(f => f.Id)
            .ToListAsync(ct);
        return fds.Select(ToDto).ToList();
    }

    public async Task<FdDetailDto> GetAsync(string userId, Guid fdId, CancellationToken ct)
    {
        var fd = await OwnedFds(userId).AsNoTracking().SingleOrDefaultAsync(f => f.Id == fdId, ct)
            ?? throw new NotFoundException("Fixed deposit not found.");

        PrematureQuoteDto? quote = null;
        if (fd.Status == FdStatus.Active)
        {
            var today = BankCalendar.Today(clock);
            var card = await db.RateCard.AsNoTracking().ToListAsync(ct);
            var q = ComputeClose(fd, card, today);
            quote = new PrematureQuoteDto(q.Payout, q.Interest, q.Penalty, q.EffectiveRate, q.Days, q.Forfeited, today);
        }
        return new FdDetailDto(ToDto(fd), quote);
    }

    public async Task<FdDto> OpenAsync(string userId, Guid accountId, decimal amount, int tenureMonths,
        string key, CancellationToken ct)
    {
        ValidateAmount(amount);
        ValidateKey(key);

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();

            var account = await OwnedAccounts(userId).SingleOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new NotFoundException("Account not found.");

            var existing = await db.Transactions.AsNoTracking()
                .SingleOrDefaultAsync(t => t.AccountId == accountId && t.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.Type != TransactionType.FdOpen || existing.Amount != -amount)
                    throw new ConflictException("This Idempotency-Key was already used for a different request.");

                var fdId = ParseFdId(existing.Reference)
                    ?? throw new ConflictException("This Idempotency-Key was already used for a different request.");
                var created = await db.FixedDeposits.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fdId, ct)
                    ?? throw new ConflictException("This Idempotency-Key was already used for a different request.");
                if (created.TenureMonths != tenureMonths)
                    throw new ConflictException("This Idempotency-Key was already used for a different request.");
                return ToDto(created);
            }

            var today = BankCalendar.Today(clock);
            var card = await db.RateCard.AsNoTracking().ToListAsync(ct);
            var rate = RateLookup.FindRate(card, tenureMonths, today)
                ?? throw new DomainException("No interest rate is available for this tenure.");

            var fd = FixedDeposit.Open(account.Id, new Money(amount), rate, tenureMonths, today);
            account.Apply(TransactionType.FdOpen, -amount, $"FD {fd.Id}", key);   // debit + ledger row
            db.FixedDeposits.Add(fd);

            try
            {
                await db.SaveChangesAsync(ct);   // balance, ledger row and FD in one DB transaction
                return ToDto(fd);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts) { }
            catch (DbUpdateException ex) when (attempt < MaxAttempts
                && ex.InnerException is SqlException { Number: 2601 or 2627 }) { }
        }
    }

    public async Task<CloseResultDto> CloseAsync(string userId, Guid fdId, string key, CancellationToken ct)
    {
        ValidateKey(key);

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();

            var fd = await OwnedFds(userId).SingleOrDefaultAsync(f => f.Id == fdId, ct)
                ?? throw new NotFoundException("Fixed deposit not found.");
            var account = await db.Accounts.SingleAsync(a => a.Id == fd.AccountId, ct);
            var reference = $"FD {fd.Id}";

            var existing = await db.Transactions.AsNoTracking()
                .SingleOrDefaultAsync(t => t.AccountId == account.Id && t.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.Type != TransactionType.FdClose || existing.Reference != reference)
                    throw new ConflictException("This Idempotency-Key was already used for a different request.");
                return new CloseResultDto(ToDto(fd), existing.Amount, existing.Amount - fd.Principal,
                    fd.PenaltyApplied ?? 0m, existing.Id, existing.BalanceAfter);
            }

            if (fd.Status != FdStatus.Active)
                throw new ConflictException("This fixed deposit is already closed.");

            var today = BankCalendar.Today(clock);
            var card = await db.RateCard.AsNoTracking().ToListAsync(ct);
            var q = ComputeClose(fd, card, today);

            if (today >= fd.MaturityDate) fd.MarkMatured(today);
            else fd.MarkClosed(today, q.Penalty);

            var tx = account.Apply(TransactionType.FdClose, q.Payout, reference, key);   // credit + ledger row

            try
            {
                await db.SaveChangesAsync(ct);
                return new CloseResultDto(ToDto(fd), q.Payout, q.Interest, q.Penalty, tx.Id, tx.BalanceAfter);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts) { }
            catch (DbUpdateException ex) when (attempt < MaxAttempts
                && ex.InnerException is SqlException { Number: 2601 or 2627 }) { }
        }
    }

    private sealed record Quote(decimal Payout, decimal Interest, decimal Penalty, decimal EffectiveRate, int Days, bool Forfeited);

    private static Quote ComputeClose(FixedDeposit fd, IReadOnlyCollection<RateCardEntry> card, DateOnly today)
    {
        var days = today.DayNumber - fd.StartDate.DayNumber;

        if (today >= fd.MaturityDate)
            return new Quote(fd.MaturityAmount, fd.MaturityAmount - fd.Principal, 0m, fd.AnnualRatePercent, days, false);

        var rate = RateLookup.PrematureApplicableRate(card, fd.AnnualRatePercent, fd.StartDate, today);
        var principal = new Money(fd.Principal);
        var actual = FdCalculator.PrematureClose(principal, rate, fd.StartDate, today, fd.CompoundingPerYear);
        var noPenalty = FdCalculator.PrematureClose(principal, rate, fd.StartDate, today, fd.CompoundingPerYear, penaltyPercent: 0m);
        var penalty = noPenalty.Interest.Amount - actual.Interest.Amount;

        return new Quote(actual.Payout.Amount, actual.Interest.Amount, penalty,
            actual.EffectiveRatePercent, actual.DaysHeld, actual.InterestForfeited);
    }

    private static Guid? ParseFdId(string? reference) =>
        reference is not null && reference.StartsWith("FD ") && Guid.TryParse(reference.AsSpan(3), out var id) ? id : null;

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new DomainException("A valid Idempotency-Key header (max 100 characters) is required.");
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount != decimal.Round(amount, 2)) throw new DomainException("Amount cannot have more than 2 decimal places.");
        if (amount < MinAmount) throw new DomainException($"Minimum deposit amount is {MinAmount:N0}.");
        if (amount > MaxAmount) throw new DomainException($"Maximum deposit amount is {MaxAmount:N0}.");
    }

    private static FdDto ToDto(FixedDeposit f) =>
        new(f.Id, f.AccountId, f.Principal, f.AnnualRatePercent, f.TenureMonths, f.CompoundingPerYear,
            f.StartDate, f.MaturityDate, f.MaturityAmount, f.Status.ToString(), f.ClosedOn, f.PenaltyApplied);
}