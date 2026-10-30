namespace BankPortal.Domain.Entities;

public enum AccountStatus { Active = 1, Frozen = 2, Closed = 3 }

public class Account
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public string AccountNumber { get; private set; } = default!;
    public decimal Balance { get; private set; }
    public AccountStatus Status { get; private set; } = AccountStatus.Active;
    public byte[] RowVersion { get; private set; } = default!;   // optimistic concurrency token

    public Customer Customer { get; private set; } = default!;
    public List<Transaction> Transactions { get; private set; } = new();

    private Account() { }

    public Account(Guid customerId, string accountNumber)
    {
        CustomerId = customerId;
        AccountNumber = accountNumber;
    }

    public Transaction Deposit(decimal amount, string? reference, string? idempotencyKey)
    {
        EnsureActive();
        if (amount <= 0) throw new DomainException("Deposit amount must be positive.");
        Balance += amount;
        return Record(TransactionType.Deposit, amount, reference, idempotencyKey);
    }

    public Transaction Withdraw(decimal amount, string? reference, string? idempotencyKey)
    {
        EnsureActive();
        if (amount <= 0) throw new DomainException("Withdrawal amount must be positive.");
        if (amount > Balance) throw new DomainException("Insufficient funds.");
        Balance -= amount;
        return Record(TransactionType.Withdrawal, -amount, reference, idempotencyKey);
    }

    // Used by FD open/close and interest credit later. Signed amount: debit negative, credit positive.
    public Transaction Apply(TransactionType type, decimal signedAmount, string? reference, string? idempotencyKey)
    {
        EnsureActive();
        if (Balance + signedAmount < 0) throw new DomainException("Insufficient funds.");
        Balance += signedAmount;
        return Record(type, signedAmount, reference, idempotencyKey);
    }

    private Transaction Record(TransactionType type, decimal signedAmount, string? reference, string? key)
    {
        var tx = new Transaction(Id, type, signedAmount, Balance, reference, key);
        Transactions.Add(tx);
        return tx;
    }

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
            throw new DomainException($"Account is {Status}.");
    }
}