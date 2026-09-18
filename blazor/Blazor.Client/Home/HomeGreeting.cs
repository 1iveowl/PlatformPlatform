// The greeting the authenticated home page opens with, the React edition's time-based greeting: the local hour of day
// decides which of the four greetings is shown, and the user's first name is used when the account has one. The decision
// lives here rather than in the component so both the prerendered and the interactive render ask the same question of the
// same texts, and so the boundaries are covered by tests.

using System.Globalization;

namespace Blazor.Client.Home;

public enum HomeGreetingKind
{
    Night,
    Morning,
    Afternoon,
    Evening
}

public static class HomeGreeting
{
    public static HomeGreetingKind GetKind(int hourOfDay)
    {
        return hourOfDay switch
        {
            >= 0 and < 5 => HomeGreetingKind.Night,
            >= 5 and < 12 => HomeGreetingKind.Morning,
            >= 12 and < 17 => HomeGreetingKind.Afternoon,
            _ => HomeGreetingKind.Evening
        };
    }

    public static string GetText(int hourOfDay, string? firstName)
    {
        var name = firstName?.Trim();
        var kind = GetKind(hourOfDay);
        if (string.IsNullOrEmpty(name))
        {
            return kind switch
            {
                HomeGreetingKind.Night => CommonStrings.GreetingNight,
                HomeGreetingKind.Morning => CommonStrings.GreetingMorning,
                HomeGreetingKind.Afternoon => CommonStrings.GreetingAfternoon,
                _ => CommonStrings.GreetingEvening
            };
        }

        var template = kind switch
        {
            HomeGreetingKind.Night => CommonStrings.GreetingNightNamed,
            HomeGreetingKind.Morning => CommonStrings.GreetingMorningNamed,
            HomeGreetingKind.Afternoon => CommonStrings.GreetingAfternoonNamed,
            _ => CommonStrings.GreetingEveningNamed
        };
        return string.Format(CultureInfo.CurrentCulture, template, name);
    }
}
