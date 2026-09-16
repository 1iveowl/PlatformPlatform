namespace Blazor.Host.Shell;

// Marks a page that exists for local verification only, such as the content security policy probe. Outside the
// Development environment the request is answered with 404 before the page renders.
[AttributeUsage(AttributeTargets.Class)]
public sealed class DevelopmentOnlyAttribute : Attribute;

public static class DevelopmentOnlyPages
{
    // Runs after routing, so the endpoint metadata is known
    public static Task RejectOutsideDevelopmentAsync(HttpContext context, RequestDelegate next, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || context.GetEndpoint()?.Metadata.GetMetadata<DevelopmentOnlyAttribute>() is null)
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
