// FluentUI's built-in texts (required markers, empty and loading content, message box buttons, error boundary) read from
// the shared resources in the current UI culture. A key the shared resources do not carry falls back to FluentUI's own
// English text, so a component added later still renders; FluentResourceLocalizerTests lists the keys that are covered.

using System.Globalization;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Blazor.Client.Localization;

public sealed class FluentResourceLocalizer : IFluentLocalizer
{
    public string this[string key, params object[] arguments]
    {
        get
        {
            var text = FluentComponentStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
            return text is null ? this.GetDefault(key, arguments) : string.Format(CultureInfo.CurrentCulture, text, arguments);
        }
    }
}
