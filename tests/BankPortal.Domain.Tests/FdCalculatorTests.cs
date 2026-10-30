using BankPortal.Domain;
using Xunit;

namespace BankPortal.Domain.Tests;

public class FdCalculatorTests
{
    [Fact]
    public void Maturity_QuarterlyCompounding_OneYear()
    {
        var result = FdCalculator.MaturityAmount(new Money(100_000m), 7m, 12, 4);
        Assert.Equal(107_185.90m, result.Amount); // 100000 × (1 + 0.07/4)^4
    }

    [Fact]
    public void Maturity_ZeroRate_ReturnsPrincipal()
    {
        var result = FdCalculator.MaturityAmount(new Money(50_000m), 0m, 12);
        Assert.Equal(50_000m, result.Amount);
    }

    [Fact]
    public void Maturity_HigherCompoundingFrequency_PaysMore()
    {
        var quarterly = FdCalculator.MaturityAmount(new Money(100_000m), 7m, 12, 4);
        var monthly   = FdCalculator.MaturityAmount(new Money(100_000m), 7m, 12, 12);
        Assert.True(monthly.Amount > quarterly.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-6)]
    public void Maturity_NonPositiveTenure_Throws(int months)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FdCalculator.MaturityAmount(new Money(1000m), 7m, months));
    }

    [Fact]
    public void Maturity_NonPositivePrincipal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FdCalculator.MaturityAmount(new Money(0m), 7m, 12));
    }

    [Theory]
    [InlineData("2026-01-31", 1, "2026-02-28")]   // month-end clamping
    [InlineData("2026-03-15", 12, "2027-03-15")]
    [InlineData("2024-02-29", 12, "2025-02-28")]  // leap-day start
    public void MaturityDate_AddsMonths(string start, int months, string expected)
    {
        var result = FdCalculator.MaturityDate(DateOnly.Parse(start), months);
        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Fact]
    public void PrematureClose_WithinMinimumDays_ForfeitsInterest()
    {
        var start = new DateOnly(2026, 1, 1);
        var result = FdCalculator.PrematureClose(new Money(100_000m), 6.5m, start, start.AddDays(6));

        Assert.True(result.InterestForfeited);
        Assert.Equal(100_000m, result.Payout.Amount);
        Assert.Equal(0m, result.Interest.Amount);
    }

    [Fact]
    public void PrematureClose_AppliesPenaltyToRate()
    {
        var start = new DateOnly(2026, 1, 1);
        var result = FdCalculator.PrematureClose(new Money(100_000m), 6.5m, start, start.AddDays(180));

        Assert.False(result.InterestForfeited);
        Assert.Equal(5.5m, result.EffectiveRatePercent);
        Assert.True(result.Interest.Amount > 0m);
    }

    [Fact]
    public void PrematureClose_PenaltyNeverMakesRateNegative()
    {
        var start = new DateOnly(2026, 1, 1);
        var result = FdCalculator.PrematureClose(new Money(100_000m), 0.5m, start, start.AddDays(90));

        Assert.Equal(0m, result.EffectiveRatePercent);
        Assert.Equal(100_000m, result.Payout.Amount);
    }

    [Fact]
    public void PrematureClose_PaysLessThanFullMaturity()
    {
        var start = new DateOnly(2026, 1, 1);
        var principal = new Money(100_000m);

        var early = FdCalculator.PrematureClose(principal, 7m, start, start.AddDays(300));
        var full  = FdCalculator.MaturityAmount(principal, 7m, 12);

        Assert.True(early.Payout.Amount < full.Amount);
    }

    [Fact]
    public void PrematureClose_CloseBeforeStart_Throws()
    {
        var start = new DateOnly(2026, 1, 10);
        Assert.Throws<ArgumentException>(
            () => FdCalculator.PrematureClose(new Money(1000m), 7m, start, start.AddDays(-1)));
    }
}