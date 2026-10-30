using BankPortal.Domain;
using Xunit;

namespace BankPortal.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Addition_SameCurrency_Sums()
    {
        var result = new Money(100.10m) + new Money(50.20m);
        Assert.Equal(new Money(150.30m), result);
    }

    [Fact]
    public void Addition_DifferentCurrency_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Money(1m, "INR") + new Money(1m, "USD"));
    }

    [Theory]
    [InlineData(2.345, 2.34)]   // midpoint rounds to even (4)
    [InlineData(2.355, 2.36)]   // midpoint rounds to even (6)
    [InlineData(2.341, 2.34)]
    [InlineData(2.349, 2.35)]
    public void Construction_RoundsBankers(double input, double expected)
    {
        Assert.Equal((decimal)expected, new Money((decimal)input).Amount);
    }

    [Fact]
    public void Subtraction_CanGoNegative_AndReportsIt()
    {
        var result = new Money(10m) - new Money(25m);
        Assert.True(result.IsNegative);
    }

    [Fact]
    public void Currency_IsNormalizedToUpperCase()
    {
        Assert.Equal("INR", new Money(1m, "inr").Currency);
    }
}