namespace Blazor.Client.Components;

public enum SidePaneMode
{
    // Beside the content, a labelled region sharing focus and scrolling with the page around it
    Docked,

    // Over the content, a labelled modal dialog: the background is inert, focus is trapped and the page behind does not scroll
    FullScreen
}

// The decisions a SidePane makes, kept out of the component so they can be tested without a renderer.
public static class SidePaneRules
{
    // A docked pane and the content beside it need two pane widths; below that the pane takes the screen. The React edition
    // derives the same threshold from twice its 24 rem pane width in SidePane.tsx's useNeedsFullscreen.
    public const Breakpoint DockedFrom = Breakpoint.Medium;

    public static SidePaneMode ModeFor(ViewportMatches matches)
    {
        return matches.Reaches(DockedFrom) ? SidePaneMode.Docked : SidePaneMode.FullScreen;
    }

    // What an open pane belongs to: the path and the fragment, never the query. The list writes the activated row's key into
    // the query, so opening and closing the pane is not itself a navigation away from it.
    public static string LocationKey(string uri)
    {
        var parsed = new Uri(uri, UriKind.Absolute);
        return $"{parsed.AbsolutePath}{parsed.Fragment}";
    }

    public static bool ClosesOnNavigation(string openedAtLocationKey, string currentUri)
    {
        return !string.Equals(openedAtLocationKey, LocationKey(currentUri), StringComparison.Ordinal);
    }
}
