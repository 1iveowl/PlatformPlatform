// Form models for the welcome setup. The limits mirror the account API's validators for UpdateCurrentTenant and
// UpdateCurrentUser; the API stays authoritative and its field errors are mapped onto the same properties.

using System.ComponentModel.DataAnnotations;
using SharedKernel.Localization;

namespace Blazor.Host.Components.Pages.App;

public sealed class AccountSetupForm
{
    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TenantNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TenantNameLength))]
    public string Name { get; set; } = "";
}

public sealed class ProfileSetupForm
{
    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.FirstNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.FirstNameLength))]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.LastNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.LastNameLength))]
    public string LastName { get; set; } = "";

    [StringLength(50, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TitleTooLong))]
    public string? Title { get; set; }
}
