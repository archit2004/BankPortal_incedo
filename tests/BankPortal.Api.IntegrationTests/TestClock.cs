namespace BankPortal.Api.IntegrationTests;

// Lets tests choose the bank's "today". Unset = real time.
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset? _fixed;

    public override DateTimeOffset GetUtcNow() => _fixed ?? DateTimeOffset.UtcNow;

    // 06:30 UTC is 12:00 IST, so the bank's date is exactly this date.
    public void SetIstDate(DateOnly date) =>
        _fixed = new DateTimeOffset(date.ToDateTime(new TimeOnly(6, 30)), TimeSpan.Zero);

    public void Reset() => _fixed = null;
}