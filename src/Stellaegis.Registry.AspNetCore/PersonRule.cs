using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Registry.AspNetCore;

/// <summary>
/// The rule of the registry on a gesture that reaches a person — the routes of <c>Stellaegis.Identity</c> that block an
/// account or reset its second factor, the deactivation of the directory —: the resource-based authorization of a
/// <see cref="Subject"/>, answered by <see cref="IUserDirectory.ReachesAsync"/>. A refusal fails the requirement rather
/// than leaving it unmet: another handler of the host that would let every person through does not outweigh it.
/// </summary>
internal sealed class PersonRule(IUserDirectory directory) : AuthorizationHandler<OperationRequirement, Subject>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement, Subject resource)
    {
        if (Callers.ActorOf(context.User) is not { } actor)
        {
            context.Fail();
            return;
        }
        if ((await directory.ReachesAsync(resource.Value, actor).ConfigureAwait(false)).IsSuccess)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

/// <summary>The routes of the directory that act on a person: the person of their <c>{subject}</c> is part of the decision.</summary>
internal static class PersonTargets
{
    /// <summary>
    /// Declares that the route acts on the person of its <c>{subject}</c>: before it runs, the person is loaded as a
    /// <see cref="Subject"/> and asked about, the route's operation as the requirement; a refusal is an absence.
    /// </summary>
    public static RouteHandlerBuilder ActsOnPerson(this RouteHandlerBuilder route)
        => route
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                if (http.Request.RouteValues["subject"] is not string raw || !RegistryRules.IsSubject(raw)
                    || !Subject.TryParse(RegistryRules.NormalizeSubject(raw), out var subject))
                    return await next(context);   // the route answers what is not a subject
                if (!await http.AuthorizeResourceAsync(subject))
                    return Result.Failure(RegistryErrors.UserNotFound(subject.Value)).ToHttpResult();
                return await next(context);
            })
            .ProducesProblem(StatusCodes.Status404NotFound);
}
