// Form models for the static server-rendered public surface. The validation messages come from the shared resources in
// the request culture, so no framework default English message reaches the page.

using System.ComponentModel.DataAnnotations;
using SharedKernel.Localization;

namespace Blazor.Host.Components.Pages.Public;

public sealed class EmailForm
{
    [Required(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressInvalid))]
    [StringLength(100, ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressTooLong))]
    public string Email { get; set; } = "";
}

public sealed class OneTimePasswordForm
{
    [Required(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EnterYourVerificationCode))]
    [StringLength(6, MinimumLength = 6, ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.VerificationCodeLength))]
    public string OneTimePassword { get; set; } = "";
}
