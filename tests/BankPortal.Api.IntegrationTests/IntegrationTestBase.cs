using Xunit;

namespace BankPortal.Api.IntegrationTests;

// One API + one database shared by every test class. Classes in a collection run one at a time,
// so tests may compare before/after totals without interference.
[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }

public abstract class IntegrationTestBase : IDisposable
{
    // A fixed bank date for tests that need exact figures (rate card is effective from 2026-01-01).
    protected static readonly DateOnly D0 = new(2026, 2, 10);

    protected ApiFactory Factory { get; }

    protected IntegrationTestBase(ApiFactory factory)
    {
        Factory = factory;
        Factory.Clock.Reset();
    }

    public void Dispose() => Factory.Clock.Reset();
}