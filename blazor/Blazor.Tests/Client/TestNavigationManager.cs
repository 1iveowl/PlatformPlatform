using Microsoft.AspNetCore.Components;

namespace Blazor.Tests.Client;

public sealed record RecordedNavigation(string Uri, bool ForceLoad);

// Records navigations instead of performing them
public sealed class TestNavigationManager : NavigationManager
{
    public const string BaseAddress = "https://app.dev.localhost:9000/";
    public const string CurrentUri = $"{BaseAddress}blazor/development/form-errors/interactive?tab=forms";

    public TestNavigationManager()
    {
        Initialize(BaseAddress, CurrentUri);
    }

    public List<RecordedNavigation> Navigations { get; } = [];

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add(new RecordedNavigation(uri, options.ForceLoad));
    }
}
