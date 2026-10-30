using BankPortal.Domain;
using Xunit;

namespace BankPortal.Domain.Tests;

public class InterestScheduleTests
{
    private static (decimal Principal, decimal Maturity, int Days) Setup(decimal principal, decimal rate, int months)
    {
        var start = new DateOnly(2026, 1, 31);   // month-end start exercises date clamping
        var maturity = FdCalculator.MaturityAmount(new Money(principal), rate, months).Amount;
        var days = FdCalculator.MaturityDate(start, months).DayNumber - start.DayNumber;
        return (Money.Round(principal), maturity, days);
    }

    [Theory]
    [InlineData(100000.0, 7.0, 12)]
    [InlineData(1000.0, 6.0, 6)]
    [InlineData(2500000.0, 7.25, 24)]
    [InlineData(1234.56, 7.25, 60)]
    [InlineData(50000.0, 6.0, 11)]
    public void FullTenure_AccrualsSumToExactlyMaturityMinusPrincipal(double principal, double rate, int months)
    {
        var (p, m, days) = Setup((decimal)principal, (decimal)rate, months);

        var total = 0m;
        for (var d = 1; d <= days; d++) total += InterestSchedule.AccrualForDay(p, m, days, d);

        Assert.Equal(m - p, total);
    }

    [Fact]
    public void OutsideTenure_AccruesNothing()
    {
        var (p, m, days) = Setup(100_000m, 7m, 12);

        Assert.Equal(0m, InterestSchedule.AccrualForDay(p, m, days, 0));
        Assert.Equal(0m, InterestSchedule.AccrualForDay(p, m, days, -3));
        Assert.Equal(0m, InterestSchedule.AccrualForDay(p, m, days, days + 1));
    }

    [Fact]
    public void Accruals_AreNeverNegative()
    {
        var (p, m, days) = Setup(1_000m, 6m, 6);

        for (var d = 1; d <= days; d++)
            Assert.True(InterestSchedule.AccrualForDay(p, m, days, d) >= 0m);
    }

    [Fact]
    public void Compounding_MeansLaterDaysAccrueMore()
    {
        var (p, m, days) = Setup(100_000m, 7m, 12);

        Assert.True(InterestSchedule.AccrualForDay(p, m, days, days) > InterestSchedule.AccrualForDay(p, m, days, 1));
    }

    [Fact]
    public void FirstDayAccrual_IsTheDailyShareOfCompoundGrowth()
    {
        var (p, m, days) = Setup(100_000m, 7m, 12);

        Assert.InRange(InterestSchedule.AccrualForDay(p, m, days, 1), 19.00m, 19.03m);
    }
}