// The rollout percentage an administrator enters on the flag detail: a whole number from 0 to 100, checked before the call so
// a value the account API would refuse is never sent. The field is text bound on each input event so the unsaved-changes guard
// sees an edit before the field loses focus, which is why the value is a string here and becomes a number only once it passed
// these checks. The property keeps the server's field name, so a refusal the account API still sends lands on this field.

using System.ComponentModel.DataAnnotations;

namespace Blazor.Client.BackOffice.FeatureFlags;

public sealed class FeatureFlagRolloutForm
{
    [Required(ErrorMessageResourceType = typeof(BackOfficeStrings), ErrorMessageResourceName = nameof(BackOfficeStrings.RolloutPercentageInvalid))]
    [RegularExpression("^[0-9]{1,3}$", ErrorMessageResourceType = typeof(BackOfficeStrings), ErrorMessageResourceName = nameof(BackOfficeStrings.RolloutPercentageInvalid))]
    [Range(FeatureFlagDetail.MinimumRolloutPercentage, FeatureFlagDetail.MaximumRolloutPercentage, ErrorMessageResourceType = typeof(BackOfficeStrings),
        ErrorMessageResourceName = nameof(BackOfficeStrings.RolloutPercentageInvalid)
    )]
    public string RolloutPercentage { get; set; } = "";

    // Called only after the checks above passed
    public int ToPercentage()
    {
        return int.Parse(RolloutPercentage, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
    }
}
