namespace BankPortal.Application.Jobs;

public static class JobActors
{
    public const string Scheduler = "system";
}

public record JobRunResult(
    DateOnly Date, int FdsProcessed, int AccrualsCreated, decimal InterestAccrued,
    int FdsMatured, decimal MaturityPaidOut, int Failures);

public interface IInterestJobService
{
    Task<JobRunResult> RunAsync(DateOnly date, string actorId, CancellationToken ct);
}