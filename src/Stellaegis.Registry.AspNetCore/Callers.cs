using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry.AspNetCore;

/// <summary>
/// Reads the actor of a request from the principal the host authenticated. The routes of the registry read no
/// request context, the module having no compartments: the actor of a write comes from the claims, as the
/// administration routes of Identity read it — <c>NameIdentifier</c>, <c>sub</c>, then <c>client_id</c>.
/// </summary>
internal static class Callers
{
    private static readonly string[] ActorClaims = [ClaimTypes.NameIdentifier, "sub", "client_id"];

    /// <summary>The actor of the request, never <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">An authenticated principal carries none of the claims, or a value that is not an actor.</exception>
    public static Actor ActorOf(HttpContext httpContext)
    {
        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return httpContext.RequestServices.GetStellaegisFrame().AnonymousActor;

        foreach (var type in ActorClaims)
        {
            if (user.FindFirst(type)?.Value is not { } value)
                continue;
            if (Actor.TryParse(value, out var actor))
                return actor;
            throw new InvalidOperationException(
                $"The claim {type} of the authenticated user is not an actor: '{value}'. An actor is 1 to {Actor.MaxLength} "
                + "characters without control characters nor surrounding whitespace.");
        }
        throw new InvalidOperationException(
            "The authenticated user carries none of the claims an actor is read from: NameIdentifier, sub, client_id. "
            + "Recording the request as anonymous would attribute it to nobody: map the claim the token carries onto one of them.");
    }
}
