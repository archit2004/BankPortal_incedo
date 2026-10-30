namespace BankPortal.Domain;

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "INR")
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        Amount = Round(amount);
        Currency = currency.ToUpperInvariant();
    }

    // Banker's rounding (ToEven), applied only when a Money value is created.
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.ToEven);

    public static Money Zero(string currency = "INR") => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public bool IsNegative => Amount < 0;

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException(
                $"Currency mismatch: {a.Currency} vs {b.Currency}.");
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}