using Account.Features.Authentication.Domain;
using Account.Features.Authentication.Queries;

namespace Blazor.Client.Sessions;

// One session card as the sessions page renders it: the parsed browser and operating system, the device type and login
// method labels, and whether the account name is shown, which it is only for a session in another tenant than the current
// session's.
public sealed record SessionCardModel(
    UserSessionInfo Session,
    string Browser,
    string OperatingSystem,
    string DeviceTypeLabel,
    string LoginMethodLabel,
    bool ShowAccountName
);

// The sessions page's list, the React edition's order: the current session first, then the others in the order the account
// API returned them.
public sealed record SessionsModel(SessionCardModel? Current, IReadOnlyList<SessionCardModel> Others)
{
    public bool IsEmpty => Current is null && Others.Count == 0;

    public static SessionsModel Create(IReadOnlyList<UserSessionInfo> sessions)
    {
        var current = sessions.FirstOrDefault(session => session.IsCurrent);
        var currentTenantName = current?.TenantName;
        var others = sessions.Where(session => !session.IsCurrent).Select(session => CreateCard(session, session.TenantName != currentTenantName)).ToArray();
        return new SessionsModel(current is null ? null : CreateCard(current, false), others);
    }

    public static string GetDeviceTypeLabel(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Desktop => AccountStrings.DeviceTypeDesktop,
            DeviceType.Mobile => AccountStrings.DeviceTypeMobile,
            DeviceType.Tablet => AccountStrings.DeviceTypeTablet,
            _ => AccountStrings.Unknown
        };
    }

    public static string GetLoginMethodLabel(LoginMethod loginMethod)
    {
        return loginMethod switch
        {
            LoginMethod.OneTimePassword => AccountStrings.LoginMethodOneTimePassword,
            LoginMethod.Google => AccountStrings.LoginMethodGoogle,
            LoginMethod.Entra => AccountStrings.LoginMethodMicrosoft,
            LoginMethod.MitId => AccountStrings.LoginMethodMitId,
            _ => AccountStrings.Unknown
        };
    }

    private static SessionCardModel CreateCard(UserSessionInfo session, bool showAccountName)
    {
        var userAgent = UserAgentParser.Parse(session.UserAgent);
        return new SessionCardModel(
            session,
            userAgent.Browser ?? AccountStrings.Unknown,
            userAgent.OperatingSystem ?? AccountStrings.Unknown,
            GetDeviceTypeLabel(session.DeviceType),
            GetLoginMethodLabel(session.LoginMethod),
            showAccountName
        );
    }
}
