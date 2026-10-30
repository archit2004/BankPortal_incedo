namespace BankPortal.Domain;

// Compound-consistent daily accrual: value on day d = P * (M/P)^(d/D), rounded once per day.
// Accruals are differences of rounded cumulative values, so they sum to exactly M - P.
public static class InterestSchedule
{
    public static decimal ValueOnDay(decimal principal, decimal maturityAmount, int totalDays, int day)
    {
        if (day <= 0) return principal;
        if (day >= totalDays) return maturityAmount;
        var growth = Math.Pow((double)(maturityAmount / principal), (double)day / totalDays);
        return Money.Round(principal * (decimal)growth);
    }

    public static decimal AccrualForDay(decimal principal, decimal maturityAmount, int totalDays, int day)
    {
        if (totalDays <= 0 || day < 1 || day > totalDays) return 0m;
        return ValueOnDay(principal, maturityAmount, totalDays, day)
             - ValueOnDay(principal, maturityAmount, totalDays, day - 1);
    }
}