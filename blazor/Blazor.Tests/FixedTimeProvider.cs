namespace Blazor.Tests;

// A clock that stands still until a test moves it, for the code that reads the current time from the registered TimeProvider
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
