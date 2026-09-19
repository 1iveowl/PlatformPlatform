using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Blazor.Client.Shell;

// The shell marks the current navigation item from the document location, and enhanced navigation changes the location
// without re-rendering a component that stays alive across it, so the sidebar and the mobile menu would keep marking the
// page the user came from until an unrelated event happened to re-render the shell. This watches the location and calls
// back whenever the path that decides the current item changes, so the shell re-renders on the navigation itself. A change
// that only alters the query or the fragment leaves the current item where it is and calls back nothing, which keeps a
// list's filtering and paging out of the shell's render path.
public sealed class ShellLocationWatcher : IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly Action _onCurrentItemChanged;
    private string _currentPath;

    public ShellLocationWatcher(NavigationManager navigation, Action onCurrentItemChanged)
    {
        _navigation = navigation;
        _onCurrentItemChanged = onCurrentItemChanged;
        _currentPath = ShellNavigation.GetCurrentPath(navigation.Uri);
        _navigation.LocationChanged += OnLocationChanged;
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs eventArgs)
    {
        var path = ShellNavigation.GetCurrentPath(eventArgs.Location);
        if (string.Equals(path, _currentPath, StringComparison.Ordinal)) return;

        _currentPath = path;
        _onCurrentItemChanged();
    }
}
