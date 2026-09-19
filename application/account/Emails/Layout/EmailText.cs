using System.Globalization;

namespace Account.Emails.Layout;

// Fills the placeholders of a localized email string in the culture the renderer set for this email.
public static class EmailText
{
    public static string Format(string format, string argument)
    {
        return string.Format(CultureInfo.CurrentCulture, format, argument);
    }

    public static string Format(string format, string firstArgument, string secondArgument)
    {
        return string.Format(CultureInfo.CurrentCulture, format, firstArgument, secondArgument);
    }
}
