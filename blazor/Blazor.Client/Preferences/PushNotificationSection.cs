// The state and the text of the notifications section, so the component only calls the service and renders. The section
// stays hidden until this deployment is known to offer notifications and the browser has been asked what it supports and
// what the user has allowed. One change runs at a time: while it does, the switch and the test button are disabled, and
// both are set from what the browser and the account API report afterwards, never from the click, so a refused or a lost
// change leaves the state they report.

namespace Blazor.Client.Preferences;

// What the browser says about the permission for notifications, as the three values the Notification API defines
public enum PushPermission
{
    Default,
    Granted,
    Denied
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

    // Turning notifications on needs a permission the browser has not refused; turning them off is always allowed
    public bool IsSwitchDisabled => !IsLoaded || !IsSupported || IsBusy || (IsBlocked && !IsSubscribed);

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

    public void Load(bool isSupported, PushPermission permission, bool isSubscribed)
    {
        IsSupported = isSupported;
        Permission = permission;
        IsSubscribed = isSupported && isSubscribed;
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
