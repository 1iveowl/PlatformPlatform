using Blazor.Client.Forms;
using FluentAssertions;

namespace Blazor.Tests.Client;

public sealed class ToastServiceTests
{
    [Fact]
    public void Show_ShouldAddToastWithUniqueIdAndNotify()
    {
        // Arrange
        var toasts = new ToastService();
        var notifications = 0;
        toasts.Changed += () => notifications++;

        // Act
        var first = toasts.Show(ToastKind.Error, "Something went wrong", "Conflict", "api-failure-toast");
        var second = toasts.Show(ToastKind.Warning, "This page has expired.", null, "antiforgery-recovery-toast", "Reload page", () => { });

        // Assert
        toasts.Toasts.Should().Equal(first, second);
        first.Id.Should().NotBe(second.Id);
        notifications.Should().Be(2);
    }

    [Fact]
    public void Show_WhenMoreThanTheMaximumAreShown_ShouldDropTheOldest()
    {
        // Arrange
        var toasts = new ToastService();
        var shown = Enumerable.Range(1, ToastService.MaximumVisibleToasts + 1).Select(index => toasts.Show(ToastKind.Error, "Title", $"Message {index}", "api-failure-toast")).ToArray();

        // Act
        var visible = toasts.Toasts;

        // Assert
        visible.Should().Equal(shown.Skip(1));
    }

    [Fact]
    public void Dismiss_ShouldRemoveOnlyThatToastAndNotifyOnce()
    {
        // Arrange
        var toasts = new ToastService();
        var first = toasts.Show(ToastKind.Error, "Title", "First", "api-failure-toast");
        var second = toasts.Show(ToastKind.Error, "Title", "Second", "api-failure-toast");
        var notifications = 0;
        toasts.Changed += () => notifications++;

        // Act
        toasts.Dismiss(first.Id);
        toasts.Dismiss(first.Id);

        // Assert
        toasts.Toasts.Should().Equal(second);
        notifications.Should().Be(1);
    }

    [Fact]
    public void Clear_ShouldRemoveEveryToast()
    {
        // Arrange
        var toasts = new ToastService();
        toasts.Show(ToastKind.Error, "Title", "First", "api-failure-toast");

        // Act
        toasts.Clear();

        // Assert
        toasts.Toasts.Should().BeEmpty();
    }
}
