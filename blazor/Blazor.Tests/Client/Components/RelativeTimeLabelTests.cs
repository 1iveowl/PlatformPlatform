using System.Globalization;
using Blazor.Client.BackOffice.Billing;
using Blazor.Client.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blazor.Tests.Client.Components;

// The relative time labels of the lists and the dashboard measure against the registered clock, so a container that
// registers a fixed clock decides the label at every boundary without waiting for real time to pass
public sealed class RelativeTimeLabelTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US", 59, "Just now")]
    [InlineData("en-US", 60, "1 minute ago")]
    [InlineData("en-US", 3599, "59 minutes ago")]
    [InlineData("en-US", 3600, "1 hour ago")]
    [InlineData("en-US", 86399, "23 hours ago")]
    [InlineData("da-DK", 60, "1 minut siden")]
    public async Task Render_WhenClockIsFixed_ShouldWordTheValueAgainstThatClock(string culture, int secondsAgo, string expected)
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { [nameof(RelativeTimeLabel.Value)] = FixedNow.AddSeconds(-secondsAgo) };

        // Act
        var html = await RenderAsync<RelativeTimeLabel>(new FixedTimeProvider(FixedNow), culture, parameters);

        // Assert
        html.Should().Be(expected);
    }

    [Fact]
    public async Task Render_WhenClockMoves_ShouldWordTheSameValueAgainstTheNewTime()
    {
        // Arrange
        var clock = new FixedTimeProvider(FixedNow);
        var parameters = new Dictionary<string, object?> { [nameof(RelativeTimeLabel.Value)] = FixedNow };
        var before = await RenderAsync<RelativeTimeLabel>(clock, "en-US", parameters);

        // Act
        clock.Now = FixedNow.AddMinutes(2);
        var after = await RenderAsync<RelativeTimeLabel>(clock, "en-US", parameters);

        // Assert
        before.Should().Be("Just now");
        after.Should().Be("2 minutes ago");
    }

    [Fact]
    public async Task Render_WhenValueIsMissing_ShouldRenderNothing()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { [nameof(RelativeTimeLabel.Value)] = null };

        // Act
        var html = await RenderAsync<RelativeTimeLabel>(new FixedTimeProvider(FixedNow), "en-US", parameters);

        // Assert
        html.Should().BeEmpty();
    }

    [Fact]
    public async Task DateCell_WhenClockIsFixed_ShouldShowTheRelativeTimeOfThatClock()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { [nameof(FragmentHost.Content)] = BillingCells.DateCell(FixedNow.AddHours(-3)) };

        // Act
        var html = await RenderAsync<FragmentHost>(new FixedTimeProvider(FixedNow), "en-US", parameters);

        // Assert
        html.Should().StartWith("<span class=\"account-name-cell\"><span>3 hours ago</span>");
    }

    private static async Task<string> RenderAsync<TComponent>(TimeProvider clock, string culture, Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().AddSingleton(clock).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                var previousUiCulture = CultureInfo.CurrentUICulture;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                try
                {
                    var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
                    return output.ToHtmlString();
                }
                finally
                {
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
            }
        );
    }

    // Renders a fragment such as a list cell on its own, the way a list column renders it
    private sealed class FragmentHost : IComponent
    {
        private RenderHandle _renderHandle;

        [Parameter]
        public RenderFragment? Content { get; set; }

        public void Attach(RenderHandle renderHandle)
        {
            _renderHandle = renderHandle;
        }

        public Task SetParametersAsync(ParameterView parameters)
        {
            parameters.SetParameterProperties(this);
            _renderHandle.Render(Content ?? (_ => { }));
            return Task.CompletedTask;
        }
    }
}
