using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Registry.AspNetCore;

/// <summary>
/// Maps the administration routes of the registry: what an operator of the platform does to the compartments, the
/// users, the roles, the memberships and the operators, through the services of the registry — never its stores —,
/// so that every change recomputes the effective rights and hands them to the <see cref="IRightsSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// The module has no compartments: it acts <em>on</em> them, from the shared space. Its routes read no request
/// context, the actor of a write is read from the principal the host authenticated — the registry keeps it on
/// the row it writes, and the host's journal gets it too —, and the host's rights system
/// decides them in the shared space — an operator of the platform who is also a member of a compartment is, in
/// that compartment, in the situation of any member. A compartment, a user or a role is nobody's resource, so
/// the routes declare they load none: the operation each is labelled with is the whole decision.
/// </para>
/// <list type="bullet">
///   <item><c>GET registry/compartments</c>, <c>GET registry/compartments/{compartment}</c> — <c>compartments.read</c></item>
///   <item><c>POST registry/compartments</c> (201, with its first owner or without), <c>PUT registry/compartments/{compartment}</c> (the display name), <c>POST …/suspend</c>, <c>…/resume</c>, <c>…/terminate</c>, <c>PUT …/owner/{subject}</c> (the first owner, once) — <c>compartments.write</c></item>
///   <item><c>GET registry/compartments/{compartment}/owner/transfer</c> — <c>compartments.read</c>; <c>PUT …/owner/transfer</c>, <c>POST …/owner/transfer/accept</c>, <c>DELETE …/owner/transfer</c> — <c>ownership.write</c>, the registry checking the author is the owner, or the recipient</item>
///   <item><c>GET registry/users</c>, <c>GET registry/users/{subject}</c> — <c>directory.read</c>; no creation: a user is the projection of an account of Identity, recorded by the host from <c>UserRegistered</c></item>
///   <item><c>POST registry/users/{subject}/deactivate</c>, <c>…/reactivate</c> — <c>directory.write</c></item>
///   <item><c>GET registry/catalog</c>, <c>GET registry/catalog/{module}</c> — <c>catalog.read</c></item>
///   <item><c>GET registry/roles?scope=</c>, <c>GET registry/roles/{role}</c> — <c>roles.read</c></item>
///   <item><c>POST registry/roles</c> (201), <c>PUT registry/roles/{role}</c>, <c>DELETE registry/roles/{role}</c> — <c>roles.write</c>; a declared role is changed, never deleted</item>
///   <item><c>GET registry/memberships/compartments/{compartment}</c>, <c>GET registry/memberships/users/{subject}</c> — <c>memberships.read</c></item>
///   <item><c>PUT registry/memberships/{compartment}/{subject}</c>, <c>DELETE …</c>, <c>PUT …/roles/{role}</c>, <c>DELETE …/roles/{role}</c> — <c>memberships.write</c></item>
///   <item><c>GET registry/operators</c>, <c>GET registry/operators/{subject}</c> — <c>operators.read</c></item>
///   <item><c>PUT registry/operators/{subject}/roles/{role}</c> (202, an act to validate; 204 for the installation), <c>DELETE …</c> — <c>operators.write</c></item>
///   <item><c>GET registry/validations</c> — <c>operators.read</c>; <c>POST registry/validations/{id}/validate</c>, <c>…/reject</c> — <c>validations.write</c>, never one's own act</item>
///   <item><c>PUT registry/compartments/{compartment}/owner/rescue/{subject}</c> (202) — <c>ownership.rescue</c></item>
/// </list>
/// <para>
/// A refusal of a service is answered as problem details with its code — <c>Role.Declared</c>, <c>Role.InUse</c>,
/// <c>Catalog.UnknownOperation</c>, <c>Membership.NotMember</c>… —; a malformed name is a 400 with the code
/// <c>Registry.Invalid</c>. The routes carry no name, so the module can be mapped several times in one host.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddStellaegisRegistryPostgreSQL(builder.Configuration);
/// builder.Services.AddStellaegisRegistryAdministration();
///
/// var app = builder.Build();
/// app.UseAuthentication();
/// app.UseAuthorization();
/// app.MapGroup("/api/admin").MapRegistryAdminEndpoints().RequireAuthorizationPerOperation(op => op.ToString());
/// </code>
/// </example>
public static class RegistryAdminEndpoints
{
    private const string InvalidCode = "Registry.Invalid";

    /// <summary>
    /// Registers the startup check of the routes, which <see cref="MapRegistryAdminEndpoints"/> needs: a module
    /// without compartments registers it from its own registration, since no request context does. Harmless when
    /// something else registered it.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryAdministration(this IServiceCollection services)
    {
        Guard.NotNull(services);
        return services.AddStellaegisStartupCheck();
    }

    /// <summary>
    /// Maps the administration routes of the registry, relative to <paramref name="endpoints"/>, under
    /// <c>registry</c>. The host declares who may call them on the returned group.
    /// </summary>
    /// <param name="endpoints">The builder to map the routes on — the application, or a route group.</param>
    /// <returns>The group holding the routes, for the host's authorization and further conventions.</returns>
    public static RouteGroupBuilder MapRegistryAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Guard.NotNull(endpoints);
        var group = endpoints.MapGroup("registry")
            .BelongsTo<RegistryModule>()
            .LoadsNoResource()
            .AddErrorTranslation();

        MapCompartments(group.MapGroup("/compartments"));
        MapUsers(group.MapGroup("/users"));
        MapCatalog(group.MapGroup("/catalog"));
        MapRoles(group.MapGroup("/roles"));
        MapMemberships(group.MapGroup("/memberships"));
        MapOperators(group.MapGroup("/operators"));
        MapValidations(group.MapGroup("/validations"));
        return group;
    }

    private static void MapValidations(RouteGroupBuilder validations)
    {
        validations.MapGet("/", async (IValidations service, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(ct)).Select(PendingActSummary.Of).ToArray()))
            .WithMetadata(RegistryOperations.OperatorsRead)
            .Produces<PendingActSummary[]>(StatusCodes.Status200OK);

        validations.MapPost("/{id:guid}/validate", async (Guid id, HttpContext http, IValidations service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                Written(http, loggers, "validated act {Act}", id);
                return (await service.ValidateAsync(id, Callers.ActorOf(http), ValidationChannel.Screen, ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.ValidationsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        validations.MapPost("/{id:guid}/reject", async (Guid id, HttpContext http, IValidations service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                Written(http, loggers, "rejected act {Act}", id);
                return (await service.RejectAsync(id, Callers.ActorOf(http), ValidationChannel.Screen, ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.ValidationsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // An act proposed: it takes effect once validated by a second channel.
    private static IResult Pending(PendingAct act) => Results.Accepted((string?)null, PendingActSummary.Of(act));

    private static void MapCompartments(RouteGroupBuilder compartments)
    {
        compartments.MapGet("/", async (ICompartments service, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(ct)).Select(CompartmentSummary.Of).ToArray()))
            .WithMetadata(RegistryOperations.CompartmentsRead)
            .Produces<CompartmentSummary[]>(StatusCodes.Status200OK);

        compartments.MapGet("/{compartment}", async (string compartment, ICompartments service, CancellationToken ct) =>
                Compartment.TryParse(compartment, out var parsed)
                    ? Summary(await service.GetAsync(parsed, ct), CompartmentSummary.Of)
                    : NotACompartment(compartment))
            .WithMetadata(RegistryOperations.CompartmentsRead)
            .Produces<CompartmentSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        compartments.MapPost("/", async (CreateCompartmentRequest request, HttpContext http, ICompartments service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (request is null || !Compartment.TryParse(request.Compartment, out var parsed) || string.IsNullOrWhiteSpace(request.DisplayName))
                    return Invalid("A compartment has a name in the grammar of a compartment and a display name.");
                if (request.Owner is not null && !RegistryRules.IsSubject(request.Owner))
                    return NotASubject(request.Owner);
                var created = request.Owner is null
                    ? await service.CreateAsync(parsed, request.DisplayName, Callers.ActorOf(http), ct)
                    : await service.CreateAsync(parsed, request.DisplayName, request.Owner, Callers.ActorOf(http), ct);
                Written(http, loggers, "created compartment {Compartment}", parsed);
                return created.IsSuccess ? Results.Created((string?)null, CompartmentSummary.Of(created.Value)) : created.ToHttpResult();
            })
            .WithMetadata(RegistryOperations.CompartmentsWrite)
            .Produces<CompartmentSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        compartments.MapPut("/{compartment}", async (string compartment, RenameCompartmentRequest request, HttpContext http, ICompartments service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
                    return Invalid("A compartment has a display name.");
                Written(http, loggers, "renamed compartment {Compartment}", parsed);
                return Summary(await service.RenameAsync(parsed, request.DisplayName, Callers.ActorOf(http), ct), CompartmentSummary.Of);
            })
            .WithMetadata(RegistryOperations.CompartmentsWrite)
            .Produces<CompartmentSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        MapTransition(compartments, "suspend", (service, compartment, actor, ct) => service.SuspendAsync(compartment, actor, ct));
        MapTransition(compartments, "resume", (service, compartment, actor, ct) => service.ResumeAsync(compartment, actor, ct));
        MapTransition(compartments, "terminate", (service, compartment, actor, ct) => service.TerminateAsync(compartment, actor, ct));
        MapOwnership(compartments);
    }

    private static void MapOwnership(RouteGroupBuilder compartments)
    {
        compartments.MapPut("/{compartment}/owner/{subject}", async (string compartment, string subject, HttpContext http, IOwnership service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "seated the owner of compartment {Compartment}", parsed);
                return (await service.SeatAsync(parsed, subject, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.CompartmentsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        compartments.MapPut("/{compartment}/owner/rescue/{subject}", async (string compartment, string subject, HttpContext http, IOwnership service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "proposed the rescue of compartment {Compartment}", parsed);
                var rescued = await service.RescueAsync(parsed, subject, Callers.ActorOf(http), ct);
                if (rescued.IsFailure)
                    return rescued.ToHttpResult();
                return rescued.Value is { } pending ? Pending(pending) : Results.NoContent();
            })
            .WithMetadata(RegistryOperations.OwnershipRescue)
            .Produces<PendingActSummary>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        compartments.MapGet("/{compartment}/owner/transfer", async (string compartment, IOwnership service, CancellationToken ct) =>
                Compartment.TryParse(compartment, out var parsed)
                    ? Summary(await service.GetTransferAsync(parsed, ct), TransferSummary.Of)
                    : NotACompartment(compartment))
            .WithMetadata(RegistryOperations.CompartmentsRead)
            .Produces<TransferSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        compartments.MapPut("/{compartment}/owner/transfer", async (string compartment, ProposeTransferRequest request, HttpContext http, IOwnership service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (request is null || !RegistryRules.IsSubject(request.To) || (request.Keep is not null && request.Keep.Any(role => !RegistryRules.IsRoleName(role))))
                    return Invalid("A transfer has the subject of its recipient, and the names of the roles the owner keeps or none.");
                Written(http, loggers, "proposed the ownership of compartment {Compartment}", parsed);
                return Summary(await service.ProposeAsync(parsed, request.To, request.Keep, Callers.ActorOf(http), ct), TransferSummary.Of);
            })
            .WithMetadata(RegistryOperations.OwnershipWrite)
            .Produces<TransferSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        compartments.MapPost("/{compartment}/owner/transfer/accept", async (string compartment, HttpContext http, IOwnership service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                Written(http, loggers, "accepted the ownership of compartment {Compartment}", parsed);
                return (await service.AcceptAsync(parsed, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.OwnershipWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        compartments.MapDelete("/{compartment}/owner/transfer", async (string compartment, HttpContext http, IOwnership service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                Written(http, loggers, "withdrew the transfer of compartment {Compartment}", parsed);
                return (await service.CancelAsync(parsed, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.OwnershipWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapTransition(RouteGroupBuilder compartments, string verb, Func<ICompartments, Compartment, Actor, CancellationToken, Task<Result<CompartmentRecord>>> transition)
        => compartments.MapPost($"/{{compartment}}/{verb}", async (string compartment, HttpContext http, ICompartments service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                Written(http, loggers, verb + "d compartment {Compartment}", parsed);
                return Summary(await transition(service, parsed, Callers.ActorOf(http), ct), CompartmentSummary.Of);
            })
            .WithMetadata(RegistryOperations.CompartmentsWrite)
            .Produces<CompartmentSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

    private static void MapUsers(RouteGroupBuilder users)
    {
        users.MapGet("/", async (IUserDirectory directory, CancellationToken ct) =>
                Results.Ok((await directory.ListAsync(ct)).Select(UserSummary.Of).ToArray()))
            .WithMetadata(RegistryOperations.DirectoryRead)
            .Produces<UserSummary[]>(StatusCodes.Status200OK);

        users.MapGet("/{subject}", async (string subject, IUserDirectory directory, CancellationToken ct) =>
                RegistryRules.IsSubject(subject)
                    ? Summary(await directory.GetAsync(subject, ct), UserSummary.Of)
                    : NotASubject(subject))
            .WithMetadata(RegistryOperations.DirectoryRead)
            .Produces<UserSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{subject}/deactivate", async (string subject, HttpContext http, IUserDirectory directory, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "deactivated user {Subject}", subject);
                return Summary(await directory.DeactivateAsync(subject, Callers.ActorOf(http), ct), UserSummary.Of);
            })
            .WithMetadata(RegistryOperations.DirectoryWrite)
            .Produces<UserSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{subject}/reactivate", async (string subject, HttpContext http, IUserDirectory directory, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "reactivated user {Subject}", subject);
                return Summary(await directory.ReactivateAsync(subject, Callers.ActorOf(http), ct), UserSummary.Of);
            })
            .WithMetadata(RegistryOperations.DirectoryWrite)
            .Produces<UserSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapCatalog(RouteGroupBuilder catalog)
    {
        catalog.MapGet("/", async (ICatalog service, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(ct)).Select(CatalogEntrySummary.Of).ToArray()))
            .WithMetadata(RegistryOperations.CatalogRead)
            .Produces<CatalogEntrySummary[]>(StatusCodes.Status200OK);

        catalog.MapGet("/{module}", async (string module, ICatalog service, CancellationToken ct) =>
                NameRules.IsModuleName(module)
                    ? Results.Ok((await service.ListAsync(ct)).Where(entry => entry.Module == module).Select(CatalogEntrySummary.Of).ToArray())
                    : Invalid($"'{module}' is not a module name."))
            .WithMetadata(RegistryOperations.CatalogRead)
            .Produces<CatalogEntrySummary[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    // The definition a body gives a role, or the answer that refuses it.
    private static bool TryDefine(string? category, string displayName, string[] operations, AssignableRolesBody? assignable, bool requiresSecondFactor, out RoleDefinition definition, out IResult invalid)
    {
        definition = null!;
        invalid = null!;
        if (!Statuses.TryParse(category, out RoleCategory parsed))
        {
            invalid = Invalid("A category is administration or business.");
            return false;
        }
        var roles = AssignableRoles.None;
        if (assignable is not null && !assignable.TryRead(out roles))
        {
            invalid = Invalid("An assignable list has a compartment selector — none, business or all —, a platform flag and an array of role names.");
            return false;
        }
        definition = new RoleDefinition(parsed, displayName, operations, roles, requiresSecondFactor);
        return true;
    }

    private static void MapRoles(RouteGroupBuilder roles)
    {
        roles.MapGet("/", async (IRoles service, CancellationToken ct, string? scope = null) =>
            {
                RoleScope? parsed = null;
                if (scope is not null)
                {
                    if (!Statuses.TryParseScope(scope, out var value))
                        return Invalid("A scope is compartment or platform.");
                    parsed = value;
                }
                return Results.Ok((await service.ListAsync(parsed, ct)).Select(RoleSummary.Of).ToArray());
            })
            .WithMetadata(RegistryOperations.RolesRead)
            .Produces<RoleSummary[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        roles.MapGet("/{role}", async (string role, IRoles service, CancellationToken ct) =>
                RegistryRules.IsRoleName(role)
                    ? Summary(await service.GetAsync(role, ct), RoleSummary.Of)
                    : NotARole(role))
            .WithMetadata(RegistryOperations.RolesRead)
            .Produces<RoleSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        roles.MapPost("/", async (CreateRoleRequest request, HttpContext http, IRoles service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (request is null || !RegistryRules.IsRoleName(request.Name) || string.IsNullOrWhiteSpace(request.DisplayName) || request.Operations is null)
                    return Invalid("A role has a name — an identifier —, a scope, a category, a display name and an array of operations.");
                if (!Statuses.TryParseScope(request.Scope, out var scope))
                    return Invalid("A scope is compartment or platform.");
                if (!TryDefine(request.Category, request.DisplayName, request.Operations, request.Assignable, request.RequiresSecondFactor, out var definition, out var invalid))
                    return invalid;
                var created = await service.CreateAsync(request.Name, scope, definition, Callers.ActorOf(http), ct);
                Written(http, loggers, "created role {Role}", request.Name);
                if (created.IsFailure)
                    return created.ToHttpResult();
                return created.Value.Pending is { } pending ? Pending(pending) : Results.Created((string?)null, RoleSummary.Of(created.Value.Value!));
            })
            .WithMetadata(RegistryOperations.RolesWrite)
            .Produces<RoleSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapPut("/{role}", async (string role, UpdateRoleRequest request, HttpContext http, IRoles service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                if (request is null || string.IsNullOrWhiteSpace(request.DisplayName) || request.Operations is null)
                    return Invalid("A role has a category, a display name and an array of operations.");
                if (!TryDefine(request.Category, request.DisplayName, request.Operations, request.Assignable, request.RequiresSecondFactor, out var definition, out var invalid))
                    return invalid;
                Written(http, loggers, "changed role {Role}", role);
                var updated = await service.UpdateAsync(role, definition, Callers.ActorOf(http), ct);
                if (updated.IsFailure)
                    return updated.ToHttpResult();
                return updated.Value.Pending is { } pending ? Pending(pending) : Results.Ok(RoleSummary.Of(updated.Value.Value!));
            })
            .WithMetadata(RegistryOperations.RolesWrite)
            .Produces<RoleSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        roles.MapDelete("/{role}", async (string role, HttpContext http, IRoles service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                Written(http, loggers, "deleted role {Role}", role);
                return (await service.DeleteAsync(role, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.RolesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    private static void MapMemberships(RouteGroupBuilder memberships)
    {
        memberships.MapGet("/compartments/{compartment}", async (string compartment, IMemberships service, CancellationToken ct) =>
                Compartment.TryParse(compartment, out var parsed)
                    ? Results.Ok((await service.ListByCompartmentAsync(parsed, ct)).Select(MemberSummary.Of).ToArray())
                    : NotACompartment(compartment))
            .WithMetadata(RegistryOperations.MembershipsRead)
            .Produces<MemberSummary[]>(StatusCodes.Status200OK);

        memberships.MapGet("/users/{subject}", async (string subject, IMemberships service, CancellationToken ct) =>
                RegistryRules.IsSubject(subject)
                    ? Results.Ok((await service.ListByUserAsync(subject, ct)).Select(MemberSummary.Of).ToArray())
                    : NotASubject(subject))
            .WithMetadata(RegistryOperations.MembershipsRead)
            .Produces<MemberSummary[]>(StatusCodes.Status200OK);

        memberships.MapPut("/{compartment}/{subject}", async (string compartment, string subject, HttpContext http, IMemberships service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "added member {Subject} to {Compartment}", subject, parsed);
                return (await service.AddAsync(subject, parsed, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.MembershipsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        memberships.MapDelete("/{compartment}/{subject}", async (string compartment, string subject, HttpContext http, IMemberships service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                Written(http, loggers, "removed member {Subject} from {Compartment}", subject, parsed);
                return (await service.RemoveAsync(subject, parsed, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.MembershipsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        memberships.MapPut("/{compartment}/{subject}/roles/{role}", async (string compartment, string subject, string role, HttpContext http, IMemberships service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                Written(http, loggers, "assigned role {Role} to {Subject} in {Compartment}", role, subject, parsed);
                return (await service.AssignRoleAsync(subject, parsed, role, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.MembershipsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        memberships.MapDelete("/{compartment}/{subject}/roles/{role}", async (string compartment, string subject, string role, HttpContext http, IMemberships service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!Compartment.TryParse(compartment, out var parsed))
                    return NotACompartment(compartment);
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                Written(http, loggers, "unassigned role {Role} from {Subject} in {Compartment}", role, subject, parsed);
                return (await service.UnassignRoleAsync(subject, parsed, role, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.MembershipsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapOperators(RouteGroupBuilder operators)
    {
        operators.MapGet("/", async (IOperators service, CancellationToken ct) =>
                Results.Ok((await service.ListAsync(ct)).Select(entry => new OperatorSummary(entry.Subject, entry.Roles.ToArray())).ToArray()))
            .WithMetadata(RegistryOperations.OperatorsRead)
            .Produces<OperatorSummary[]>(StatusCodes.Status200OK);

        operators.MapGet("/{subject}", async (string subject, IOperators service, CancellationToken ct) =>
                RegistryRules.IsSubject(subject)
                    ? Results.Ok(new OperatorSummary(RegistryRules.NormalizeSubject(subject), (await service.ListRolesAsync(subject, ct)).ToArray()))
                    : NotASubject(subject))
            .WithMetadata(RegistryOperations.OperatorsRead)
            .Produces<OperatorSummary>(StatusCodes.Status200OK);

        operators.MapPut("/{subject}/roles/{role}", async (string subject, string role, HttpContext http, IOperators service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                Written(http, loggers, "assigned platform role {Role} to {Subject}", role, subject);
                var assigned = await service.AssignAsync(subject, role, Callers.ActorOf(http), ct);
                if (assigned.IsFailure)
                    return assigned.ToHttpResult();
                return assigned.Value is { } pending ? Pending(pending) : Results.NoContent();
            })
            .WithMetadata(RegistryOperations.OperatorsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<PendingActSummary>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        operators.MapDelete("/{subject}/roles/{role}", async (string subject, string role, HttpContext http, IOperators service, ILoggerFactory loggers, CancellationToken ct) =>
            {
                if (!RegistryRules.IsSubject(subject))
                    return NotASubject(subject);
                if (!RegistryRules.IsRoleName(role))
                    return NotARole(role);
                Written(http, loggers, "unassigned platform role {Role} from {Subject}", role, subject);
                return (await service.UnassignAsync(subject, role, Callers.ActorOf(http), ct)).ToHttpResult();
            })
            .WithMetadata(RegistryOperations.OperatorsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult Summary<TRecord, TSummary>(Result<TRecord> result, Func<TRecord, TSummary> summary)
        => result.IsSuccess ? Results.Ok(summary(result.Value)) : result.ToHttpResult();

    private static IResult Invalid(string message) => Result.Failure(Error.Validation(InvalidCode, message)).ToHttpResult();

    private static IResult NotACompartment(string value) => Invalid($"'{value}' is not a compartment.");

    private static IResult NotASubject(string value) => Invalid($"'{value}' is not the subject of a person: a GUID.");

    private static IResult NotARole(string value) => Invalid($"'{value}' is not a role name: {NameRules.IdentifierRule}.");

    /// <summary>The trace of a write in the host's journal, with who did it; the row keeps the same actor.</summary>
    private static void Written(HttpContext http, ILoggerFactory loggers, string what, params object[] arguments)
    {
        var logger = loggers.CreateLogger("Stellaegis.Registry.Administration");
        if (!logger.IsEnabled(LogLevel.Information))
            return;
        var actor = Callers.ActorOf(http);
#pragma warning disable CA2254 // the template is composed from constants of this file
        logger.LogInformation("{Actor} " + what, [actor, .. arguments]);
#pragma warning restore CA2254
    }
}
