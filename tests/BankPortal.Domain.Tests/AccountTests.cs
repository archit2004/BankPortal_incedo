using BankPortal.Domain;
using BankPortal.Domain.Entities;
using Xunit;

namespace BankPortal.Domain.Tests;

public class AccountTests
{
    private static Account NewAccount() => new(Guid.NewGuid(), "10123456789012");

    [Fact]
    public void Deposit_IncreasesBalance_AndLogsTransaction()
    {
        var account = NewAccount();
        var tx = account.Deposit(500m, "salary", "k1");

        Assert.Equal(500m, account.Balance);
        Assert.Equal(TransactionType.Deposit, tx.Type);
        Assert.Equal(500m, tx.Amount);
        Assert.Equal(500m, tx.BalanceAfter);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public void Withdraw_DecreasesBalance_AndLogsNegativeAmount()
    {
        var account = NewAccount();
        account.Deposit(500m, null, "k1");
        var tx = account.Withdraw(200m, null, "k2");

        Assert.Equal(300m, account.Balance);
        Assert.Equal(-200m, tx.Amount);
        Assert.Equal(300m, tx.BalanceAfter);
    }

    [Fact]
    public void Withdraw_MoreThanBalance_Throws_AndLeavesStateUntouched()
    {
        var account = NewAccount();
        account.Deposit(100m, null, "k1");

        Assert.Throws<DomainException>(() => account.Withdraw(100.01m, null, "k2"));
        Assert.Equal(100m, account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public void Withdraw_ExactBalance_Succeeds()
    {
        var account = NewAccount();
        account.Deposit(100m, null, "k1");
        account.Withdraw(100m, null, "k2");
        Assert.Equal(0m, account.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveAmounts_Throw(int amount)
    {
        var account = NewAccount();
        Assert.Throws<DomainException>(() => account.Deposit(amount, null, null));
        Assert.Throws<DomainException>(() => account.Withdraw(amount, null, null));
    }

    [Fact]
    public void Apply_DebitBeyondBalance_Throws()
    {
        var account = NewAccount();
        Assert.Throws<DomainException>(() => account.Apply(TransactionType.FdOpen, -1m, null, null));
    }

    [Fact]
    public void Ledger_BalanceAfter_FollowsRunningBalance()
    {
        var account = NewAccount();
        account.Deposit(1000m, null, "a");
        account.Withdraw(250m, null, "b");
        account.Deposit(50m, null, "c");

        Assert.Equal(new[] { 1000m, 750m, 800m }, account.Transactions.Select(t => t.BalanceAfter));
    }
}