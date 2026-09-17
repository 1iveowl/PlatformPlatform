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
    // Six letters A to Z in any case; the page sends the code upper case
    [RegularExpression("^[A-Za-z]{6}$", ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.VerificationCodeFormat))]
    public string OneTimePassword { get; set; } = "";

    // The code as the account API expects it: the generator issues upper case letters
    public string GetNormalizedOneTimePassword()
    {
        return OneTimePassword.ToUpperInvariant();
    }
}
