using System.Collections.Concurrent;
using Account.Features.PushNotifications.Shared;
using Account.Integrations.WebPush;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Account.Tests.PushNotifications;

// Push notification tests need a deployment that has a VAPID key pair, because that is what the push-notifications-enabled
// system flag is evaluated from. The pair is supplied as configuration rather than as environment variables, so a test
// class that runs beside one whose deployment has no pair is unaffected.
public class PushNotificationsWebApplicationFactory : AccountWebApplicationFactory
{
    // The public half of a key pair, base64url encoded, which is what the flag is evaluated from. No private key is
    // configured and none is needed: nothing signs here, because the sender is replaced below.
    public const string VapidPublicKey = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U";

    // A browser's subscription keys have fixed shapes the command validator enforces: an uncompressed P-256 point and a
    // 16 byte secret, both base64url
    public const string SubscriptionPublicKey = VapidPublicKey;

    public const string SubscriptionAuthSecret = "cmFuZG9tLWF1dGgtc2VjIQ";

    public RecordingPushNotificationSender PushNotificationSender { get; } = new();

    // A deployment that names no push service hosts of its own sends through the policy's default set
    protected virtual string? AllowedEndpointHosts => null;

    // How often one user may ask for a test notification is state of this process rather than of the test's database, so
    // every test of this host would otherwise inherit the allowance the test before it spent
    public override IDisposable BeginTest(AccountTestContext context)
    {
        var testScope = base.BeginTest(context);
        Services.GetRequiredService<PushTestNotificationThrottle>().Clear();
        return testScope;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["PushNotifications:VapidPublicKey"] = VapidPublicKey,
                        ["PushNotifications:Subject"] = "mailto:no-reply@platformplatform.net",
                        [PushNotificationPolicy.AllowedEndpointHostsConfigurationKey] = AllowedEndpointHosts
                    }
                );
            }
        );

        builder.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IPushNotificationSender));
                services.AddSingleton<IPushNotificationSender>(PushNotificationSender);
            }
        );
    }
}

/// <summary>
///     A deployment whose configuration names the push service hosts it sends through, the way the AppHost names the
///     address the browser harness subscribes with. The configured value replaces the default set rather than extending
///     it, so the default push services are refused here.
/// </summary>
public sealed class ConfiguredHostsPushNotificationsWebApplicationFactory : PushNotificationsWebApplicationFactory
{
    public const string ConfiguredEndpointHost = "push.harness.invalid";

    protected override string AllowedEndpointHosts => ConfiguredEndpointHost;
}

/// <summary>
///     A push service that answers from the address it is asked to deliver to, so a test states the outcome it needs in
///     the subscription it creates and no test has to reach into shared state: an endpoint under /gone/ is one the push
///     service has forgotten, an endpoint under /fail/ is one that refused, an endpoint under /timeout/ is one that never
///     answered, which is what the real sender reports as Failed when its client's timeout elapses, and every other
///     endpoint is delivered to.
///     Every send is recorded, and a test reads back only the sends to its own endpoints.
/// </summary>
public sealed class RecordingPushNotificationSender : IPushNotificationSender
{
    public const string ForgottenSubscriptionSegment = "/gone/";

    public const string RefusingSubscriptionSegment = "/fail/";

    public const string TimingOutSubscriptionSegment = "/timeout/";

    private readonly ConcurrentQueue<(PushNotificationTarget Target, PushNotificationPayload Payload)> _sends = new();

    public bool IsConfigured => true;

    public Task<PushDeliveryOutcome> SendAsync(PushNotificationTarget target, PushNotificationPayload payload, CancellationToken cancellationToken)
    {
        _sends.Enqueue((target, payload));

        if (target.Endpoint.Contains(ForgottenSubscriptionSegment, StringComparison.Ordinal)) return Task.FromResult(PushDeliveryOutcome.Expired);
        if (target.Endpoint.Contains(RefusingSubscriptionSegment, StringComparison.Ordinal)) return Task.FromResult(PushDeliveryOutcome.Failed);
        if (target.Endpoint.Contains(TimingOutSubscriptionSegment, StringComparison.Ordinal)) return Task.FromResult(PushDeliveryOutcome.Failed);

        return Task.FromResult(PushDeliveryOutcome.Delivered);
    }

    public (PushNotificationTarget Target, PushNotificationPayload Payload)[] SendsTo(string endpointPrefix)
    {
        return _sends.Where(send => send.Target.Endpoint.StartsWith(endpointPrefix, StringComparison.Ordinal)).ToArray();
    }
}
