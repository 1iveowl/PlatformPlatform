namespace Blazor.Client.Shell;

// The shell's breakpoints, the same literals as the media queries in blazor/Blazor.Host/wwwroot/app.css and the matchMedia
// queries in wwwroot/js/shell.js; a media query condition cannot read a CSS custom property, so ShellBreakpointsTests reads
// both files and fails when a value drifts.
public static class ShellBreakpoints
{
    // Below this the sidebar is replaced by the floating button and the mobile navigation dialog
    public const string Small = "40rem";

    // Below this the sidebar starts collapsed and overlays the content when expanded
    public const string ExtraLarge = "80rem";
}
