namespace BankPortal.Domain.Entities;

public enum TransactionType { Deposit = 1, Withdrawal = 2, FdOpen = 3, FdClose = 4, Interest = 5 }

public class Transaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }          // signed: debit negative, credit positive
    public decimal BalanceAfter { get; private set; }
    public string? Reference { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Transaction() { }

    internal Transaction(Guid accountId, TransactionType type, decimal amount,
        decimal balanceAfter, string? reference, string? idempotencyKey)
    {
        AccountId = accountId;
        Type = type;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Reference = reference;
        IdempotencyKey = idempotencyKey;
    }
}