using BankPortal.Domain;
using BankPortal.Domain.Entities;
using Xunit;

namespace BankPortal.Domain.Tests;

public class RateLookupTests
{
    private static readonly DateOnly D2026 = new(2026, 1, 1);

    private static readonly RateCardEntry[] Card =
    {
        new(6, 11, 6.00m, D2026),
        new(12, 23, 7.00m, D2026),
        new(24, 60, 7.25m, D2026),
    };

    [Theory]
    [InlineData(6, 6.00)]
    [InlineData(12, 7.00)]
    [InlineData(23, 7.00)]
    [InlineData(24, 7.25)]
    [InlineData(60, 7.25)]
    public void FindRate_ReturnsTierRate(int tenure, double expected) =>
        Assert.Equal((decimal)expected, RateLookup.FindRate(Card, tenure, new DateOnly(2026, 6, 1)));

    [Theory]
    [InlineData(5)]
    [InlineData(61)]
    public void FindRate_OutsideTiers_ReturnsNull(int tenure) =>
        Assert.Null(RateLookup.FindRate(Card, tenure, new DateOnly(2026, 6, 1)));

    [Fact]
    public void FindRate_UsesLatestEffectiveEntry_AndIgnoresFutureOnes()
    {
        var card = Card.Append(new RateCardEntry(12, 23, 8.00m, new DateOnly(2027, 1, 1))).ToArray();

        Assert.Equal(7.00m, RateLookup.FindRate(card, 12, new DateOnly(2026, 6, 1)));
        Assert.Equal(8.00m, RateLookup.FindRate(card, 12, new DateOnly(2027, 2, 1)));
    }

    [Theory]
    [InlineData("2026-01-31", "2026-02-28", 1)]
    [InlineData("2026-01-15", "2026-07-14", 5)]
    [InlineData("2026-01-15", "2026-07-15", 6)]
    [InlineData("2026-01-15", "2026-01-20", 0)]
    public void CompletedMonths_CountsWholeMonthsOnly(string start, string end, int expected) =>
        Assert.Equal(expected, RateLookup.CompletedMonths(DateOnly.Parse(start), DateOnly.Parse(end)));

    [Fact]
    public void PrematureRate_UsesTierForCompletedMonths()
    {
        var rate = RateLookup.PrematureApplicableRate(Card, 7.25m, D2026, new DateOnly(2026, 8, 15));
        Assert.Equal(6.00m, rate);   // 7 months completed -> 6-11 month tier
    }

    [Fact]
    public void PrematureRate_BelowMinimumTenure_FallsBackToBaseRate()
    {
        var rate = RateLookup.PrematureApplicableRate(Card, 7.25m, D2026, new DateOnly(2026, 3, 1));
        Assert.Equal(RateLookup.BelowMinimumRatePercent, rate);
    }

    [Fact]
    public void PrematureRate_NeverExceedsContractedRate()
    {
        var rate = RateLookup.PrematureApplicableRate(Card, 6.00m, D2026, new DateOnly(2027, 3, 1));
        Assert.Equal(6.00m, rate);   // tier says 7.00, contract says 6.00
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void BankCalendar_Today_IsIstDate()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 1, 2), BankCalendar.Today(clock));   // 20:00 UTC = 01:30 IST next day
    }
}