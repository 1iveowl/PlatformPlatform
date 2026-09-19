namespace Account.Emails.Layout;

// The parts of the email document that are not markup a component can express: the document type an email client
// expects, and the stylesheet the layout puts in the head. Both are here rather than in the layout component so they
// can be asserted by a test and so the stylesheet's at-rules are not read as Razor expressions.
public static class EmailDocument
{
    public const string DocumentType = """<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">""";

    public const string FontStack = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    // Standard, non-nested CSS: the dark-mode and phone rules that inline styles cannot express. Clients that strip the
    // style element or ignore the media queries fall back to the inline light defaults on every element.
    //
    // The html element is coloured as well as the body because the body only fills the content height, so without it the
    // area around the message shows the client's own chrome. The dark rules target the inner cells as well as the
    // classed elements because the inline light colours sit on those cells and would otherwise win.
    public static string BuildStyleSheet(string headerBackground)
    {
        return $$"""
                 html { background-color: #f4f4f5; }
                 @media (prefers-color-scheme: dark) {
                   html { background-color: #1f1f1f !important; }
                   .email-body { background-color: #1f1f1f !important; }
                   .email-body td { background-color: #1f1f1f !important; color: #e5e5e5 !important; }
                   .email-card { background-color: #2a2a2a !important; color: #e5e5e5 !important; }
                   .email-card td { background-color: #2a2a2a !important; color: #e5e5e5 !important; }
                   .email-header { background-color: {{headerBackground}} !important; }
                   .email-header td { background-color: {{headerBackground}} !important; }
                   .email-heading { color: #fafafa !important; }
                   .email-otp-box { background-color: #171717 !important; }
                   .email-otp-box td { background-color: #171717 !important; }
                   .email-otp-text { color: #fafafa !important; }
                   .email-muted { color: #a3a3a3 !important; }
                   .email-link { color: #e5e5e5 !important; }
                   .email-button-default { background-color: #f6f6f6 !important; color: #171717 !important; }
                   .email-separator { border-top-color: #404040 !important; }
                 }
                 /* Phones: the card spans the full width flush to the top edge, so the desktop floating margin and the
                    corner radius go away; rounded corners on a full-bleed card look like a rendering glitch. */
                 @media (max-width: 600px) {
                   .email-card { margin-top: 0 !important; border-radius: 0 !important; }
                 }
                 """;
    }
}
