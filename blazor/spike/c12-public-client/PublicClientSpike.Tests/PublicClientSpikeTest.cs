using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Account.Database;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using Account.Tests;
using Microsoft.Extensions.DependencyInjection;
using PublicClientSpike.Tests.Harness;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.Domain;
using SharedKernel.StronglyTypedIds;
using SharedKernel.Tests.Persistence;

namespace PublicClientSpike.Tests;

// SPIKE CODE (T012). Shared arrangement for the public-client spike tests on the real account API.
public abstract class PublicClientSpikeTest : EndpointBaseTest<AccountDbContext>, IClassFixture<PublicClientSpikeFactory>
{
    protected PublicClientSpikeTest(PublicClientSpikeFactory factory) : base(factory)
    {
        Factory = factory;
        factory.Clock.Reset();
        Harness = new NativeClientHarness(factory);
    }

    protected PublicClientSpikeFactory Factory { get; }

    protected NativeClientHarness Harness { get; }

    protected string CreateWebAccessToken(User user, SessionId sessionId)
    {
        var userInfo = new UserInfo
        {
            IsAuthenticated = true, Id = user.Id, TenantId = user.TenantId, Role = user.Role.ToString(), SessionId = sessionId, Email = user.Email, Locale = user.Locale
        };
        return new AccessTokenGenerator(WebApplicationServices.GetRequiredService<ITokenSigningClient>(), TimeProvider.System).Generate(userInfo);
    }

    protected string OwnerWebAccessToken()
    {
        return CreateWebAccessToken(DatabaseSeeder.Tenant1Owner, DatabaseSeeder.Tenant1OwnerSession.Id);
    }

    protected string MemberWebAccessToken()
    {
        return CreateWebAccessToken(DatabaseSeeder.Tenant1Member, DatabaseSeeder.Tenant1MemberSession.Id);
    }

    protected static string Claim(string jwt, string type)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.First(claim => claim.Type == type).Value;
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await NativeClientHarness.ReadJsonAsync(response);
    }

    // A second tenant where the seeded owner's email also has a membership, so the owner can switch into it
    protected (TenantId TenantId, UserId UserId) InsertSecondTenantMembership(string email)
    {
        var tenantId = TenantId.NewId();
        var userId = UserId.NewId();
        Connection.Insert("tenants", [
                ("id", tenantId.Value), ("created_at", TimeProvider.GetUtcNow()), ("modified_at", null), ("name", "Second tenant"),
                ("state", nameof(TenantState.Active)), ("logo", """{"Url":null,"Version":0}"""), ("plan", nameof(SubscriptionPlan.Basis)), ("rollout_bucket", 42)
            ]
        );
        Connection.Insert("subscriptions", [
                ("tenant_id", tenantId.Value), ("id", SubscriptionId.NewId().ToString()), ("created_at", TimeProvider.GetUtcNow()), ("modified_at", null),
                ("plan", nameof(SubscriptionPlan.Basis)), ("scheduled_plan", null), ("stripe_customer_id", null), ("stripe_subscription_id", null),
                ("current_price_amount", null), ("current_price_currency", null), ("current_period_end", null), ("cancel_at_period_end", false),
                ("first_payment_failed_at", null), ("cancellation_reason", null), ("cancellation_feedback", null), ("payment_transactions", "[]"),
                ("payment_method", null), ("billing_info", null), ("has_drift_detected", false), ("drift_checked_at", null), ("drift_discrepancies", "[]")
            ]
        );
        Connection.Insert("users", [
                ("tenant_id", tenantId.Value), ("id", userId.ToString()), ("created_at", TimeProvider.GetUtcNow()), ("modified_at", null), ("email", email),
                ("email_confirmed", true), ("first_name", "Second"), ("last_name", "Membership"), ("title", null), ("avatar", JsonSerializer.Serialize(new Avatar())),
                ("role", nameof(UserRole.Owner)), ("locale", "en-US"), ("external_identities", "[]"), ("rollout_bucket", 42)
            ]
        );
        return (tenantId, userId);
    }
}
