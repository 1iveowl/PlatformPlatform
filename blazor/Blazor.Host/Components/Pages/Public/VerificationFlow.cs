using System.Globalization;
using Account.Features.EmailAuthentication.Domain;
using Blazor.Client;

namespace Blazor.Host.Components.Pages.Public;

// The state a one-time password verification carries across the static form round trips and reloads: when the code was
// sent (server time, Unix seconds) and how long the API said it is valid. It lives in the verification page's query
// string, so it only drives what the page shows; the account API stays authoritative for expiry, attempts and resend
// limits. Values are clamped so an edited query string can never show more validity than the API grants for one code.
public sealed record VerificationFlow(long SentAtUnixSeconds, int ValidForSeconds)
{
    public const int MaximumValidForSeconds = 300;

    public const int ResendDelaySeconds = 30;

    public static VerificationFlow Start(DateTimeOffset now, int validForSeconds)
    {
        return new VerificationFlow(now.ToUnixTimeSeconds(), Math.Clamp(validForSeconds, 0, MaximumValidForSeconds));
    }

    // A missing or unreadable state is shown as expired with the resend action available, never as a fresh code
    public static VerificationFlow FromQuery(string? sent, string? validFor, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        if (!long.TryParse(sent, NumberStyles.None, CultureInfo.InvariantCulture, out var sentAt) ||
            !int.TryParse(validFor, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return new VerificationFlow(nowSeconds - MaximumValidForSeconds, 0);
        }

        return new VerificationFlow(Math.Min(sentAt, nowSeconds), Math.Clamp(seconds, 0, MaximumValidForSeconds));
    }

    public int GetRemainingSeconds(DateTimeOffset now)
    {
        return (int)Math.Clamp(SentAtUnixSeconds + ValidForSeconds - now.ToUnixTimeSeconds(), 0, ValidForSeconds);
    }

    public int GetResendInSeconds(DateTimeOffset now)
    {
        return (int)Math.Clamp(SentAtUnixSeconds + ResendDelaySeconds - now.ToUnixTimeSeconds(), 0, ResendDelaySeconds);
    }

    public static string FormatDuration(int totalSeconds)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60}:{totalSeconds % 60:00}");
    }

    // The verification page URL with the flow state; returnPath is sanitized here, so it is never carried unchecked
    public string GetVerifyUrl(string route, EmailLoginId emailLoginId, string? email, string? returnPath, bool resent)
    {
        var parameters = new List<string>
        {
            $"id={Uri.EscapeDataString(emailLoginId.Value)}",
            $"email={Uri.EscapeDataString(email ?? "")}",
            string.Create(CultureInfo.InvariantCulture, $"sent={SentAtUnixSeconds}"),
            string.Create(CultureInfo.InvariantCulture, $"validFor={ValidForSeconds}")
        };
        if (returnPath is not null) parameters.Add($"returnPath={Uri.EscapeDataString(AppUrls.SanitizeReturnPath(returnPath))}");
        if (resent) parameters.Add("resent=true");

        return $"{AppUrls.ToAbsolute(route)}?{string.Join('&', parameters)}";
    }
}
