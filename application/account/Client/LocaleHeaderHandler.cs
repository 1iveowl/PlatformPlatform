using System.Globalization;

namespace Account.Client;

// The account API localizes its messages from X-Locale, because a browser does not let a page set Accept-Language
public sealed class LocaleHeaderHandler(Func<string?> getLocale) : DelegatingHandler
{
    public static LocaleHeaderHandler FromCurrentUiCulture()
    {
        return new LocaleHeaderHandler(() => CultureInfo.CurrentUICulture.Name);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var locale = getLocale();
        if (!string.IsNullOrEmpty(locale) && !request.Headers.Contains(AccountApiHeaders.Locale))
        {
            request.Headers.Add(AccountApiHeaders.Locale, locale);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
