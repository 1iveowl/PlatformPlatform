namespace Blazor.Client.BackOffice;

// The back office's pages below the path base on the back-office host: the dashboard is the back office's home, and each
// list page keeps the React back office's path below it (accounts, users, invoices, billing-events, feature-flags), so a
// link such as accounts?driftDetected=true means the same in both editions. The platform's login and logout sit at the
// root of the back-office host, outside the path base, in front of both editions.
public static class BackOfficeUrls
{
    public const string LogoutUrl = "/.auth/logout";

    public static readonly string Home = AppUrls.ToAbsolute("back-office");

    public static string ToAbsolute(string page)
    {
        return $"{Home}/{page.TrimStart('/')}";
    }
}
