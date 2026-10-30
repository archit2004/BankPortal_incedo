namespace BankPortal.Domain.Entities;

public class InterestAccrual
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FixedDepositId { get; private set; }
    public DateOnly AccrualDate { get; private set; }
    public decimal Amount { get; private set; }

    private InterestAccrual() { }

    public InterestAccrual(Guid fixedDepositId, DateOnly accrualDate, decimal amount)
    {
        FixedDepositId = fixedDepositId;
        AccrualDate = accrualDate;
        Amount = amount;
    }
}
