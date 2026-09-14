namespace Account.Client;

// The header names the account API and the gateway use. The values must stay identical to AuthenticationTokenHttpKeys and
// the X-Locale culture provider in SharedKernel, which this portable assembly cannot reference.
public static class AccountApiHeaders
{
    public const string Locale = "X-Locale";

    public const string AntiforgeryToken = "x-xsrf-token";

    public const string UserFeatureFlags = "x-user-feature-flags";

    public const string UnauthorizedReason = "x-unauthorized-reason";
}
