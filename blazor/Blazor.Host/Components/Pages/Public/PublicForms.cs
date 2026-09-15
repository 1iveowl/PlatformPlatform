// Form models for the static server-rendered public surface.

using System.ComponentModel.DataAnnotations;

namespace Blazor.Host.Components.Pages.Public;

public sealed class EmailForm
{
    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = "";
}

public sealed class OneTimePasswordForm
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string OneTimePassword { get; set; } = "";
}
