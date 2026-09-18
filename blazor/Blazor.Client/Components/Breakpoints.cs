using System.Globalization;

namespace Blazor.Client.Components;

// The viewport widths the edition responds to. They mirror the React edition's application/shared-webapp/ui/utils/responsive.ts,
// whose sm, md, lg, xl and 2xl are 640, 768, 1024, 1280 and 1536 pixels, written here in rem at the 16 pixel root font size
// the host stylesheet assumes.
public enum Breakpoint
{
    Small,
    Medium,
    Large,
    ExtraLarge,
    ExtraExtraLarge
}

// The one place the five widths are named. A media query condition cannot read a CSS custom property, so each width is also
// a literal in the media queries of blazor/Blazor.Host/wwwroot/app.css and in the matchMedia queries of wwwroot/js/shell.js,
// viewport.js and data-list.js; BreakpointsTests reads those files and fails when a value drifts from this class. The
// stylesheet declares the same widths as --breakpoint-* custom properties, which are the declared reference for anyone
// reading it, never the source of a media query condition.
public static class Breakpoints
{
    // Below this the sidebar is replaced by the floating button and the mobile navigation dialog, a dialog fills the screen,
    // a column with a hide-below class is dropped and tap targets grow
    public const string Small = "40rem";

    // Below this a side pane has no room beside the content and becomes a full-screen modal dialog (two pane widths)
    public const string Medium = "48rem";

    public const string Large = "64rem";

    // Below this the sidebar starts collapsed and overlays the content when expanded
    public const string ExtraLarge = "80rem";

    public const string ExtraExtraLarge = "96rem";

    public static IReadOnlyList<Breakpoint> All { get; } = Enum.GetValues<Breakpoint>();

    public static string Width(Breakpoint breakpoint)
    {
        return breakpoint switch
        {
            Breakpoint.Small => Small,
            Breakpoint.Medium => Medium,
            Breakpoint.Large => Large,
            Breakpoint.ExtraLarge => ExtraLarge,
            Breakpoint.ExtraExtraLarge => ExtraExtraLarge,
            _ => throw new ArgumentOutOfRangeException(nameof(breakpoint), breakpoint, null)
        };
    }

    // The suffix the stylesheet and the class names use, the same names the React edition's breakpoints carry
    public static string Suffix(Breakpoint breakpoint)
    {
        return breakpoint switch
        {
            Breakpoint.Small => "sm",
            Breakpoint.Medium => "md",
            Breakpoint.Large => "lg",
            Breakpoint.ExtraLarge => "xl",
            Breakpoint.ExtraExtraLarge => "2xl",
            _ => throw new ArgumentOutOfRangeException(nameof(breakpoint), breakpoint, null)
        };
    }

    public static string MinWidthQuery(Breakpoint breakpoint)
    {
        return $"(min-width: {Width(breakpoint)})";
    }

    // The complement of the minimum-width query, one hundredth of a rem below it, for a rule that applies only under the
    // breakpoint and has no mobile-first form
    public static string MaxWidthQuery(Breakpoint breakpoint)
    {
        var width = decimal.Parse(Width(breakpoint).Replace("rem", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        return $"(max-width: {(width - 0.01m).ToString(CultureInfo.InvariantCulture)}rem)";
    }

    public static string CustomProperty(Breakpoint breakpoint)
    {
        return $"--breakpoint-{Suffix(breakpoint)}";
    }

    // The class a DataListColumn.HideBelow renders; the stylesheet hides the header and the cells carrying it below the width
    public static string HideBelowClass(Breakpoint breakpoint)
    {
        return $"data-list-hide-below-{Suffix(breakpoint)}";
    }
}
