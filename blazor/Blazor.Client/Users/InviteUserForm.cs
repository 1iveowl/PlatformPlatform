using System.ComponentModel.DataAnnotations;

namespace Blazor.Client.Users;

// The invite dialog's email field, validated as the InviteUser command's email rule is: required, a valid format and at
// most 100 characters. The server stays the authority and its messages are shown as returned.
public sealed class InviteUserForm
{
    public const int EmailMaxLength = 100;

    [Required(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressInvalid))]
    [StringLength(EmailMaxLength, ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressTooLong))]
    public string Email { get; set; } = "";
}
