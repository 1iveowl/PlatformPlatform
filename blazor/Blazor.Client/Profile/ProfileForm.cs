// The profile form's model with the account API's limits as data annotations, so the browser rejects what
// UpdateCurrentUserCommand's validator would; the server stays the authority and its field errors reach the same fields
// through FormErrorMapper.

using System.ComponentModel.DataAnnotations;
using Account.Features.Users.Requests;

namespace Blazor.Client.Profile;

public sealed class ProfileForm
{
    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.FirstNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.FirstNameLength))]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.LastNameLength))]
    [StringLength(30, MinimumLength = 1, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.LastNameLength))]
    public string LastName { get; set; } = "";

    [StringLength(50, ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.TitleTooLong))]
    public string? Title { get; set; } = "";

    public static ProfileForm From(CurrentUserResponse user)
    {
        return new ProfileForm { FirstName = user.FirstName ?? "", LastName = user.LastName ?? "", Title = user.Title };
    }

    public UpdateCurrentUserCommand ToCommand()
    {
        return new UpdateCurrentUserCommand(FirstName, LastName, Title ?? "");
    }

    // The values as the account API stores them, which trims each field before validating it
    public ProfileForm Trimmed()
    {
        return new ProfileForm { FirstName = FirstName.Trim(), LastName = LastName.Trim(), Title = Title?.Trim() };
    }

    public bool HasChangesFrom(ProfileForm saved)
    {
        return FirstName != saved.FirstName || LastName != saved.LastName || (Title ?? "") != (saved.Title ?? "");
    }
}
