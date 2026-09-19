using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Emails;

// Renders a transactional email by rendering its Razor component with the .NET HtmlRenderer, taking the subject from
// the rendered <title> so an email defines it once, next to the markup it belongs to.
//
// The components resolve their services from a service provider of this renderer's own rather than the application's,
// for one reason: the HtmlEncoder. The framework default escapes every character outside basic Latin, which would turn
// the Danish emails into entity references that read correctly in a browser but not in a plain string comparison, and
// an email body is compared as a string by the specifications and by anyone reading it in a mail client's source view.
// The encoder below leaves every printable character alone and still escapes the characters that carry meaning in HTML.
public sealed partial class RazorEmailRenderer(EmailBrand brand, ILoggerFactory loggerFactory) : IEmailRenderer
{
    private readonly ServiceProvider _componentServices = BuildComponentServices(brand);

    public async Task<EmailRenderResult> RenderEmailAsync(EmailTemplateBase template)
    {
        var culture = CultureInfo.GetCultureInfo(template.Locale);
        await using var htmlRenderer = new HtmlRenderer(_componentServices, loggerFactory);

        var rendered = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                var previousUiCulture = CultureInfo.CurrentUICulture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                try
                {
                    var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Model"] = template.Model });
                    var output = await htmlRenderer.RenderComponentAsync(template.ComponentType, parameters);
                    return (HtmlBody: output.ToHtmlString(), PlainTextBody: template.RenderPlainText(brand));
                }
                finally
                {
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
            }
        );

        var subject = ExtractSubject(rendered.HtmlBody, template.ComponentType.Name);
        return new EmailRenderResult(subject, rendered.HtmlBody, rendered.PlainTextBody);
    }

    private static ServiceProvider BuildComponentServices(EmailBrand brand)
    {
        var services = new ServiceCollection();
        services.AddSingleton(brand);
        services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));
        return services.BuildServiceProvider();
    }

    private static string ExtractSubject(string htmlBody, string componentName)
    {
        var match = TitleRegex().Match(htmlBody);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Email component '{componentName}' is missing a <title> element required for the subject line.");
        }

        // The title is HTML; a subject header is plain text, so the entities the renderer wrote are resolved back here
        return WebUtility.HtmlDecode(WhitespaceRegex().Replace(match.Groups[1].Value, " ").Trim());
    }

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
