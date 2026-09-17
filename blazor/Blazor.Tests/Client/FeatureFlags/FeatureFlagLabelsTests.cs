using System.Globalization;
using Blazor.Client.FeatureFlags;
using FluentAssertions;
using SharedKernel.FeatureFlags;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Tests.Client.FeatureFlags;

// The registry is the list of flags a user can be shown; these tests fail when one of them reaches the sections without a
// name and a description in both cultures, which would otherwise surface as the registry's English text in Danish.
public sealed class FeatureFlagLabelsTests
{
    public static TheoryData<string> ConfigurableFlagKeys =>
        [.. FeatureFlagRegistry.GetAll().Where(IsConfigurable).Select(definition => definition.Key)];

    [Theory]
    [InlineData("account-overview", "Name", "FeatureFlagAccountOverviewName")]
    [InlineData("compact-view", "Description", "FeatureFlagCompactViewDescription")]
    public void ResourceName_WhenAKeyIsGiven_ShouldBeTheFlagKeyInPascalCaseWithTheSuffix(string flagKey, string suffix, string expected)
    {
        // Act
        var resourceName = FeatureFlagLabels.ResourceName(flagKey, suffix);

        // Assert
        resourceName.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ConfigurableFlagKeys))]
    public void TryResource_WhenTheFlagIsConfigurable_ShouldHaveANameAndADescriptionInBothCultures(string flagKey)
    {
        // Arrange
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            foreach (var culture in new[] { CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("da-DK") })
            {
                // Act
                CultureInfo.CurrentUICulture = culture;
                var name = FeatureFlagLabels.TryResource(flagKey, FeatureFlagLabels.NameSuffix);
                var description = FeatureFlagLabels.TryResource(flagKey, FeatureFlagLabels.DescriptionSuffix);

                // Assert
                name.Should().NotBeNullOrWhiteSpace($"'{flagKey}' is configurable and needs {FeatureFlagLabels.ResourceName(flagKey, FeatureFlagLabels.NameSuffix)} in {culture.Name}");
                description.Should().NotBeNullOrWhiteSpace($"'{flagKey}' is configurable and needs {FeatureFlagLabels.ResourceName(flagKey, FeatureFlagLabels.DescriptionSuffix)} in {culture.Name}");
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void TryCreateRow_WhenTheUiCultureIsDanish_ShouldUseTheDanishNameAndDescription()
    {
        // Arrange
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");

        try
        {
            // Act
            var row = FeatureFlagLabels.TryCreateRow("compact-view", true);

            // Assert
            row.Should().NotBeNull();
            row.Name.Should().Be("Kompakt visning");
            row.Description.Should().Be("Reducér afstanden mellem UI-elementer for et tættere layout");
            row.Enabled.Should().BeTrue();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public void TryCreateRow_WhenTheKeyIsNotInTheRegistry_ShouldBeNull()
    {
        // Act
        var row = FeatureFlagLabels.TryCreateRow("retired-flag", true);

        // Assert
        row.Should().BeNull();
    }

    [Fact]
    public void ConfigurableFlagKeys_WhenReadFromTheRegistry_ShouldBeTheTwoFlagsTheSectionsShow()
    {
        // Assert
        FeatureFlagRegistry.GetAll().Where(IsConfigurable).Select(definition => definition.Key).Should().BeEquivalentTo("account-overview", "compact-view");
    }

    private static bool IsConfigurable(FeatureFlagDefinition definition)
    {
        return definition.ConfigurableByTenant || definition.ConfigurableByUser;
    }
}
