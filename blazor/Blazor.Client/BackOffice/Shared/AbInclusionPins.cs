// The choices of the back office's feature flag rollouts dialog, the React back office's SetAbInclusionPinDialog: Default
// stands for no pin, which the API receives as null, and the outcome of a save is named for the account or user it changed.

using System.Globalization;
using SharedKernel.FeatureFlags;

namespace Blazor.Client.BackOffice.Shared;

public enum AbInclusionPinChoice
{
    Default,
    AlwaysOn,
    NeverOn
}

public static class AbInclusionPins
{
    public static readonly AbInclusionPinChoice[] Choices = [AbInclusionPinChoice.Default, AbInclusionPinChoice.AlwaysOn, AbInclusionPinChoice.NeverOn];

    public static AbInclusionPinChoice ToChoice(AbInclusionPin? pin)
    {
        return pin switch
        {
            AbInclusionPin.AlwaysOn => AbInclusionPinChoice.AlwaysOn,
            AbInclusionPin.NeverOn => AbInclusionPinChoice.NeverOn,
            _ => AbInclusionPinChoice.Default
        };
    }

    public static AbInclusionPin? ToPin(AbInclusionPinChoice choice)
    {
        return choice switch
        {
            AbInclusionPinChoice.AlwaysOn => AbInclusionPin.AlwaysOn,
            AbInclusionPinChoice.NeverOn => AbInclusionPin.NeverOn,
            _ => null
        };
    }

    public static string GetTitle(AbInclusionPinChoice choice)
    {
        return choice switch
        {
            AbInclusionPinChoice.AlwaysOn => BackOfficeStrings.FirstInRollouts,
            AbInclusionPinChoice.NeverOn => BackOfficeStrings.LastInRollouts,
            _ => BackOfficeStrings.DefaultInRollouts
        };
    }

    public static string GetDescription(AbInclusionPinChoice choice)
    {
        return choice switch
        {
            AbInclusionPinChoice.AlwaysOn => BackOfficeStrings.FirstInRolloutsDescription,
            AbInclusionPinChoice.NeverOn => BackOfficeStrings.LastInRolloutsDescription,
            _ => BackOfficeStrings.DefaultInRolloutsDescription
        };
    }

    public static string GetSavedMessage(string entityLabel, AbInclusionPin? pin)
    {
        var format = pin switch
        {
            AbInclusionPin.AlwaysOn => BackOfficeStrings.NowFirstInRollouts,
            AbInclusionPin.NeverOn => BackOfficeStrings.NowLastInRollouts,
            _ => BackOfficeStrings.RolloutsResetToDefault
        };
        return string.Format(CultureInfo.CurrentCulture, format, entityLabel);
    }
}
