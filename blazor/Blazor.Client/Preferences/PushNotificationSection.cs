// The state and the text of the notifications section, so the component only calls the service and renders. The section
// stays hidden until this deployment is known to offer notifications and the browser has been asked what it supports and
// what the user has allowed. One change runs at a time: while it does, the switch and the test button are disabled, and
// both are set from what the browser and the account API report afterwards, never from the click, so a refused or a lost
// change leaves the state they report. A denied permission ends this device's subscription: the switch reads off, and
// PushSubscriptionRevocation removes what the browser and the account still hold for it.

using Account.Client;
using Account.Features.PushNotifications.Domain;

namespace Blazor.Client.Preferences;

// What the browser says about the permission for notifications, as the three values the Notification API defines
public enum PushPermission
{
    Default,
    Granted,
    Denied
}

// Whose subscription the one this browser holds is. The browser's subscription and the identifier this device stored both
// outlive a logout, so a switch read from the browser alone shows the account that left this device as the one signed in
// now; only the account's own rows say which it is.
public enum PushDeviceOwner
{
    // The browser holds no subscription
    None,

    // The browser holds one and the account has the row this device stored for it
    ThisAccount,

    // The browser holds one the account does not have, which is what an account that left this device leaves behind
    AnotherAccount,

    // The browser holds one this device cannot attribute: it stored no identifier for it, or the account's rows could not
    // be read. Nothing is destroyed for one, and the switch reads off until the account says it is its own.
    Unattributed
}

// What a subscribe attempt ended in, as the module reports it
public enum PushSubscribeOutcome
{
    Subscribed,
    Denied,
    Dismissed,
    Unsupported,
    Failed
}

public sealed class PushNotificationSection
{
    public const string SectionTestId = "preferences-notifications";

    public const string SwitchTestId = "notifications-switch";

    public const string TestButtonTestId = "notifications-send-test";

    public const string TurnedOnToastTestId = "notifications-turned-on-toast";

    public const string TurnedOffToastTestId = "notifications-turned-off-toast";

    public const string TestSentToastTestId = "test-notification-sent-toast";

    public const string NoticeTestId = "notifications-notice";

    public bool IsLoaded { get; private set; }

    public bool IsSupported { get; private set; }

    public PushPermission Permission { get; private set; }

    public bool IsSubscribed { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsVisible => IsLoaded;

    public bool IsBlocked => Permission == PushPermission.Denied;

    // Turning notifications on needs a permission the browser has not refused, and a refused permission has already ended
    // this device's subscription, so there is nothing to turn off either
    public bool IsSwitchDisabled => !IsLoaded || !IsSupported || IsBusy || IsBlocked;

    public bool IsTestDisabled => !IsLoaded || !IsSubscribed || IsBusy;

    public string HeadingId => $"{SectionTestId}-heading";

    public string DescriptionId => $"{SectionTestId}-description";

    // The one sentence that explains why the switch cannot be used, or none when it can
    public string? Notice
    {
        get
        {
            if (!IsLoaded) return null;
            if (!IsSupported) return AccountStrings.NotificationsNotSupported;
            return IsBlocked ? AccountStrings.NotificationsBlocked : null;
        }
    }

    public static PushPermission ParsePermission(string? permission)
    {
        return permission switch
        {
            "granted" => PushPermission.Granted,
            "denied" => PushPermission.Denied,
            _ => PushPermission.Default
        };
    }

    // The switch is on for this account's own subscription and for no other: an identifier this device stored for a row the
    // account does not have belonged to the account that left the device.
    public static PushDeviceOwner ReadDeviceOwner(bool hasBrowserSubscription, string? storedSubscriptionId, string[] accountSubscriptionIds)
    {
        if (!hasBrowserSubscription) return PushDeviceOwner.None;
        if (string.IsNullOrEmpty(storedSubscriptionId)) return PushDeviceOwner.Unattributed;

        return accountSubscriptionIds.Contains(storedSubscriptionId, StringComparer.Ordinal) ? PushDeviceOwner.ThisAccount : PushDeviceOwner.AnotherAccount;
    }

    public static PushSubscribeOutcome ParseOutcome(string? outcome)
    {
        return outcome switch
        {
            "subscribed" => PushSubscribeOutcome.Subscribed,
            "denied" => PushSubscribeOutcome.Denied,
            "dismissed" => PushSubscribeOutcome.Dismissed,
            "unsupported" => PushSubscribeOutcome.Unsupported,
            _ => PushSubscribeOutcome.Failed
        };
    }

    // A subscription the browser still holds while the permission is denied is one nothing sent to it is shown for, so the
    // switch reads off and agrees with the notice that says notifications are blocked
    public void Load(bool isSupported, PushPermission permission, bool isSubscribed)
    {
        IsSupported = isSupported;
        Permission = permission;
        IsSubscribed = isSupported && isSubscribed && permission != PushPermission.Denied;
        IsLoaded = true;
    }

    // False while another change runs, so a second click starts nothing
    public bool TryBeginChange()
    {
        if (IsBusy || !IsLoaded) return false;

        IsBusy = true;
        return true;
    }

    public void EndChange()
    {
        IsBusy = false;
    }

    // The account this section belongs to is being left, so it shows nothing that could reach the next one
    public void Reset()
    {
        IsLoaded = false;
        IsSupported = false;
        Permission = PushPermission.Default;
        IsSubscribed = false;
        IsBusy = false;
    }
}

// A subscription the user revoked in the browser settings is removed from the account on the next visit, in either form a
// browser leaves it in: the browser no longer holds the subscription, or it still holds one while the permission is denied,
// which Safari does, so nothing sent to it is ever shown. The browser lets go of the one it holds, and the identifier this
// device stored names the row the account deletes. A granted or unanswered permission with a subscription is left alone.
public static class PushSubscriptionRevocation
{
    // deleteSubscription returns null when the authenticated surface is being left (SessionState.UnlessLeavingAsync); the
    // stored identifier is then kept, so the next visit, finding no subscription in the browser, deletes the row
    public static async Task ReconcileAsync(PushNotificationBrowser browser, Func<PushSubscriptionId, Task<ApiCallResult?>> deleteSubscription)
    {
        if (await browser.ReadSubscriptionAsync() is not null)
        {
            if (await browser.ReadPermissionAsync() != PushPermission.Denied) return;

            await browser.UnsubscribeAsync();
        }

        if (!PushSubscriptionId.TryParse(await browser.ReadSavedSubscriptionIdAsync(), out var savedSubscriptionId)) return;

        var result = await deleteSubscription(savedSubscriptionId);
        if (result is null) return;

        await browser.SaveSubscriptionIdAsync(null);
    }
}
