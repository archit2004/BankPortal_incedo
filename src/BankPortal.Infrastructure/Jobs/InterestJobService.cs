using BankPortal.Application.Jobs;
using BankPortal.Domain;
using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankPortal.Infrastructure.Jobs;

public class InterestJobService(BankDbContext db, TimeProvider clock, ILogger<InterestJobService> log)
    : IInterestJobService
{
    private const int MaxAttempts = 3;

    private readonly record struct FdOutcome(int Accruals, decimal Interest, bool Matured, decimal Paid);

    public async Task<JobRunResult> RunAsync(DateOnly date, string actorId, CancellationToken ct)
    {
        if (date > BankCalendar.Today(clock))
            throw new DomainException("The job date cannot be in the future.");

        db.ChangeTracker.Clear();

        // Active FDs, plus matured FDs (closed by the customer) whose accrual history is still incomplete.
        var ids = await db.FixedDeposits.AsNoTracking()
            .Where(f => f.StartDate < date &&
                (f.Status == FdStatus.Active ||
                 (f.Status == FdStatus.Matured &&
                  !db.InterestAccruals.Any(a => a.FixedDepositId == f.Id && a.AccrualDate == f.MaturityDate))))
            .Select(f => f.Id)
            .ToListAsync(ct);

        int created = 0, matured = 0, failures = 0;
        decimal interest = 0m, paid = 0m;

        foreach (var id in ids)
        {
            try
            {
                var o = await ProcessFdAsync(id, date, ct);
                created += o.Accruals;
                interest += o.Interest;
                if (o.Matured) { matured++; paid += o.Paid; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failures++;
                log.LogError(ex, "Interest job failed for fixed deposit {FdId}", id);
            }
        }

        var result = new JobRunResult(date, ids.Count, created, interest, matured, paid, failures);
        log.LogInformation("Interest job {Date}: {Result}", date, result);

        if (actorId != JobActors.Scheduler)
        {
            db.ChangeTracker.Clear();
            db.AuditLog.Add(new AuditLogEntry(actorId, "JOB_RUN", "InterestJob",
                $"date={date:yyyy-MM-dd}; processed={result.FdsProcessed}; accruals={result.AccrualsCreated}; " +
                $"matured={result.FdsMatured}; failures={result.Failures}"));
            await db.SaveChangesAsync(ct);
        }

        return result;
    }

    private async Task<FdOutcome> ProcessFdAsync(Guid fdId, DateOnly asOf, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();

            var fd = await db.FixedDeposits.SingleOrDefaultAsync(f => f.Id == fdId, ct);
            if (fd is null || fd.Status is not (FdStatus.Active or FdStatus.Matured))
                return default;

            var isActive = fd.Status == FdStatus.Active;
            var totalDays = fd.MaturityDate.DayNumber - fd.StartDate.DayNumber;

            var last = await db.InterestAccruals.Where(a => a.FixedDepositId == fdId)
                .MaxAsync(a => (DateOnly?)a.AccrualDate, ct);
            var lastDay = last is null ? 0 : last.Value.DayNumber - fd.StartDate.DayNumber;
            var throughDay = Math.Min(asOf.DayNumber - fd.StartDate.DayNumber, totalDays);

            var created = 0;
            var interest = 0m;
            for (var day = lastDay + 1; day <= throughDay; day++)
            {
                var amount = InterestSchedule.AccrualForDay(fd.Principal, fd.MaturityAmount, totalDays, day);
                db.InterestAccruals.Add(new InterestAccrual(fd.Id, fd.StartDate.AddDays(day), amount));
                created++;
                interest += amount;
            }

            var matured = false;
            var paid = 0m;
            if (isActive && asOf >= fd.MaturityDate)
            {
                var account = await db.Accounts.SingleAsync(a => a.Id == fd.AccountId, ct);
                fd.MarkMatured(asOf);
                account.Apply(TransactionType.FdClose, fd.MaturityAmount, $"FD {fd.Id}", $"maturity-{fd.Id}");
                db.AuditLog.Add(new AuditLogEntry(JobActors.Scheduler, "FD_MATURED", $"FixedDeposit:{fd.Id}",
                    $"Credited {fd.MaturityAmount:F2} to account {account.AccountNumber}"));
                matured = true;
                paid = fd.MaturityAmount;
            }

            if (created == 0 && !matured) return default;

            try
            {
                await db.SaveChangesAsync(ct);   // accruals + maturity credit + audit row, one DB transaction
                return new FdOutcome(created, interest, matured, paid);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts) { }
            catch (DbUpdateException ex) when (attempt < MaxAttempts
                && ex.InnerException is SqlException { Number: 1205 or 2601 or 2627 }) { }
        }
    }
}