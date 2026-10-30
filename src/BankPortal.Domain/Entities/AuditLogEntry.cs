namespace BankPortal.Domain.Entities;

public class RateCardEntry
{
    public int Id { get; private set; }
    public int MinTenureMonths { get; private set; }
    public int MaxTenureMonths { get; private set; }
    public decimal AnnualRatePercent { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }

    private RateCardEntry() { }

    public RateCardEntry(int min, int max, decimal rate, DateOnly effectiveFrom)
    {
        MinTenureMonths = min;
        MaxTenureMonths = max;
        AnnualRatePercent = rate;
        EffectiveFrom = effectiveFrom;
    }
}

public class AuditLogEntry
{
    public long Id { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string Action { get; private set; } = default!;
    public string Entity { get; private set; } = default!;
    public string? Details { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private AuditLogEntry() { }

    public AuditLogEntry(string actorId, string action, string entity, string? details)
    {
        ActorId = actorId; Action = action; Entity = entity; Details = details;
    }
}
