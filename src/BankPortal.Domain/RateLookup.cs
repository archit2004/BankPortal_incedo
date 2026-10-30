using BankPortal.Domain.Entities;

namespace BankPortal.Domain;

public static class BankCalendar
{
    // India has no DST, so IST is a fixed UTC+5:30.
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(330));
}

public static class RateLookup
{
    // Rate paid on premature closure when the FD was held less than the card's minimum tenure.
    public const decimal BelowMinimumRatePercent = 3.00m;

    public static decimal? FindRate(IEnumerable<RateCardEntry> card, int tenureMonths, DateOnly on) =>
        card.Where(e => e.MinTenureMonths <= tenureMonths && tenureMonths <= e.MaxTenureMonths && e.EffectiveFrom <= on)
            .OrderByDescending(e => e.EffectiveFrom)
            .Select(e => (decimal?)e.AnnualRatePercent)
            .FirstOrDefault();

    public static int CompletedMonths(DateOnly start, DateOnly end)
    {
        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (start.AddMonths(months) > end) months--;
        return Math.Max(months, 0);
    }

    // Rate for the period actually completed (card in force when the FD was opened), never above the contracted rate.
    public static decimal PrematureApplicableRate(
        IEnumerable<RateCardEntry> card, decimal contractedRatePercent, DateOnly start, DateOnly close)
    {
        var months = CompletedMonths(start, close);
        var tier = FindRate(card, months, start) ?? BelowMinimumRatePercent;
        return Math.Min(contractedRatePercent, tier);
    }
}