using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>
/// The catalogue of what can be granted: the operations the modules declare. The installation records it from
/// <c>GetStellaegisModules()</c>; nothing is seeded by hand.
/// </summary>
public interface ICatalog
{
    /// <summary>
    /// Records the operations of the modules: adds the new ones, updates the kind and the module of the known ones,
    /// removes nothing — an operation a module no longer declares stays grantable until someone decides otherwise.
    /// </summary>
    Task<IReadOnlyList<CatalogEntry>> RecordAsync(IEnumerable<ModuleDeclaration> modules, CancellationToken cancellationToken = default);

    /// <summary>Every entry, by module then operation.</summary>
    Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The roles: a name, a category and a set of operations of the catalogue, in one scope, with the roles each lets its
/// holder assign and whether it requires a second factor. Every write names the actor who makes it, which the row
/// keeps and the event carries. Nobody changes the definition of a role they hold
/// (<see cref="RegistryErrors.RoleHeldByAuthor"/>); a platform role always requires a second factor.
/// </summary>
public interface IRoles
{
    /// <summary>
    /// Records the roles the application declares: creates each when missing, resets its definition when present and
    /// declared. A role of the same name created by hand is refused (<see cref="RegistryErrors.RoleNotDeclared"/>).
    /// The declarations may name one another in their assignable lists, in any order. Every holder of a changed role
    /// has their rights recomputed.
    /// </summary>
    Task<Result> DeclareAsync(IEnumerable<RoleDeclaration> declarations, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Creates a role by hand.</summary>
    Task<Result<Role>> CreateAsync(string name, RoleScope scope, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Changes the definition of a role; every holder has their rights recomputed. The installation resets a declared role.</summary>
    Task<Result<Role>> UpdateAsync(string name, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Deletes a role created by hand that nobody holds and no assignable list names.</summary>
    Task<Result> DeleteAsync(string name, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The role, or a failure.</summary>
    Task<Result<Role>> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Every role, or those of one scope.</summary>
    Task<IReadOnlyList<Role>> ListAsync(RoleScope? scope = null, CancellationToken cancellationToken = default);
}

/// <summary>The compartments. A compartment is never deleted: terminated, its name stays taken. Every write names its actor.</summary>
public interface ICompartments
{
    /// <summary>Creates the compartment, active.</summary>
    Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Renames the compartment.</summary>
    Task<Result<CompartmentRecord>> RenameAsync(Compartment compartment, string displayName, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Suspends the compartment: its members keep their roles in the registry and hold no right until it resumes.</summary>
    Task<Result<CompartmentRecord>> SuspendAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Resumes the compartment: its members hold their rights again.</summary>
    Task<Result<CompartmentRecord>> ResumeAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Terminates the compartment: its memberships are gone, from the registry and from the decision point, and its name stays taken.</summary>
    Task<Result<CompartmentRecord>> TerminateAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The compartment, or a failure.</summary>
    Task<Result<CompartmentRecord>> GetAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Every compartment, terminated ones included.</summary>
    Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The users, projected from Identity: the host calls <see cref="RecordAsync"/> from its subscription to the
/// registration — the actor being the person themselves —, <see cref="DeactivateAsync"/> from the blocking.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Records the user, active, or updates their e-mail and display name.</summary>
    Task<Result<UserRecord>> RecordAsync(string subject, string email, string displayName, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Deactivates the user: they keep their memberships and roles in the registry and hold no right anywhere.</summary>
    Task<Result<UserRecord>> DeactivateAsync(string subject, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Reactivates the user: they hold their rights again.</summary>
    Task<Result<UserRecord>> ReactivateAsync(string subject, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The user, or a failure.</summary>
    Task<Result<UserRecord>> GetAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Every user.</summary>
    Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The memberships, and the compartment roles of the members. Every write names its actor, which the grant keeps.</summary>
public interface IMemberships
{
    /// <summary>Makes the user a member of the compartment, without any role yet. Nobody adds themselves (<see cref="RegistryErrors.OwnRoles"/>).</summary>
    Task<Result> AddAsync(string subject, Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the user from the compartment, with their roles there: the actor may assign each of them
    /// (<see cref="RegistryErrors.RoleNotAssignable"/>), and nobody removes themselves (<see cref="RegistryErrors.OwnRoles"/>).
    /// </summary>
    Task<Result> RemoveAsync(string subject, Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a compartment role to a member: the role is in the assignable list of a role the actor holds in the
    /// compartment or on the platform (<see cref="RegistryErrors.RoleNotAssignable"/>), and the member is not the actor
    /// (<see cref="RegistryErrors.OwnRoles"/>). A job — the installation — is bounded by neither.
    /// </summary>
    Task<Result> AssignRoleAsync(string subject, Compartment compartment, string role, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Unassigns a compartment role from a member, under the same rules as its assignment.</summary>
    Task<Result> UnassignRoleAsync(string subject, Compartment compartment, string role, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The memberships of the user, with their roles.</summary>
    Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The members of the compartment, with their roles.</summary>
    Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);
}

/// <summary>The operators of the platform: the platform roles of the users. Every write names its actor, which the grant keeps.</summary>
public interface IOperators
{
    /// <summary>
    /// Assigns a platform role to a user: the role is in the assignable list of a platform role the actor holds
    /// (<see cref="RegistryErrors.RoleNotAssignable"/>), and the user is not the actor (<see cref="RegistryErrors.OwnRoles"/>).
    /// A job — the installation — is bounded by neither.
    /// </summary>
    Task<Result> AssignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Unassigns a platform role from a user, under the same rules as its assignment.</summary>
    Task<Result> UnassignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The platform roles of the user.</summary>
    Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Every operator with their roles.</summary>
    Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken = default);
}

internal sealed class Catalog(ICatalogStore store) : ICatalog
{
    public async Task<IReadOnlyList<CatalogEntry>> RecordAsync(IEnumerable<ModuleDeclaration> modules, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(modules);
        var entries = modules
            .SelectMany(module => module.Operations.Select(operation => new CatalogEntry(module.Name, operation.Identity, operation.Kind)))
            .GroupBy(entry => entry.Operation, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        await store.SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        return entries;
    }

    public Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);
}

internal sealed class Roles(
    IRoleStore store,
    ICatalogStore catalog,
    IMembershipStore memberships,
    IOperatorStore operators,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : IRoles
{
    public async Task<Result> DeclareAsync(IEnumerable<RoleDeclaration> declarations, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(declarations);
        Guard.NotNull(actor);
        var batch = declarations.ToList();
        if (batch.GroupBy(declaration => declaration.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } twice)
            throw new ArgumentException($"Role '{twice.Key}' is declared twice.", nameof(declarations));
        var declaring = batch.ToDictionary(declaration => declaration.Name, declaration => declaration.Scope, StringComparer.Ordinal);

        // Two passes: a declaration may name in its assignable list a role declared after it, which the store
        // refuses until that role exists. The first pass writes each role with the names that exist already; the
        // second, the full lists.
        var written = new List<(Role Role, Role? Before)>();
        foreach (var declaration in batch)
        {
            if (!RegistryRules.IsRoleName(declaration.Name))
                return Result.Failure(RegistryErrors.RoleNameInvalid(declaration.Name));
            var definition = Normalize(declaration.Scope, declaration.Definition);
            var invalid = await ValidateAsync(declaration.Name, declaration.Scope, definition, declaring, cancellationToken).ConfigureAwait(false);
            if (invalid is not null)
                return Result.Failure(invalid);

            var existing = await store.GetAsync(declaration.Name, cancellationToken).ConfigureAwait(false);
            if (existing is { Declared: false })
                return Result.Failure(RegistryErrors.RoleNotDeclared(declaration.Name));
            if (existing is not null && existing.Scope == declaration.Scope && Same(existing, definition))
                continue;
            if (existing is not null && await HeldByAsync(existing, actor, cancellationToken).ConfigureAwait(false))
                return Result.Failure(RegistryErrors.RoleHeldByAuthor(declaration.Name));

            var now = time.GetUtcNow();
            var role = Build(declaration.Name, declaration.Scope, definition, declared: true,
                existing is null ? Audit.CreatedBy(actor, now) : existing.Audit.ModifiedBy(actor, now));
            var known = (await store.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
            known.Add(role.Name);
            await store.SaveAsync(role with { Assignable = role.Assignable with { Roles = role.Assignable.Roles.Where(known.Contains).ToList() } }, cancellationToken).ConfigureAwait(false);
            written.Add((role, existing));
        }

        foreach (var (role, before) in written)
        {
            if (role.Assignable.Roles.Count > 0)
                await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
            await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = role.Audit.Modified.At, Role = role.Name, Exists = true, By = actor }, cancellationToken).ConfigureAwait(false);
            if (before is not null)
                await projector.ProjectRoleHoldersAsync(role, cancellationToken).ConfigureAwait(false);
        }
        return Result.Success();
    }

    public async Task<Result<Role>> CreateAsync(string name, RoleScope scope, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition);
        Guard.NotNull(actor);
        if (!RegistryRules.IsRoleName(name))
            return Result.Failure<Role>(RegistryErrors.RoleNameInvalid(name));
        if (await store.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null)
            return Result.Failure<Role>(RegistryErrors.RoleExists(name));
        definition = Normalize(scope, definition);
        var invalid = await ValidateAsync(name, scope, definition, null, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
            return Result.Failure<Role>(invalid);

        var now = time.GetUtcNow();
        var role = Build(name, scope, definition, declared: false, Audit.CreatedBy(actor, now));
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = now, Role = name, Exists = true, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success(role);
    }

    public async Task<Result<Role>> UpdateAsync(string name, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition);
        Guard.NotNull(actor);
        var existing = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<Role>(RegistryErrors.RoleNotFound(name));
        if (await HeldByAsync(existing, actor, cancellationToken).ConfigureAwait(false))
            return Result.Failure<Role>(RegistryErrors.RoleHeldByAuthor(name));
        definition = Normalize(existing.Scope, definition);
        var invalid = await ValidateAsync(name, existing.Scope, definition, null, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
            return Result.Failure<Role>(invalid);

        var now = time.GetUtcNow();
        var role = Build(name, existing.Scope, definition, existing.Declared, existing.Audit.ModifiedBy(actor, now));
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = now, Role = name, Exists = true, By = actor }, cancellationToken).ConfigureAwait(false);
        await projector.ProjectRoleHoldersAsync(role, cancellationToken).ConfigureAwait(false);
        return Result.Success(role);
    }

    public async Task<Result> DeleteAsync(string name, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var existing = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure(RegistryErrors.RoleNotFound(name));
        if (existing.Declared)
            return Result.Failure(RegistryErrors.RoleDeclared(name));
        var held = existing.Scope == RoleScope.Compartment
            ? (await memberships.ListByRoleAsync(name, cancellationToken).ConfigureAwait(false)).Count > 0
            : (await operators.ListByRoleAsync(name, cancellationToken).ConfigureAwait(false)).Count > 0;
        if (held)
            return Result.Failure(RegistryErrors.RoleInUse(name));
        var naming = (await store.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(role => role.Name != name && role.Assignable.Roles.Contains(name, StringComparer.Ordinal));
        if (naming is not null)
            return Result.Failure(RegistryErrors.RoleNamedAssignable(name, naming.Name));

        await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = time.GetUtcNow(), Role = name, Exists = false, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<Role>> GetAsync(string name, CancellationToken cancellationToken = default)
        => await store.GetAsync(Guard.NotWhiteSpace(name), cancellationToken).ConfigureAwait(false) is { } role
            ? Result.Success(role)
            : Result.Failure<Role>(RegistryErrors.RoleNotFound(name));

    public Task<IReadOnlyList<Role>> ListAsync(RoleScope? scope = null, CancellationToken cancellationToken = default) => store.ListAsync(scope, cancellationToken);

    private async Task<string?> UnknownAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
            return null;
        var known = (await catalog.ListAsync(cancellationToken).ConfigureAwait(false)).Select(entry => entry.Operation).ToHashSet(StringComparer.Ordinal);
        return operations.FirstOrDefault(operation => !known.Contains(operation));
    }

    // A platform role always requires a second factor (Fred, 2026-10-23): whoever holds the platform holds every
    // compartment; the flag is set rather than refused, so that a declaration need not repeat it.
    private static RoleDefinition Normalize(RoleScope scope, RoleDefinition definition) => definition with
    {
        DisplayName = Guard.NotWhiteSpace(definition.DisplayName),
        Operations = Guard.NotNull(definition.Operations).Distinct(StringComparer.Ordinal).ToList(),
        Assignable = definition.Assignable is { } assignable
            ? assignable with { Roles = Guard.NotNull(assignable.Roles).Distinct(StringComparer.Ordinal).ToList() }
            : AssignableRoles.None,
        RequiresSecondFactor = definition.RequiresSecondFactor || scope == RoleScope.Platform,
    };

    private static Role Build(string name, RoleScope scope, RoleDefinition definition, bool declared, Audit audit)
        => new(name, scope, definition.Category, definition.DisplayName, declared, definition.Operations, definition.Assignable!, definition.RequiresSecondFactor, audit);

    private static bool Same(Role role, RoleDefinition definition)
        => role.Category == definition.Category
           && role.DisplayName == definition.DisplayName
           && role.RequiresSecondFactor == definition.RequiresSecondFactor
           && role.Operations.ToHashSet(StringComparer.Ordinal).SetEquals(definition.Operations)
           && role.Assignable.SameAs(definition.Assignable!);

    // The operations exist; a compartment role lets its holder assign compartment roles only, by selector or by
    // name; every role named exists — in the store, or among the roles declared in the same batch.
    private async Task<Error?> ValidateAsync(string name, RoleScope scope, RoleDefinition definition, IReadOnlyDictionary<string, RoleScope>? declaring, CancellationToken cancellationToken)
    {
        var unknown = await UnknownAsync(definition.Operations, cancellationToken).ConfigureAwait(false);
        if (unknown is not null)
            return RegistryErrors.UnknownOperation(unknown);
        var assignable = definition.Assignable!;
        if (scope == RoleScope.Compartment && assignable.AllPlatform)
            return RegistryErrors.RoleAssignsPlatform(name);
        foreach (var named in assignable.Roles)
        {
            RoleScope namedScope;
            if (named == name)
                namedScope = scope;
            else if (declaring is not null && declaring.TryGetValue(named, out var declared))
                namedScope = declared;
            else if (await store.GetAsync(named, cancellationToken).ConfigureAwait(false) is { } existing)
                namedScope = existing.Scope;
            else
                return RegistryErrors.RoleNotFound(named);
            if (scope == RoleScope.Compartment && namedScope == RoleScope.Platform)
                return RegistryErrors.RoleAssignsPlatform(name);
        }
        return null;
    }

    // Whether the actor is a person who holds the role: on the platform for a platform role, in any compartment for a
    // compartment role. A job — the installation — holds nothing.
    private async Task<bool> HeldByAsync(Role role, Actor actor, CancellationToken cancellationToken)
    {
        if (!RegistryRules.IsSubject(actor.Value))
            return false;
        var subject = RegistryRules.NormalizeSubject(actor.Value);
        return role.Scope == RoleScope.Platform
            ? (await operators.ListRolesAsync(subject, cancellationToken).ConfigureAwait(false)).Contains(role.Name, StringComparer.Ordinal)
            : (await memberships.ListByUserAsync(subject, cancellationToken).ConfigureAwait(false)).Any(membership => membership.Roles.Contains(role.Name, StringComparer.Ordinal));
    }
}

internal sealed class Compartments(
    ICompartmentStore store,
    IMembershipStore memberships,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : ICompartments
{
    public async Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        if (await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false) is not null)
            return Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentExists(compartment));
        var now = time.GetUtcNow();
        var record = new CompartmentRecord(compartment, Guard.NotWhiteSpace(displayName), CompartmentStatus.Active, Audit.CreatedBy(actor, now));
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> RenameAsync(Compartment compartment, string displayName, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        var record = current.Value with { DisplayName = Guard.NotWhiteSpace(displayName), Audit = current.Value.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> SuspendAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        if (current.Value.Status == CompartmentStatus.Suspended)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Suspended, Audit = current.Value.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> ResumeAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        if (current.Value.Status == CompartmentStatus.Active)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Active, Audit = current.Value.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.ProjectCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> TerminateAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Terminated, Audit = current.Value.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await memberships.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> GetAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => await store.GetAsync(Guard.NotNull(compartment), cancellationToken).ConfigureAwait(false) is { } record
            ? Result.Success(record)
            : Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentNotFound(compartment));

    public Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);

    private Task PublishAsync(CompartmentRecord record, Actor actor, CancellationToken cancellationToken)
        => publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = record.Compartment, Status = record.Status, By = actor }, cancellationToken);

    /// <summary>The compartment when it exists and is not terminated.</summary>
    private async Task<Result<CompartmentRecord>> LiveAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        var record = await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Status == CompartmentStatus.Terminated)
            return Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentTerminated(compartment));
        return Result.Success(record);
    }
}

internal sealed class UserDirectory(
    IUserStore store,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : IUserDirectory
{
    public async Task<Result<UserRecord>> RecordAsync(string subject, string email, string displayName, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotWhiteSpace(email);
        Guard.NotWhiteSpace(displayName);
        Guard.NotNull(actor);
        var now = time.GetUtcNow();
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        var record = existing is null
            ? new UserRecord(subject, email, displayName, UserStatus.Active, Audit.CreatedBy(actor, now))
            : existing with { Email = email, DisplayName = displayName, Audit = existing.Audit.ModifiedBy(actor, now) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<UserRecord>> DeactivateAsync(string subject, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<UserRecord>(RegistryErrors.UserNotFound(subject));
        if (existing.Status == UserStatus.Deactivated)
            return Result.Success(existing);
        var record = existing with { Status = UserStatus.Deactivated, Audit = existing.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveSubjectAsync(subject, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<UserRecord>> ReactivateAsync(string subject, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<UserRecord>(RegistryErrors.UserNotFound(subject));
        if (existing.Status == UserStatus.Active)
            return Result.Success(existing);
        var record = existing with { Status = UserStatus.Active, Audit = existing.Audit.ModifiedBy(actor, time.GetUtcNow()) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.ProjectSubjectAsync(subject, cancellationToken).ConfigureAwait(false);
        await PublishAsync(record, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<UserRecord>> GetAsync(string subject, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        return await store.GetAsync(subject, cancellationToken).ConfigureAwait(false) is { } record
            ? Result.Success(record)
            : Result.Failure<UserRecord>(RegistryErrors.UserNotFound(subject));
    }

    public Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);

    private Task PublishAsync(UserRecord record, Actor actor, CancellationToken cancellationToken)
        => publisher.PublishAsync(new UserChangedEvent { OccurredAt = time.GetUtcNow(), Subject = record.Subject, Status = record.Status, By = actor }, cancellationToken);
}

internal sealed class Memberships(
    IMembershipStore store,
    IUserStore users,
    ICompartmentStore compartments,
    IRoleStore roles,
    AssignmentRules rules,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : IMemberships
{
    public async Task<Result> AddAsync(string subject, Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var check = await CheckAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
            return check;
        if (AssignmentRules.Own(subject, actor) is { } own)
            return Result.Failure(own);
        if (await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Success();
        await store.AddAsync(subject, compartment, new Stamp(time.GetUtcNow(), actor), cancellationToken).ConfigureAwait(false);
        await projector.ProjectMembershipsAsync(subject, cancellationToken).ConfigureAwait(false);
        await PublishAsync(subject, compartment, isMember: true, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(string subject, Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        if (!await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Success();
        // Removing a member removes their roles: the actor may assign each of them, or the removal would strip a role
        // beyond the actor's list — the manager of the accesses removing whoever made them so.
        var held = (await store.ListByUserAsync(subject, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(membership => membership.Compartment == compartment)?.Roles ?? [];
        var refused = await rules.CheckAsync(actor, subject, compartment, held, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure(refused);
        await store.RemoveAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await projector.ProjectMembershipsAsync(subject, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(subject, compartment, isMember: false, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> AssignRoleAsync(string subject, Compartment compartment, string role, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var check = await CheckAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
            return check;
        var record = await roles.GetAsync(Guard.NotWhiteSpace(role), cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.RoleNotFound(role));
        if (record.Scope != RoleScope.Compartment)
            return Result.Failure(RegistryErrors.RoleScopeMismatch(role, RoleScope.Compartment));
        if (!await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.NotMember(subject, compartment));
        var refused = await rules.CheckAsync(actor, subject, compartment, [role], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure(refused);

        await store.AssignRoleAsync(subject, compartment, role, new Stamp(time.GetUtcNow(), actor), cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(subject, compartment, isMember: true, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UnassignRoleAsync(string subject, Compartment compartment, string role, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(compartment);
        Guard.NotWhiteSpace(role);
        Guard.NotNull(actor);
        if (!await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.NotMember(subject, compartment));
        var refused = await rules.CheckAsync(actor, subject, compartment, [role], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure(refused);
        await store.UnassignRoleAsync(subject, compartment, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await PublishAsync(subject, compartment, isMember: true, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default)
        => store.ListByUserAsync(RegistryRules.NormalizeSubject(subject), cancellationToken);

    public Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => store.ListByCompartmentAsync(Guard.NotNull(compartment), cancellationToken);

    private Task PublishAsync(string subject, Compartment compartment, bool isMember, Actor actor, CancellationToken cancellationToken)
        => publisher.PublishAsync(new MembershipChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment, IsMember = isMember, By = actor }, cancellationToken);

    /// <summary>The user exists and is active, the compartment exists and is not terminated.</summary>
    private async Task<Result> CheckAsync(string subject, Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        var user = await users.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result.Failure(RegistryErrors.UserNotFound(subject));
        if (user.Status == UserStatus.Deactivated)
            return Result.Failure(RegistryErrors.UserDeactivated(subject));
        var record = await compartments.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Status == CompartmentStatus.Terminated)
            return Result.Failure(RegistryErrors.CompartmentTerminated(compartment));
        return Result.Success();
    }
}

internal sealed class Operators(
    IOperatorStore store,
    IUserStore users,
    IRoleStore roles,
    AssignmentRules rules,
    RightsProjector projector,
    TimeProvider time) : IOperators
{
    public async Task<Result> AssignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var user = await users.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result.Failure(RegistryErrors.UserNotFound(subject));
        if (user.Status == UserStatus.Deactivated)
            return Result.Failure(RegistryErrors.UserDeactivated(subject));
        var record = await roles.GetAsync(Guard.NotWhiteSpace(role), cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.RoleNotFound(role));
        if (record.Scope != RoleScope.Platform)
            return Result.Failure(RegistryErrors.RoleScopeMismatch(role, RoleScope.Platform));
        var refused = await rules.CheckAsync(actor, subject, compartment: null, [role], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure(refused);

        await store.AssignAsync(subject, role, new Stamp(time.GetUtcNow(), actor), cancellationToken).ConfigureAwait(false);
        await projector.ProjectAdminGrantsAsync(subject, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UnassignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotWhiteSpace(role);
        Guard.NotNull(actor);
        var refused = await rules.CheckAsync(actor, subject, compartment: null, [role], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure(refused);
        await store.UnassignAsync(subject, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectAdminGrantsAsync(subject, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken = default)
        => store.ListRolesAsync(RegistryRules.NormalizeSubject(subject), cancellationToken);

    public Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken = default)
        => store.ListAsync(cancellationToken);
}

/// <summary>
/// Who may assign or unassign which role (I3, I7). Assigning is a right of its own, bounded by the assignable lists
/// of the roles the actor holds, and does not suppose holding the role assigned: in a compartment, those of the
/// actor's roles there and of their platform roles — an operator acts on the compartments —; on the platform, those
/// of their platform roles. Nobody changes their own memberships or roles. A job — the installation — is bounded by
/// neither: it is what seats the first administrator. Any other actor that is not a person — a client — holds no role,
/// and so assigns none. Access answered first whether the actor may assign at all, on the operation of the route;
/// this answers which roles.
/// </summary>
internal sealed class AssignmentRules(IRoleStore roles, IMembershipStore memberships, IOperatorStore operators)
{
    /// <summary>Whether the actor is a job, <c>job:…</c>: the installation, a scheduled task.</summary>
    public static bool IsJob(Actor actor) => actor.Value.StartsWith(Actor.JobPrefix, StringComparison.Ordinal);

    /// <summary>The refusal when the actor is the person whose memberships or roles change; <see langword="null"/> otherwise.</summary>
    public static Error? Own(string subject, Actor actor)
        => RegistryRules.IsSubject(actor.Value) && RegistryRules.NormalizeSubject(actor.Value) == subject ? RegistryErrors.OwnRoles(subject) : null;

    /// <summary>
    /// The refusal when the actor may not assign every role of <paramref name="roleNames"/> to <paramref name="subject"/>
    /// — in <paramref name="compartment"/>, or on the platform when it is <see langword="null"/> —; <see langword="null"/> otherwise.
    /// </summary>
    public async Task<Error?> CheckAsync(Actor actor, string subject, Compartment? compartment, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken)
    {
        if (IsJob(actor))
            return null;
        if (Own(subject, actor) is { } own)
            return own;
        if (roleNames.Count == 0)
            return null;

        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        var held = new List<string>();
        if (RegistryRules.IsSubject(actor.Value))
        {
            var author = RegistryRules.NormalizeSubject(actor.Value);
            held.AddRange(await operators.ListRolesAsync(author, cancellationToken).ConfigureAwait(false));
            if (compartment is not null)
                held.AddRange((await memberships.ListByUserAsync(author, cancellationToken).ConfigureAwait(false))
                    .Where(membership => membership.Compartment == compartment)
                    .SelectMany(membership => membership.Roles));
        }
        var lists = held.Distinct(StringComparer.Ordinal).Where(all.ContainsKey).Select(name => all[name].Assignable).ToList();

        foreach (var name in roleNames)
        {
            // A role gone from the catalogue of roles cannot be checked; it cannot be assigned either, and unassigning
            // what no longer exists takes nothing from anyone.
            if (!all.TryGetValue(name, out var role))
                continue;
            if (!lists.Any(list => list.Covers(role)))
                return RegistryErrors.RoleNotAssignable(name);
        }
        return null;
    }
}
