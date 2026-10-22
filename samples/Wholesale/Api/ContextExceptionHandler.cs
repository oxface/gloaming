using Gloaming.ActorIdentity.AspNetCore;
using Gloaming.Tenancy;
using Gloaming.Tenancy.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ModulithFoundry.Samples.Wholesale.Api;

// Consumer-owned mappings. Unknown failures use native exception-handler fallback.
internal sealed class ContextExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        (int Status, string Title)? response = exception switch
        {
            HttpActorResolutionException => (
                StatusCodes.Status403Forbidden,
                "Actor mapping failed."
            ),
            TenantRequiredException => (StatusCodes.Status400BadRequest, "Select an Organization."),
            HttpTenantResolutionException => (
                StatusCodes.Status404NotFound,
                "Organization selection failed."
            ),
            _ => null,
        };
        if (response is not { } failure)
            return false;
        httpContext.Response.StatusCode = failure.Status;
        await problems.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = failure.Status,
                    Title = failure.Title,
                },
            }
        );
        return true;
    }
}
