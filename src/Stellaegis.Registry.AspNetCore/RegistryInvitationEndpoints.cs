using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Registry.AspNetCore;

/// <summary>
/// Maps the administration of the invitations, apart from the other routes of the registry: a host maps them when it
/// lets people in by invitation. Decided in the shared space, as every route of the registry; the registry checks the
/// roles given are in the assignable lists of the author's roles.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>POST registry/compartments/{compartment}/invitations</c> (201) — <c>invitations.write</c>: the token is in the answer, this once, for a link handed over</item>
///   <item><c>GET registry/compartments/{compartment}/invitations</c> — <c>memberships.read</c></item>
///   <item><c>DELETE registry/invitations/{id}</c> — <c>invitations.write</c>: its author, the owner of its compartment, or anyone who could issue it</item>
/// </list>
/// The acceptance is no route of the administration: the page of the accounts calls <see cref="IInvitations"/> in process.
/// </remarks>
public static class RegistryInvitationEndpoints
{
    /// <summary>
    /// Maps the invitation routes, relative to <paramref name="endpoints"/>, under <c>registry</c>. A host that sends no
    /// invitation — no <see cref="IInvitationSender"/> — and does not let them be handed over
    /// (<c>Stellaegis:Registry:Invitations:HandDelivery</c>) could invite nobody, and is refused here, at startup.
    /// </summary>
    public static RouteGroupBuilder MapRegistryInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Guard.NotNull(endpoints);
        var services = endpoints.ServiceProvider;
        if (services.GetService<IInvitationSender>() is null && !services.GetRequiredService<IOptions<RegistryOptions>>().Value.Invitations.HandDelivery)
            throw new InvalidOperationException(
                "The invitations are mapped, and the host can deliver none: register an IInvitationSender, "
                + "or declare Stellaegis:Registry:Invitations:HandDelivery.");

        var group = endpoints.MapGroup("registry")
            .BelongsTo<RegistryModule>()
            .LoadsNoResource()
            .AddErrorTranslation();

        group.MapPost("/compartments/{compartment}/invitations", async (string compartment, InviteRequest request, HttpContext http, IInvitations service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return Invalid($"'{compartment}' is not a compartment.");
                if (request is null || string.IsNullOrWhiteSpace(request.Email) || request.Roles is null || request.Roles.Any(role => !RegistryRules.IsRoleName(role)))
                    return Invalid("An invitation has an address and an array of role names.");
                if (!TryDelivery(request.Delivery, out var delivery))
                    return Invalid("A delivery is email or hand.");
                var issued = await service.IssueAsync(parsed, request.Email, request.Roles, delivery, Callers.ActorOf(http), request.DesignatesOwner, ct);
                loggers.CreateLogger("Stellaegis.Registry").LogInformation("{Actor} invited to compartment {Compartment}", Callers.ActorOf(http), parsed);
                return issued.IsSuccess
                    ? Results.Created((string?)null, new InvitationIssued(InvitationSummary.Of(issued.Value.Invitation), issued.Value.Token))
                    : issued.ToHttpResult();
            })
            .WithMetadata(RegistryOperations.InvitationsWrite)
            .Produces<InvitationIssued>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/compartments/{compartment}/invitations", async (string compartment, IInvitations service, CancellationToken ct) =>
                Compartment.TryParse(compartment, out var parsed)
                    ? Results.Ok((await service.ListAsync(parsed, ct)).Select(InvitationSummary.Of).ToArray())
                    : Invalid($"'{compartment}' is not a compartment."))
            .WithMetadata(RegistryOperations.MembershipsRead)
            .Produces<InvitationSummary[]>(StatusCodes.Status200OK);

        group.MapDelete("/invitations/{id:guid}", async (Guid id, HttpContext http, IInvitations service, CancellationToken ct) =>
                (await service.RevokeAsync(id, Callers.ActorOf(http), ct)).ToHttpResult())
            .WithMetadata(RegistryOperations.InvitationsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        return group;
    }

    private static bool TryDelivery(string? value, out InvitationDelivery delivery)
    {
        delivery = InvitationDelivery.Email;
        if (value is null)
            return true;
        if (Statuses.TryParse(value, out InvitationDelivery parsed))
        {
            delivery = parsed;
            return true;
        }
        return false;
    }

    private static IResult Invalid(string message) => Result.Failure(Error.Validation("Registry.Invalid", message)).ToHttpResult();
}

/// <summary>The body of an invitation.</summary>
/// <param name="Email">The address invited.</param>
/// <param name="Roles">The compartment roles it gives, each in the assignable lists of the author's roles.</param>
/// <param name="Delivery"><c>email</c>, the default, or <c>hand</c> when the host declares it.</param>
/// <param name="DesignatesOwner">Whether it seats the owner of a compartment that has none.</param>
public sealed record InviteRequest(string Email, string[] Roles, string? Delivery = null, bool DesignatesOwner = false);

/// <summary>An invitation, as the routes answer it; never its token.</summary>
/// <param name="InvitationId">Its identity.</param>
/// <param name="Compartment">The compartment it admits to.</param>
/// <param name="Email">The address invited.</param>
/// <param name="Roles">The roles it gives.</param>
/// <param name="DesignatesOwner">Whether it seats the owner.</param>
/// <param name="Delivery"><c>email</c> or <c>hand</c>.</param>
/// <param name="IssuedAt">When it was issued.</param>
/// <param name="IssuedBy">Its author.</param>
/// <param name="ExpiresAt">When it lapses.</param>
/// <param name="AcceptedAt">When it was accepted, or <see langword="null"/>.</param>
/// <param name="AcceptedBy">The account that accepted it, or <see langword="null"/>.</param>
/// <param name="RevokedAt">When it was revoked, or <see langword="null"/>.</param>
public sealed record InvitationSummary(
    Guid InvitationId, string Compartment, string Email, string[] Roles, bool DesignatesOwner, string Delivery,
    DateTimeOffset IssuedAt, string IssuedBy, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string? AcceptedBy, DateTimeOffset? RevokedAt)
{
    internal static InvitationSummary Of(Invitation invitation)
        => new(invitation.InvitationId, invitation.Compartment.Value, invitation.Email, invitation.Roles.ToArray(), invitation.DesignatesOwner,
            invitation.Delivery.ToString().ToLowerInvariant(), invitation.Issued.At, invitation.Issued.By.Value, invitation.ExpiresAt,
            invitation.AcceptedAt, invitation.AcceptedBy, invitation.Revoked?.At);
}

/// <summary>An invitation just issued.</summary>
/// <param name="Invitation">The invitation.</param>
/// <param name="Token">The token of its link, this once, when it is handed over; <see langword="null"/> when it was sent by e-mail.</param>
public sealed record InvitationIssued(InvitationSummary Invitation, string? Token);
