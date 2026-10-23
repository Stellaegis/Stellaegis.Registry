using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Stellaegis.Access;
using Stellaegis.Identity;
using Stellaegis.Identity.AspNetCore;
using Stellaegis.Issuer;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Registry.Sample;

/// <summary>The frame of the example: one compartment, the company, and the shared space the platform is administered from.</summary>
public static class SampleFrame
{
    public static readonly Compartment Company = Compartment.Parse("acme");

    public static readonly ApplicationFrame Value = ApplicationFrame.SingleCompartment(Company).WithSharedSpace();
}

/// <summary>The policy scheme of the example, which sends a bearer token to the bearer and everything else to the cookie.</summary>
public static class SampleAuthentication
{
    public const string Scheme = "Sample";
}

// ── The application's own module ─────────────────────────────────────────────

/// <summary>
/// The module of the example, compartmented: an inventory of items, held per compartment. It declares its
/// operations, labels its routes with them, and decides nothing — Access does, with the rights the registry gives.
/// </summary>
public sealed class InventoryModule : IStellaegisModule
{
    public static string Name => "inventory";

    public static ModuleDeclaration Declaration { get; } = ModuleDeclaration.Compartmented<InventoryModule>()
        .WithOperations(InventoryOperations.All);
}

/// <summary>The operations of the inventory: what the registry puts in its catalogue, what a role groups, what Access grants.</summary>
public static class InventoryOperations
{
    public static readonly Operation Read = new("items", "read", OperationKind.Read);
    public static readonly Operation Write = new("items", "write", OperationKind.Write);
    public static readonly Operation Purge = new("items", "purge", OperationKind.Delete);
    public static IReadOnlyList<Operation> All { get; } = [Read, Write, Purge];
}

/// <summary>
/// The roles the application declares in code, which the installation records and re-applies: three compartment
/// roles for the people of the company, one platform role for whoever administers the application. A role is a
/// name and a set of operations of the catalogue; the platform role takes every operation every module declares.
/// </summary>
public static class SampleRoles
{
    public const string TenantAdministrator = "tenant_administrator";
    public const string ShopManager = "shop_manager";
    public const string Operator = "operator";
    public const string SuperAdmin = "super_admin";

    public static IReadOnlyList<RoleDeclaration> Declarations(IEnumerable<ModuleDeclaration> modules) =>
    [
        new(TenantAdministrator, RoleScope.Compartment, RoleCategory.Administration, "Administrateur du compartiment",
            [InventoryOperations.Read, InventoryOperations.Write], AssignableRoles.Every(CompartmentRoleSelector.Business)),
        new(ShopManager, RoleScope.Compartment, RoleCategory.Business, "Chef d'atelier",
            [InventoryOperations.Read, InventoryOperations.Write], AssignableRoles.Of(Operator)),
        new(Operator, RoleScope.Compartment, RoleCategory.Business, "Opérateur", [InventoryOperations.Read]),
        new(SuperAdmin, RoleScope.Platform, RoleCategory.Administration, "Administrateur de la plateforme",
            modules.SelectMany(module => module.Operations).DistinctBy(operation => operation.Identity, StringComparer.Ordinal).ToList(),
            AssignableRoles.Every(CompartmentRoleSelector.All, allPlatform: true)),
    ];
}

/// <summary>The items, in memory, per compartment: enough for the decisions to be seen.</summary>
public sealed class Inventory
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Item>> _items = new();

    public IReadOnlyList<Item> List(Compartment compartment)
        => _items.TryGetValue(compartment.Value, out var items) ? [.. items] : [];

    public Item Add(Compartment compartment, string name, Actor by)
    {
        var item = new Item(Guid.NewGuid().ToString("N"), name, by.Value, by.Via?.Value);
        _items.GetOrAdd(compartment.Value, _ => new ConcurrentQueue<Item>()).Enqueue(item);
        return item;
    }

    public int Purge(Compartment compartment)
        => _items.TryRemove(compartment.Value, out var items) ? items.Count : 0;

    public sealed record Item(string Id, string Name, string AddedBy, string? Via);
}

/// <summary>What a request adds to the inventory.</summary>
public sealed record NewItem(string Name);

/// <summary>The routes of the inventory, each under its operation.</summary>
public static class InventoryEndpoints
{
    /// <summary>The routes in the compartment: read and write, decided for the actor of the request in its space.</summary>
    public static RouteGroupBuilder MapInventoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/items")
            .BelongsTo<InventoryModule>()
            .RequireRequestContext()
            .LoadsNoResource();

        group.MapGet("/", (IRequestContext context, Inventory inventory) =>
                context.Space?.Compartment is { } compartment
                    ? Results.Ok(new
                    {
                        actor = context.Actor.Value,
                        via = context.Actor.Via?.Value,
                        space = context.Space.ToSegment(),
                        items = inventory.List(compartment),
                    })
                    : Results.NotFound())
            .WithMetadata(InventoryOperations.Read);

        group.MapPost("/", (NewItem item, IRequestContext context, Inventory inventory) =>
                context.Space?.Compartment is { } compartment && !string.IsNullOrWhiteSpace(item?.Name)
                    ? Results.Created($"/api/items/{compartment.Value}", inventory.Add(compartment, item.Name, context.Actor))
                    : Results.BadRequest())
            .WithMetadata(InventoryOperations.Write);

        return group;
    }

    /// <summary>
    /// The administration route, which acts on a compartment named in its path: purging it. The compartment is the
    /// object of the route, not the space of the decision — Access decides it in the shared space, for the operators
    /// of the platform; a member of the compartment holding <c>items.purge</c> there does not purge it.
    /// </summary>
    public static RouteGroupBuilder MapInventoryAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/inventory")
            .BelongsTo<InventoryModule>()
            .RequireRequestContext()
            .ActsOnSpaces()
            .LoadsNoResource();

        group.MapDelete("/compartments/{compartment}", (Compartment compartment, IRequestContext context, Inventory inventory) =>
                Results.Ok(new { purged = inventory.Purge(compartment), compartment = compartment.Value, actor = context.Actor.Value }))
            .WithMetadata(InventoryOperations.Purge);

        return group;
    }
}

// ── What the host brings to compose the four packages ────────────────────────

/// <summary>Reads the actor of a request from the claims of the token this host accepts.</summary>
public static class Callers
{
    /// <summary>
    /// A person by <c>sub</c>, or a client by <c>client_id</c>: the default source of Primitives reads <c>sub</c> and
    /// <c>NameIdentifier</c> only, and would fail the request of a client on a compartmented route.
    /// </summary>
    public static Actor? PersonOrClient(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true)
            return null;
        var value = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http.User.FindFirst("client_id")?.Value;
        return value is null ? null : Actor.Parse(value);
    }
}

/// <summary>
/// Where the effective rights the registry computes go: the projection of Access, in the same process. One
/// class, one line per method — the two ports have the same shape on purpose. A host whose decision point runs
/// apart implements the same five methods on the projection routes of Access.
/// </summary>
public sealed class AccessRightsSink(IAccessProjection projection) : IRightsSink
{
    public Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken cancellationToken = default)
        => projection.ReplaceMembershipsAsync(subject, compartments, cancellationToken);

    public Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
        => projection.ReplaceUserGrantsAsync(subject, compartment, operations, cancellationToken);

    public Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
        => projection.ReplaceAdminGrantsAsync(subject, operations, cancellationToken);

    public Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => projection.RemoveCompartmentAsync(compartment, cancellationToken);

    public Task RemoveSubjectAsync(string subject, CancellationToken cancellationToken = default)
        => projection.RemoveSubjectAsync(AccessSubject.Person(subject), cancellationToken);
}

/// <summary>
/// The mailer of the example: what the pages ask to send goes to the log, where a developer reads the code or
/// the link. A real host implements <see cref="IIdentityMailer"/> on its messaging, with its own wording.
/// </summary>
public sealed class LoggingMailer(ILogger<LoggingMailer> logger) : IIdentityMailer
{
    public Task SendVerificationCodeAsync(string email, string code, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("MAIL to {Email}: your verification code is {Code}, valid for {Lifetime}", email, code, lifetime);
        return Task.CompletedTask;
    }

    public Task SendLoginCodeAsync(string email, string code, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("MAIL to {Email}: your login code is {Code}, valid for {Lifetime}", email, code, lifetime);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(string email, Uri link, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("MAIL to {Email}: reset your password at {Link}, valid for {Lifetime}", email, link, lifetime);
        return Task.CompletedTask;
    }

    public Task SendAccountAlreadyExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("MAIL to {Email}: someone tried to register this address, which has an account already", email);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The bus of the example: a single process, so an event is handed to its subscribers straight away. This is where
/// the host wires the four packages together, as the doctrine asks of a host — a module subscribes to nothing, it
/// offers explicit operations the host calls:
/// <list type="bullet">
///   <item>an account registered in Identity is recorded in the registry's directory, by the person themselves;</item>
///   <item>an account blocked is deactivated in the registry — which withdraws its rights from Access through the
///   sink —, refused by the admission at once, and every token the issuer gave it is revoked;</item>
///   <item>an account unblocked is reactivated and admitted again.</item>
/// </list>
/// A host with several processes registers the bus of <c>Stellaegis.Backplane</c> instead, and subscribes the same way.
/// </summary>
public sealed class SampleEventPublisher(BlockedAccounts accounts, IServiceScopeFactory scopes, ILogger<SampleEventPublisher> logger) : IEventPublisher
{
    /// <summary>The actor the host records the facts of Identity under, in the registry.</summary>
    private static readonly Actor Identity = Actor.Job("identity");

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        switch (@event)
        {
            case UserRegisteredEvent registered:
                using (var scope = scopes.CreateScope())
                {
                    var subject = registered.UserUniqueId.ToString("N");
                    var recorded = await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
                        .RecordAsync(subject, registered.Email, registered.DisplayName, Actor.Parse(subject), cancellationToken);
                    logger.LogInformation("Account {Subject} registered: recorded in the directory ({Outcome})", subject, recorded.IsSuccess ? "ok" : recorded.Error);
                }
                break;
            case UserBlockedEvent blocked:
                accounts.Block(blocked.UserUniqueId);
                using (var scope = scopes.CreateScope())
                {
                    var subject = blocked.UserUniqueId.ToString("N");
                    await scope.ServiceProvider.GetRequiredService<IUserDirectory>().DeactivateAsync(subject, Identity, cancellationToken);
                    var revoked = await scope.ServiceProvider.GetRequiredService<ITokenRevoker>().RevokeSubjectAsync(subject, cancellationToken);
                    logger.LogInformation("Account {Subject} blocked: deactivated in the directory, {Count} token(s) revoked", subject, revoked);
                }
                break;
            case UserUnblockedEvent unblocked:
                accounts.Unblock(unblocked.UserUniqueId);
                using (var scope = scopes.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<IUserDirectory>().ReactivateAsync(unblocked.UserUniqueId.ToString("N"), Identity, cancellationToken);
                break;
        }
        logger.LogInformation("Event {Event}", @event);
    }
}

/// <summary>
/// The scheme a request is authenticated by: the bearer for a bearer token and for every call of the API — so that
/// an API call without a token is answered 401, not sent to the login page —, the cookie for everything else, a
/// person on the pages or the browser of the native flow on <c>/connect/authorize</c>.
/// </summary>
public static class SampleSchemes
{
    public static string Select(HttpContext context)
        => context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api")
            ? JwtBearerDefaults.AuthenticationScheme
            : IdentityCookie.Scheme;
}
