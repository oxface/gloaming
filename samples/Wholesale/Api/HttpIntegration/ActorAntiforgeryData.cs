using Gloaming.ActorIdentity;
using Microsoft.AspNetCore.Antiforgery;

namespace ModulithFoundry.Samples.Wholesale.Api.HttpIntegration;

// Native antiforgery services are singleton; resolve the established actor per request.
public sealed class ActorAntiforgeryData : IAntiforgeryAdditionalDataProvider
{
    public string GetAdditionalData(HttpContext context) => RequireHumanActor(context);

    public bool ValidateAdditionalData(HttpContext context, string additionalData) =>
        string.Equals(RequireHumanActor(context), additionalData, StringComparison.Ordinal);

    private static string RequireHumanActor(HttpContext context)
    {
        Actor actor = context
            .RequestServices.GetRequiredService<IActorContextAccessor>()
            .Current.Actor;
        return actor.Kind == ActorKind.Human && actor.Id is not null
            ? actor.Id.Value
            : throw new InvalidOperationException(
                "Antiforgery requires an established human actor."
            );
    }
}
