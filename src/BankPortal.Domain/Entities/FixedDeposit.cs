namespace BankPortal.Domain.Entities;

public enum FdStatus { Active = 1, Matured = 2, Closed = 3 }

public class FixedDeposit
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AccountId { get; private set; }
    public decimal Principal { get; private set; }
    public decimal AnnualRatePercent { get; private set; }
    public int TenureMonths { get; private set; }
    public int CompoundingPerYear { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly MaturityDate { get; private set; }
    public decimal MaturityAmount { get; private set; }
    public FdStatus Status { get; private set; } = FdStatus.Active;
    public DateOnly? ClosedOn { get; private set; }
    public decimal? PenaltyApplied { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    public Account Account { get; private set; } = default!;

    private FixedDeposit() { }

    public static FixedDeposit Open(Guid accountId, Money principal, decimal annualRatePercent,
        int tenureMonths, DateOnly startDate, int compoundingPerYear = 4)
    {
        var maturityAmount = FdCalculator.MaturityAmount(principal, annualRatePercent, tenureMonths, compoundingPerYear);
        return new FixedDeposit
        {
            AccountId = accountId,
            Principal = principal.Amount,
            AnnualRatePercent = annualRatePercent,
            TenureMonths = tenureMonths,
            CompoundingPerYear = compoundingPerYear,
            StartDate = startDate,
            MaturityDate = FdCalculator.MaturityDate(startDate, tenureMonths),
            MaturityAmount = maturityAmount.Amount
        };
    }

    public void MarkClosed(DateOnly closedOn, decimal penaltyApplied)
    {
        if (Status != FdStatus.Active) throw new DomainException("FD is not active.");
        Status = FdStatus.Closed;
        ClosedOn = closedOn;
        PenaltyApplied = penaltyApplied;
    }

    public void MarkMatured(DateOnly onDate)
    {
        if (Status != FdStatus.Active) throw new DomainException("FD is not active.");
        Status = FdStatus.Matured;
        ClosedOn = onDate;
    }
}