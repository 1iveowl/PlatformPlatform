// The WebAssembly client uses the culture the host rendered the page in. The host picks it per request (the locale claim,
// else Accept-Language, else en-US) and writes it to <html lang>; the client reads that attribute once, before the first
// render, so the interactive render shows the same language and formats as the prerendered markup. No request is made and
// no user data is read; an attribute the platform does not support falls back to en-US.

using System.Globalization;
using Microsoft.JSInterop;

namespace Blazor.Client.Localization;

public static class ClientCulture
{
    public const string DocumentLanguageIdentifier = "document.documentElement.lang";

    public static CultureInfo Apply(IJSInProcessRuntime jsRuntime)
    {
        var culture = SupportedCultures.GetCulture(ReadDocumentLanguage(jsRuntime));
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    // A document whose language cannot be read is treated like one with an unsupported language: the default culture
    private static string? ReadDocumentLanguage(IJSInProcessRuntime jsRuntime)
    {
        try
        {
            return jsRuntime.GetValue<string?>(DocumentLanguageIdentifier);
        }
        catch (JSException)
        {
            return null;
        }
    }
}
