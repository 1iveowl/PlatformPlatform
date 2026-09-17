using Blazor.Client.Components.Images;
using FluentAssertions;

namespace Blazor.Tests.Client.Components;

public sealed class ImageDraftTests
{
    private const string SavedAvatarUrl = "/avatars/tenant/user/saved.png";

    [Fact]
    public void DisplayUrl_WhenNothingChanged_ShouldShowTheSavedAvatar()
    {
        // Arrange
        var draft = new ImageDraft();

        // Act and Assert
        draft.HasChanges.Should().BeFalse();
        draft.DisplayUrl(SavedAvatarUrl).Should().Be(SavedAvatarUrl);
    }

    [Fact]
    public void Select_WhenAFileReplacesAnEarlierSelection_ShouldUploadTheLatestFileAndShowItsPreview()
    {
        // Arrange
        var draft = new ImageDraft();
        draft.Select("image/png", "blob:first");

        // Act
        draft.Select("image/webp", "blob:second");

        // Assert
        draft.Intent.Should().Be(ImageIntent.Upload);
        draft.ContentType.Should().Be("image/webp");
        draft.DisplayUrl(SavedAvatarUrl).Should().Be("blob:second");
    }

    [Fact]
    public void Remove_WhenAnAvatarIsSaved_ShouldRemoveItAndShowTheInitials()
    {
        // Arrange
        var draft = new ImageDraft();
        draft.Select("image/png", "blob:selected");

        // Act
        draft.Remove(SavedAvatarUrl);

        // Assert
        draft.Intent.Should().Be(ImageIntent.Remove);
        draft.ContentType.Should().BeNull();
        draft.DisplayUrl(SavedAvatarUrl).Should().BeNull();
    }

    [Fact]
    public void Remove_WhenOnlyAnUnsavedSelectionExists_ShouldReturnToNoChange()
    {
        // Arrange
        var draft = new ImageDraft();
        draft.Select("image/png", "blob:selected");

        // Act
        draft.Remove(null);

        // Assert
        draft.HasChanges.Should().BeFalse();
        draft.DisplayUrl(null).Should().BeNull();
    }
}
