namespace Blazor.Host.Shell;

// The back office's subscription setting, BACK_OFFICE_SUBSCRIPTION_ENABLED: the value the account API's back-office
// listener gets as PUBLIC_SUBSCRIPTION_ENABLED, so the Blazor back office shows the billing parts (the Billing menu group,
// the revenue tiles and the payment and billing event cards) exactly when the React back office does. Only "true" turns
// it on, as in the React back office. It is separate from the app edition's PUBLIC_SUBSCRIPTION_ENABLED on this host, which
// follows the Stripe configuration. Read from configuration, so a test host can set it on the command line.
public sealed class BackOfficeSettings(bool isSubscriptionEnabled)
{
    public const string SubscriptionEnabledKey = "BACK_OFFICE_SUBSCRIPTION_ENABLED";

    public bool IsSubscriptionEnabled { get; } = isSubscriptionEnabled;

    public static BackOfficeSettings From(IConfiguration configuration)
    {
        return new BackOfficeSettings(string.Equals(configuration[SubscriptionEnabledKey], "true", StringComparison.Ordinal));
    }
}
