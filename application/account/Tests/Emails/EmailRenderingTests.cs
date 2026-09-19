using Account.Emails.Templates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Emails;
using Xunit;

namespace Account.Tests.Emails;

// The five transactional emails rendered by the Razor components, in both cultures, asserted on the three parts a
// recipient sees: the subject, the HTML body and the plain text twin.
public sealed class EmailRenderingTests
{
    private const string PublicUrl = "https://app.example.com";
    private const string OneTimePassword = "ABC123";
    private const string Domain = "app.example.com";

    private static readonly EmailBrand Brand = new(PublicUrl);

    private static readonly IEmailRenderer Renderer = new RazorEmailRenderer(Brand, NullLoggerFactory.Instance);

    public static TheoryData<string, string, string[]> EnglishExpectations => new()
    {
        { "StartLogin", "PlatformPlatform login verification code", ["Your confirmation code is below", "Enter it in your open browser window. It is only valid for a few minutes."] },
        { "StartSignup", "Confirm your email address", ["Your confirmation code is below", "Enter it in your open browser window. It is only valid for a few minutes."] },
        { "ResendEmailLogin", "Your verification code (resend)", ["Here&#x27;s your new verification code", "This code will expire in a few minutes."] },
        { "UnknownUser", "No account found", ["Is this the right email address?", "You can try again with a different email, or sign up for a new account."] },
        { "InviteUser", "You have been invited to join Contoso on PlatformPlatform", ["invited you to join PlatformPlatform.", "go to this page in your open browser"] }
    };

    public static TheoryData<string, string, string[]> DanishExpectations => new()
    {
        { "StartLogin", "PlatformPlatform-bekræftelseskode til login", ["Din bekræftelseskode står herunder", "Indtast den i dit åbne browservindue. Den er kun gyldig i få minutter."] },
        { "StartSignup", "Bekræft din e-mailadresse", ["Din bekræftelseskode står herunder", "Indtast den i dit åbne browservindue. Den er kun gyldig i få minutter."] },
        { "ResendEmailLogin", "Din bekræftelseskode (gensendt)", ["Her er din nye bekræftelseskode", "Denne kode udløber om få minutter."] },
        { "UnknownUser", "Ingen konto fundet", ["Er det den rigtige e-mailadresse?", "Du kan prøve igen med en anden e-mailadresse eller oprette en ny konto."] },
        { "InviteUser", "Du er inviteret til at deltage i Contoso på PlatformPlatform", ["har inviteret dig til at deltage i PlatformPlatform.", "gå til denne side i din åbne browser"] }
    };

    [Theory]
    [MemberData(nameof(EnglishExpectations))]
    public async Task RenderEmail_WhenCultureIsEnglish_ShouldRenderSubjectAndBody(string templateName, string expectedSubject, string[] expectedHtmlFragments)
    {
        await RenderAndAssert(templateName, "en-US", expectedSubject, expectedHtmlFragments);
    }

    [Theory]
    [MemberData(nameof(DanishExpectations))]
    public async Task RenderEmail_WhenCultureIsDanish_ShouldRenderSubjectAndBody(string templateName, string expectedSubject, string[] expectedHtmlFragments)
    {
        await RenderAndAssert(templateName, "da-DK", expectedSubject, expectedHtmlFragments);
    }

    [Theory]
    [InlineData("StartLogin")]
    [InlineData("StartSignup")]
    [InlineData("ResendEmailLogin")]
    public async Task RenderEmail_WhenTemplateCarriesOneTimePassword_ShouldEndPlainTextWithAutofillLine(string templateName)
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate(templateName, "en-US"));

        // Assert
        rendered.HtmlBody.Should().Contain(OneTimePassword);
        rendered.PlainTextBody.Should().Contain(OneTimePassword);
        rendered.PlainTextBody.Split('\n').Last().Should().Be($"@{Domain} #{OneTimePassword}");
    }

    [Fact]
    public async Task RenderEmail_WhenUnknownUser_ShouldLinkToSignup()
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate("UnknownUser", "en-US"));

        // Assert
        rendered.HtmlBody.Should().Contain($"href=\"{PublicUrl}/signup\"");
        rendered.PlainTextBody.Should().Contain($"Sign up for an account {PublicUrl}/signup");
    }

    [Fact]
    public async Task RenderEmail_WhenInviteUser_ShouldLinkToLogin()
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate("InviteUser", "en-US"));

        // Assert
        rendered.HtmlBody.Should().Contain($"href=\"{PublicUrl}/login\"");
        rendered.PlainTextBody.Should().Contain($"go to this page in your open browser {PublicUrl}/login and login using invitee@example.com.");
    }

    [Fact]
    public async Task RenderEmail_WhenAUserSuppliedValueContainsMarkup_ShouldEscapeItInHtmlAndLeaveThePlainTextAlone()
    {
        // Arrange
        const string markupName = "<img src=x onerror=alert(1)>Mallory";
        var template = new InviteUserEmailTemplate(
            "en-US",
            new InviteUserEmailModel(markupName, "Contoso", "invitee@example.com", $"{PublicUrl}/login")
        );

        // Act
        var rendered = await Renderer.RenderEmailAsync(template);

        // Assert
        rendered.HtmlBody.Should().NotContain("<img src=x onerror=alert(1)>");
        rendered.HtmlBody.Should().Contain("&lt;img src=x onerror=alert(1)&gt;Mallory");
        rendered.PlainTextBody.Should().Contain(markupName);
    }

    [Fact]
    public async Task RenderEmail_WhenCultureIsDanish_ShouldWriteDanishLettersAsThemselves()
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate("StartSignup", "da-DK"));

        // Assert
        rendered.HtmlBody.Should().Contain("Din bekræftelseskode står herunder");
        rendered.HtmlBody.Should().NotContain("&#xE6;").And.NotContain("&#xF8;").And.NotContain("&#xE5;");
        rendered.HtmlBody.Should().Contain("lang=\"da-DK\"");
    }

    [Fact]
    public async Task RenderEmail_WhenRendered_ShouldReferenceTheLogoAndLegalPagesOnThePublicHost()
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate("StartLogin", "en-US"));

        // Assert
        rendered.HtmlBody.Should().Contain($"src=\"{PublicUrl}/email/logo-1200x184.png\"");
        rendered.HtmlBody.Should().NotContain("data:image");
        rendered.HtmlBody.Should().Contain($"href=\"{PublicUrl}/legal/privacy\"");
        rendered.PlainTextBody.Should().Contain($"Privacy {PublicUrl}/legal/privacy");
    }

    private static async Task RenderAndAssert(string templateName, string locale, string expectedSubject, string[] expectedHtmlFragments)
    {
        // Act
        var rendered = await Renderer.RenderEmailAsync(CreateTemplate(templateName, locale));

        // Assert
        rendered.Subject.Should().Be(expectedSubject);
        foreach (var fragment in expectedHtmlFragments)
        {
            rendered.HtmlBody.Should().Contain(fragment);
        }
    }

    private static EmailTemplateBase CreateTemplate(string templateName, string locale)
    {
        return templateName switch
        {
            "StartLogin" => new StartLoginEmailTemplate(locale, new StartLoginEmailModel(OneTimePassword, Domain, 5)),
            "StartSignup" => new StartSignupEmailTemplate(locale, new StartSignupEmailModel(OneTimePassword, Domain, 5)),
            "ResendEmailLogin" => new ResendEmailLoginEmailTemplate(locale, new ResendEmailLoginEmailModel(OneTimePassword, Domain, 5)),
            "UnknownUser" => new UnknownUserEmailTemplate(locale, new UnknownUserEmailModel("alex@example.com", $"{PublicUrl}/signup")),
            "InviteUser" => new InviteUserEmailTemplate(locale, new InviteUserEmailModel("Alex Taylor", "Contoso", "invitee@example.com", $"{PublicUrl}/login")),
            _ => throw new ArgumentOutOfRangeException(nameof(templateName), templateName, "Unknown email template.")
        };
    }
}
