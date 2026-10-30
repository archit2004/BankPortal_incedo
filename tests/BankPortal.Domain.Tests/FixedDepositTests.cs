using BankPortal.Domain;
using BankPortal.Domain.Entities;
using Xunit;

namespace BankPortal.Domain.Tests;

public class FixedDepositTests
{
    private static FixedDeposit NewFd() =>
        FixedDeposit.Open(Guid.NewGuid(), new Money(100_000m), 7m, 12, new DateOnly(2026, 1, 1));

    [Fact]
    public void Open_ComputesMaturityDateAndAmount()
    {
        var fd = NewFd();

        Assert.Equal(FdStatus.Active, fd.Status);
        Assert.Equal(100_000m, fd.Principal);
        Assert.Equal(new DateOnly(2027, 1, 1), fd.MaturityDate);
        Assert.Equal(107_185.90m, fd.MaturityAmount);
    }

    [Fact]
    public void MarkClosed_SetsStatusDateAndPenalty()
    {
        var fd = NewFd();
        fd.MarkClosed(new DateOnly(2026, 7, 1), 250m);

        Assert.Equal(FdStatus.Closed, fd.Status);
        Assert.Equal(new DateOnly(2026, 7, 1), fd.ClosedOn);
        Assert.Equal(250m, fd.PenaltyApplied);
    }

    [Fact]
    public void MarkClosed_Twice_Throws()
    {
        var fd = NewFd();
        fd.MarkClosed(new DateOnly(2026, 7, 1), 0m);
        Assert.Throws<DomainException>(() => fd.MarkClosed(new DateOnly(2026, 7, 2), 0m));
    }

    [Fact]
    public void MarkMatured_SetsStatus_AndCannotThenBeClosed()
    {
        var fd = NewFd();
        fd.MarkMatured(new DateOnly(2027, 1, 1));

        Assert.Equal(FdStatus.Matured, fd.Status);
        Assert.Throws<DomainException>(() => fd.MarkClosed(new DateOnly(2027, 1, 2), 0m));
    }

    [Fact]
    public void Account_FdOpenThenClose_LedgerFollowsBalance()
    {
        var account = new Account(Guid.NewGuid(), "10123456789012");
        account.Deposit(100_000m, null, "k1");
        account.Apply(TransactionType.FdOpen, -40_000m, "FD x", "k2");
        Assert.Equal(60_000m, account.Balance);

        account.Apply(TransactionType.FdClose, 41_000m, "FD x", "k3");
        Assert.Equal(101_000m, account.Balance);
        Assert.Equal(
            new[] { TransactionType.Deposit, TransactionType.FdOpen, TransactionType.FdClose },
            account.Transactions.Select(t => t.Type));
    }
}