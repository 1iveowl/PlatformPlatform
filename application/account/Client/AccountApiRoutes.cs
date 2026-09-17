using System.Globalization;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.Users.Requests;
using SharedKernel.Domain;

namespace Account.Client;

// Every account API path the typed clients call, as mapped in account/Api/Endpoints. Route values and query values are
// URI-escaped here, so no caller builds an account API URL.
public static class AccountApiRoutes
{
    public const string Bootstrap = "/api/account/bootstrap";

    public const string Logout = "/api/account/authentication/logout";

    public const string SwitchTenant = "/api/account/authentication/switch-tenant";

    public const string StartEmailLogin = "/api/account/authentication/email/login/start";

    public const string StartEmailSignup = "/api/account/authentication/email/signup/start";

    public const string Users = "/api/account/users";

    public const string CurrentUser = "/api/account/users/me";

    public const string BulkDeleteUsers = "/api/account/users/bulk-delete";

    public const string InviteUser = "/api/account/users/invite";

    public const string ChangeTheme = "/api/account/users/me/change-theme";

    public const string DeletedUsers = "/api/account/users/deleted";

    public const string BulkPurgeUsers = "/api/account/users/deleted/bulk-purge";

    public const string EmptyRecycleBin = "/api/account/users/deleted/empty-recycle-bin";

    public const string Tenants = "/api/account/tenants";

    public const string CurrentTenant = "/api/account/tenants/current";

    private const string DateFormat = "yyyy-MM-dd";

    // The external login and signup starts are document navigations, not typed client calls; the provider is the name of
    // the account API's ExternalProviderType value
    public static string StartExternalLogin(string provider)
    {
        return $"/api/account/authentication/{Uri.EscapeDataString(provider)}/login/start";
    }

    public static string StartExternalSignup(string provider)
    {
        return $"/api/account/authentication/{Uri.EscapeDataString(provider)}/signup/start";
    }

    public static string CompleteEmailLogin(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/login/{Uri.EscapeDataString(emailLoginId.Value)}/complete";
    }

    public static string CompleteEmailSignup(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/signup/{Uri.EscapeDataString(emailLoginId.Value)}/complete";
    }

    public static string ResendEmailLoginCode(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/login/{Uri.EscapeDataString(emailLoginId.Value)}/resend-code";
    }

    public static string ResendEmailSignupCode(EmailLoginId emailLoginId)
    {
        return $"/api/account/authentication/email/signup/{Uri.EscapeDataString(emailLoginId.Value)}/resend-code";
    }

    public static string User(UserId userId)
    {
        return $"{Users}/{Uri.EscapeDataString(userId.Value)}";
    }

    public static string ChangeUserRole(UserId userId)
    {
        return $"{User(userId)}/change-user-role";
    }

    public static string RestoreUser(UserId userId)
    {
        return $"{User(userId)}/restore";
    }

    public static string PurgeUser(UserId userId)
    {
        return $"{User(userId)}/purge";
    }

    // Bound with [AsParameters] like the users query; the first page omits PageOffset
    public static string GetDeletedUsers(GetDeletedUsersQuery query)
    {
        var pageOffset = query.PageOffset is { } offset ? $"{nameof(GetDeletedUsersQuery.PageOffset)}={offset.ToString(CultureInfo.InvariantCulture)}&" : "";
        return $"{DeletedUsers}?{pageOffset}{nameof(GetDeletedUsersQuery.PageSize)}={query.PageSize.ToString(CultureInfo.InvariantCulture)}";
    }

    // The endpoint binds the query with [AsParameters], so the parameter names are the PascalCase property names, enums
    // are their names and dates are calendar dates. A null value is omitted.
    public static string GetUsers(GetUsersQuery query)
    {
        KeyValuePair<string, string?>[] parameters =
        [
            new(nameof(GetUsersQuery.Search), query.Search),
            new(nameof(GetUsersQuery.UserRole), query.UserRole?.ToString()),
            new(nameof(GetUsersQuery.UserStatus), query.UserStatus?.ToString()),
            new(nameof(GetUsersQuery.StartDate), query.StartDate?.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.EndDate), query.EndDate?.ToString(DateFormat, CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.OrderBy), query.OrderBy.ToString()),
            new(nameof(GetUsersQuery.SortOrder), query.SortOrder.ToString()),
            new(nameof(GetUsersQuery.PageOffset), query.PageOffset?.ToString(CultureInfo.InvariantCulture)),
            new(nameof(GetUsersQuery.PageSize), query.PageSize.ToString(CultureInfo.InvariantCulture))
        ];

        var queryString = string.Join('&', parameters.Where(parameter => parameter.Value is not null).Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}"));
        return $"{Users}?{queryString}";
    }
}
