// The account settings form's model with the account API's limit as a data annotation, so the browser rejects what
// UpdateCurrentTenantCommand's validator would; the server stays the authority and its field errors reach the same field
// through FormErrorMapper. The API stores the name as it is sent, so nothing here trims it.

using System.ComponentModel.DataAnnotations;
using Account.Features.Tenants.Queries;
using Account.Features.Tenants.Requests;

namespace Blazor.Client.Settings;

public sealed class AccountSettingsForm
{
    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TenantNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TenantNameLength))]
    public string Name { get; set; } = "";

    public static AccountSettingsForm From(TenantResponse tenant)
    {
        return new AccountSettingsForm { Name = tenant.Name };
    }

    public AccountSettingsForm Copy()
    {
        return new AccountSettingsForm { Name = Name };
    }

    public UpdateCurrentTenantCommand ToCommand()
    {
        return new UpdateCurrentTenantCommand { Name = Name };
    }

    public bool HasChangesFrom(AccountSettingsForm saved)
    {
        return Name != saved.Name;
    }
}
