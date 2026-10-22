using Microsoft.AspNetCore.Antiforgery;

namespace ModulithFoundry.Samples.Wholesale.Api.HttpIntegration;

public sealed class ValidateAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        CancellationToken cancellation = context.HttpContext.RequestAborted;
        cancellation.ThrowIfCancellationRequested();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery validation failed."
            );
        }
        cancellation.ThrowIfCancellationRequested();
        return await next(context);
    }
}
