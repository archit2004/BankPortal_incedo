namespace BankPortal.Domain;

public sealed record PrematureCloseResult(
    Money Payout,
    Money Interest,
    decimal EffectiveRatePercent,
    int DaysHeld,
    bool InterestForfeited);

public static class FdCalculator
{
    public static Money MaturityAmount(
        Money principal, decimal annualRatePercent, int tenureMonths, int compoundingPerYear = 4)
    {
        Validate(principal, annualRatePercent, compoundingPerYear);
        if (tenureMonths <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenureMonths), "Tenure must be positive.");

        var years = tenureMonths / 12.0;
        return principal + CompoundInterest(principal, annualRatePercent, years, compoundingPerYear);
    }

    public static DateOnly MaturityDate(DateOnly startDate, int tenureMonths)
    {
        if (tenureMonths <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenureMonths), "Tenure must be positive.");
        return startDate.AddMonths(tenureMonths);
    }

    public static PrematureCloseResult PrematureClose(
        Money principal,
        decimal applicableRatePercent,   // rate-card rate for the period actually completed
        DateOnly startDate,
        DateOnly closeDate,
        int compoundingPerYear = 4,
        decimal penaltyPercent = 1m,
        int minDaysForInterest = 7)
    {
        Validate(principal, applicableRatePercent, compoundingPerYear);
        if (closeDate < startDate)
            throw new ArgumentException("Close date cannot be before start date.", nameof(closeDate));

        var days = closeDate.DayNumber - startDate.DayNumber;

        if (days < minDaysForInterest)
            return new PrematureCloseResult(principal, Money.Zero(principal.Currency), 0m, days, true);

        var effectiveRate = Math.Max(0m, applicableRatePercent - penaltyPercent);
        var interest = CompoundInterest(principal, effectiveRate, days / 365.0, compoundingPerYear);

        return new PrematureCloseResult(principal + interest, interest, effectiveRate, days, false);
    }

    // Fractional exponents need Math.Pow (double). We convert back to decimal and
    // round once, at the end, inside Money.
    private static Money CompoundInterest(Money principal, decimal ratePercent, double years, int n)
    {
        if (ratePercent <= 0m || years <= 0) return Money.Zero(principal.Currency);

        var factor = Math.Pow(1 + (double)ratePercent / 100.0 / n, n * years);
        var maturity = principal.Amount * (decimal)factor;
        return new Money(maturity - principal.Amount, principal.Currency);
    }

    private static void Validate(Money principal, decimal ratePercent, int n)
    {
        if (principal.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(principal), "Principal must be positive.");
        if (ratePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(ratePercent), "Rate cannot be negative.");
        if (n <= 0)
            throw new ArgumentOutOfRangeException(nameof(n), "Compounding frequency must be positive.");
    }
}