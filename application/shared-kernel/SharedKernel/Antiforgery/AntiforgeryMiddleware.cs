using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Antiforgery;

public sealed class AntiforgeryMiddleware(IAntiforgery antiforgery, ILogger<AntiforgeryMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation == false)
        {
            // Skip validation for endpoints with disabled antiforgery
            await next(context);
            return;
        }

        if (bool.TryParse(Environment.GetEnvironmentVariable("BypassAntiforgeryValidation"), out var bypass) && bypass)
        {
            logger.LogDebug("Bypassing antiforgery validation due to environment variable setting");
            await next(context);
            return;
        }

        // For form-bound endpoints the framework antiforgery middleware has already validated the request, and reading
        // the form again after a failed validation throws, so its result is used instead of validating twice
        var validationFeature = context.Features.Get<IAntiforgeryValidationFeature>();
        var isRequestValid = validationFeature?.IsValid ?? await antiforgery.IsRequestValidAsync(context);
        if (!isRequestValid)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            logger.LogWarning(
                "Antiforgery validation failed for {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                traceId
            );

            await Results.Problem(
                title: "Invalid Antiforgery Token",
                detail: "Antiforgery validation failed for request.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { { "traceId", traceId } }
            ).ExecuteAsync(context);

            return;
        }

        await next(context);
    }
}
