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

    /// <summary>
    /// Creates a role by hand: applied at once by the installation, proposed for a validation otherwise
    /// (<see cref="IValidations"/>) — the definition of a role is the most sensitive act of the model.
    /// </summary>
    Task<Result<ActOutcome<Role>>> CreateAsync(string name, RoleScope scope, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the definition of a role; every holder has their rights recomputed. Applied at once by the installation,
    /// which resets a declared role, or when only the display name changes; proposed for a validation otherwise.
    /// </summary>
    Task<Result<ActOutcome<Role>>> UpdateAsync(string name, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default);

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
    /// <summary>Creates the compartment, active, without an owner: one is seated later (<see cref="IOwnership.SeatAsync"/>), by a designation.</summary>
    Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the compartment, active, and seats its first owner at once — the gesture of the installation: the owner
    /// becomes its first member and holds the role of the owner, every administration right of the compartment.
    /// </summary>
    Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, string owner, Actor actor, CancellationToken cancellationToken = default);

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
    /// <remarks>
    /// Applied at once by the installation; proposed for a validation otherwise (<see cref="IValidations"/>): the answer
    /// is then the act waiting, and the role takes effect once a second channel validated it.
    /// </remarks>
    Task<Result<PendingAct?>> AssignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Unassigns a platform role from a user, under the same rules as its assignment; at once, since it takes rights away.</summary>
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
    ActProposals proposals,
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
            if (declaration.Name == RegistryRoles.Owner && declaration.Scope != RoleScope.Compartment)
                return Result.Failure(RegistryErrors.RoleReserved(declaration.Name));
            var definition = Normalize(declaration.Name, declaration.Scope, declaration.Definition);
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

    public async Task<Result<ActOutcome<Role>>> CreateAsync(string name, RoleScope scope, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition);
        Guard.NotNull(actor);
        if (!RegistryRules.IsRoleName(name))
            return Result.Failure<ActOutcome<Role>>(RegistryErrors.RoleNameInvalid(name));
        if (name == RegistryRoles.Owner)
            return Result.Failure<ActOutcome<Role>>(RegistryErrors.RoleReserved(name));
        if (await store.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null)
            return Result.Failure<ActOutcome<Role>>(RegistryErrors.RoleExists(name));
        definition = Normalize(name, scope, definition);
        var invalid = await ValidateAsync(name, scope, definition, null, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
            return Result.Failure<ActOutcome<Role>>(invalid);

        if (!AssignmentRules.IsJob(actor))
            return await ProposeAsync(name, scope, definition, actor, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var role = Build(name, scope, definition, declared: false, Audit.CreatedBy(actor, now));
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = now, Role = name, Exists = true, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success(new ActOutcome<Role>(role, null));
    }

    public async Task<Result<ActOutcome<Role>>> UpdateAsync(string name, RoleDefinition definition, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition);
        Guard.NotNull(actor);
        var existing = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<ActOutcome<Role>>(RegistryErrors.RoleNotFound(name));
        if (await HeldByAsync(existing, actor, cancellationToken).ConfigureAwait(false))
            return Result.Failure<ActOutcome<Role>>(RegistryErrors.RoleHeldByAuthor(name));
        definition = Normalize(name, existing.Scope, definition);
        var invalid = await ValidateAsync(name, existing.Scope, definition, null, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
            return Result.Failure<ActOutcome<Role>>(invalid);

        // The display name gives nothing: it changes at once.
        if (!AssignmentRules.IsJob(actor) && !Same(existing, definition with { DisplayName = existing.DisplayName }))
            return await ProposeAsync(name, existing.Scope, definition, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success(new ActOutcome<Role>(await SaveAsync(existing, definition, actor, cancellationToken).ConfigureAwait(false), null));
    }

    /// <summary>
    /// Applies a definition validated: creates the role, or changes it — everything checked again, the name still free
    /// for a creation, the role still there for a change. Its author is the one who proposed it.
    /// </summary>
    internal async Task<Result> ApplyAsync(string name, RoleScope scope, RoleDefinition definition, Actor proposer, CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.Scope != scope)
            return Result.Failure(RegistryErrors.RoleExists(name));
        var invalid = await ValidateAsync(name, scope, definition, null, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
            return Result.Failure(invalid);
        if (existing is null)
        {
            var now = time.GetUtcNow();
            await store.SaveAsync(Build(name, scope, definition, declared: false, Audit.CreatedBy(proposer, now)), cancellationToken).ConfigureAwait(false);
            await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = now, Role = name, Exists = true, By = proposer }, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        await SaveAsync(existing, definition, proposer, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Role> SaveAsync(Role existing, RoleDefinition definition, Actor actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var role = Build(existing.Name, existing.Scope, definition, existing.Declared, existing.Audit.ModifiedBy(actor, now));
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = now, Role = role.Name, Exists = true, By = actor }, cancellationToken).ConfigureAwait(false);
        await projector.ProjectRoleHoldersAsync(role, cancellationToken).ConfigureAwait(false);
        return role;
    }

    private async Task<Result<ActOutcome<Role>>> ProposeAsync(string name, RoleScope scope, RoleDefinition definition, Actor actor, CancellationToken cancellationToken)
    {
        var proposed = await proposals.ProposeAsync(PendingActKind.RoleDefinition, null, name, null, scope, definition, actor, cancellationToken).ConfigureAwait(false);
        return proposed.IsSuccess
            ? Result.Success(new ActOutcome<Role>(null, proposed.Value))
            : Result.Failure<ActOutcome<Role>>(proposed.Error!);
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
    // compartment; the flag is set rather than refused, so that a declaration need not repeat it. The role of the owner
    // keeps what it always is, whatever a declaration or a change gives it, but its display name and operations.
    private static RoleDefinition Normalize(string name, RoleScope scope, RoleDefinition definition) => name == RegistryRoles.Owner
        ? Normalize(RoleScope.Compartment, RegistryRoles.OwnerDefinition(definition))
        : Normalize(scope, definition);

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
    internal async Task<bool> HeldByAsync(Role role, Actor actor, CancellationToken cancellationToken)
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
    Ownership ownership,
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

    public async Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, string owner, Actor actor, CancellationToken cancellationToken = default)
    {
        owner = RegistryRules.NormalizeSubject(owner);
        // The owner is checked before the compartment exists: a creation refused leaves nothing behind.
        var seatable = await ownership.CheckSeatableAsync(owner, actor, cancellationToken).ConfigureAwait(false);
        if (seatable is not null)
            return Result.Failure<CompartmentRecord>(seatable);
        var created = await CreateAsync(compartment, displayName, actor, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
            return created;
        var seated = await ownership.SeatAsync(compartment, owner, actor, cancellationToken).ConfigureAwait(false);
        return seated.IsFailure ? Result.Failure<CompartmentRecord>(seated.Error!) : await GetAsync(compartment, cancellationToken).ConfigureAwait(false);
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
        if (AssignmentRules.Own(subject, actor) is { } own)
            return Result.Failure(own);
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
    ActProposals proposals,
    RightsProjector projector,
    TimeProvider time) : IOperators
{
    public async Task<Result<PendingAct?>> AssignAsync(string subject, string role, Actor actor, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(actor);
        var assignable = await AssignableAsync(subject, role, cancellationToken).ConfigureAwait(false);
        if (assignable.IsFailure)
            return Result.Failure<PendingAct?>(assignable.Error!);
        var refused = await rules.CheckAsync(actor, subject, compartment: null, [role], cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return Result.Failure<PendingAct?>(refused);
        if (await store.ListRolesAsync(subject, cancellationToken).ConfigureAwait(false) is var held && held.Contains(role, StringComparer.Ordinal))
            return Result.Success<PendingAct?>(null);

        if (!AssignmentRules.IsJob(actor))
        {
            var proposed = await proposals.ProposeAsync(PendingActKind.PlatformRole, subject, role, null, null, null, actor, cancellationToken).ConfigureAwait(false);
            return proposed.IsSuccess ? Result.Success<PendingAct?>(proposed.Value) : Result.Failure<PendingAct?>(proposed.Error!);
        }
        await AssignNowAsync(subject, role, actor, cancellationToken).ConfigureAwait(false);
        return Result.Success<PendingAct?>(null);
    }

    /// <summary>Applies an assignment validated, checked again: the user still active, the role still a platform role. Its grantor is the one who proposed it.</summary>
    internal async Task<Result> ApplyAsync(string subject, string role, Actor proposer, CancellationToken cancellationToken)
    {
        var assignable = await AssignableAsync(subject, role, cancellationToken).ConfigureAwait(false);
        if (assignable.IsFailure)
            return assignable;
        await AssignNowAsync(subject, role, proposer, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task AssignNowAsync(string subject, string role, Actor grantor, CancellationToken cancellationToken)
    {
        await store.AssignAsync(subject, role, new Stamp(time.GetUtcNow(), grantor), cancellationToken).ConfigureAwait(false);
        await projector.ProjectAdminGrantsAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> AssignableAsync(string subject, string role, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return Result.Failure(RegistryErrors.UserNotFound(subject));
        if (user.Status == UserStatus.Deactivated)
            return Result.Failure(RegistryErrors.UserDeactivated(subject));
        var record = await roles.GetAsync(Guard.NotWhiteSpace(role), cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.RoleNotFound(role));
        return record.Scope != RoleScope.Platform ? Result.Failure(RegistryErrors.RoleScopeMismatch(role, RoleScope.Platform)) : Result.Success();
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
internal sealed class AssignmentRules(IRoleStore roles, IMembershipStore memberships, IOperatorStore operators, ICompartmentStore compartments)
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
        // The owner of the compartment: nobody else touches their membership or roles there, an operator included —
        // the platform's rescue alone replaces an owner. The installation, which seats them, may still give them roles.
        var owner = compartment is null ? null : (await compartments.GetAsync(compartment, cancellationToken).ConfigureAwait(false))?.Owner;
        var self = Own(subject, actor) is not null;
        if (owner == subject && !self && !IsJob(actor))
            return RegistryErrors.OwnerUntouchable(subject, compartment!);
        // The role of the owner goes with the ownership, for everyone.
        if (roleNames.Contains(RegistryRoles.Owner, StringComparer.Ordinal))
            return RegistryErrors.RoleReserved(RegistryRoles.Owner);
        if (IsJob(actor))
            return null;

        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        if (self)
        {
            // The one exception to I7: the owner may give themselves business roles, and take them back, under their
            // own name — their list covers every role, and they would reach the same by a manager they name.
            var business = roleNames.Count > 0 && roleNames.All(name => all.TryGetValue(name, out var role) && role.Category == RoleCategory.Business);
            return owner == subject && business ? null : RegistryErrors.OwnRoles(subject);
        }
        if (roleNames.Count == 0)
            return null;

        return Covers(await HeldAsync(actor, compartment, all, cancellationToken).ConfigureAwait(false), all, roleNames);
    }

    /// <summary>
    /// The refusal when the actor may not assign every role of <paramref name="roleNames"/> in <paramref name="compartment"/>,
    /// whoever to — what an invitation, an assignment deferred to someone not known yet, needs; <see langword="null"/> otherwise.
    /// </summary>
    public async Task<Error?> CoversAsync(Actor actor, Compartment compartment, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken)
    {
        if (roleNames.Contains(RegistryRoles.Owner, StringComparer.Ordinal))
            return RegistryErrors.RoleReserved(RegistryRoles.Owner);
        if (IsJob(actor) || roleNames.Count == 0)
            return null;
        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        return Covers(await HeldAsync(actor, compartment, all, cancellationToken).ConfigureAwait(false), all, roleNames);
    }

    /// <summary>
    /// Whether the actor may designate the owner of a compartment that has none: the installation, or an operator whose
    /// platform roles cover every compartment role — the owner's list.
    /// </summary>
    public async Task<bool> DesignatesOwnersAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (IsJob(actor))
            return true;
        if (!RegistryRules.IsSubject(actor.Value))
            return false;
        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        return (await operators.ListRolesAsync(RegistryRules.NormalizeSubject(actor.Value), cancellationToken).ConfigureAwait(false))
            .Any(name => all.TryGetValue(name, out var role) && role.Assignable.Compartment == CompartmentRoleSelector.All);
    }

    /// <summary>Whether the actor holds <paramref name="operation"/> by one of their roles, on the platform or in <paramref name="compartment"/>.</summary>
    public async Task<bool> HoldsAsync(Actor actor, Compartment compartment, string operation, CancellationToken cancellationToken)
    {
        if (IsJob(actor))
            return true;
        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        return (await HeldAsync(actor, compartment, all, cancellationToken).ConfigureAwait(false))
            .Any(name => all.TryGetValue(name, out var role) && role.Operations.Contains(operation, StringComparer.Ordinal));
    }

    // The roles of a person on the platform and, when given, in the compartment; none for an actor who is not a person.
    private async Task<IReadOnlyList<string>> HeldAsync(Actor actor, Compartment? compartment, IReadOnlyDictionary<string, Role> all, CancellationToken cancellationToken)
    {
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
        return held.Distinct(StringComparer.Ordinal).Where(all.ContainsKey).ToList();
    }

    private static Error? Covers(IReadOnlyList<string> held, IReadOnlyDictionary<string, Role> all, IReadOnlyCollection<string> roleNames)
    {
        var lists = held.Select(name => all[name].Assignable).ToList();
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

/// <summary>
/// The ownership of a compartment: one owner, carried by the compartment, its legal holder. A prerogative rather than a
/// permission: the owner holds the role of the owner — every administration right of the compartment, a second factor
/// required —, which nobody assigns, unassigns or takes from them, themselves included, but by a transfer; nobody
/// else changes their membership or roles there. The transfer is proposed by the owner and accepted by its recipient,
/// everything checked again at the acceptance; the outgoing owner keeps the roles they chose.
/// </summary>
public interface IOwnership
{
    /// <summary>
    /// Seats the first owner of a compartment that has none — a designation, or the gesture of the installation through
    /// <see cref="ICompartments.CreateAsync(Compartment, string, string, Actor, CancellationToken)"/>: the owner becomes a
    /// member and holds the role of the owner. Nobody seats themselves. A compartment with an owner is refused
    /// (<see cref="RegistryErrors.OwnerSeated"/>): its ownership changes by a transfer only.
    /// </summary>
    Task<Result> SeatAsync(Compartment compartment, string owner, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposes the ownership to a member, the owner being the actor; replaces a transfer waiting. <paramref name="keep"/>
    /// names the roles the outgoing owner keeps once it is done, each held now; every role they hold but that of the owner
    /// when <see langword="null"/>. It lapses after <see cref="RegistryRoles.TransferLifetime"/>.
    /// </summary>
    Task<Result<OwnershipTransfer>> ProposeAsync(Compartment compartment, string to, IReadOnlyCollection<string>? keep, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the transfer, the recipient being the actor. Everything is checked again, all or nothing: the proposer still
    /// owns the compartment, which is active, and the recipient is still an active member.
    /// </summary>
    Task<Result> AcceptAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the transfer waiting: the owner cancels it, or the recipient declines it.</summary>
    Task<Result> CancelAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>The transfer waiting for the compartment, or a failure.</summary>
    Task<Result<OwnershipTransfer>> GetTransferAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>
    /// The rescue of a compartment whose owner is unreachable, gone or dead (P5): another active user becomes its owner —
    /// a member if not one yet —, the former owner loses the role of the owner and keeps their other roles, a transfer
    /// waiting is cancelled. An act of the platform, proposed for a validation by a second channel; nobody rescues a
    /// compartment for themselves.
    /// </summary>
    Task<Result<PendingAct?>> RescueAsync(Compartment compartment, string owner, Actor actor, CancellationToken cancellationToken = default);
}

internal sealed class Ownership(
    ICompartmentStore store,
    IUserStore users,
    IMembershipStore memberships,
    IRoleStore roles,
    ActProposals proposals,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : IOwnership
{
    public async Task<Result> SeatAsync(Compartment compartment, string owner, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        owner = RegistryRules.NormalizeSubject(owner);
        var seatable = await CheckSeatableAsync(owner, actor, cancellationToken).ConfigureAwait(false);
        if (seatable is not null)
            return Result.Failure(seatable);
        var record = await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Status == CompartmentStatus.Terminated)
            return Result.Failure(RegistryErrors.CompartmentTerminated(compartment));
        if (record.Owner is not null)
            return Result.Failure(RegistryErrors.OwnerSeated(compartment));

        await EnsureOwnerRoleAsync(actor, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (!await store.SeatOwnerAsync(compartment, owner, new Stamp(now, actor), cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.OwnerSeated(compartment));
        await projector.ProjectMembershipsAsync(owner, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(owner, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new OwnershipChangedEvent { OccurredAt = now, Compartment = compartment, Owner = owner, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<OwnershipTransfer>> ProposeAsync(Compartment compartment, string to, IReadOnlyCollection<string>? keep, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        to = RegistryRules.NormalizeSubject(to);
        var record = await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure<OwnershipTransfer>(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Owner is null || !IsPerson(actor, record.Owner))
            return Result.Failure<OwnershipTransfer>(RegistryErrors.NotOwner(compartment));
        if (to == record.Owner)
            return Result.Failure<OwnershipTransfer>(RegistryErrors.TransferToSelf(compartment));
        if (await WhyNotAsync(record, to, cancellationToken).ConfigureAwait(false) is { } why)
            return Result.Failure<OwnershipTransfer>(RegistryErrors.TransferStale(compartment, why));

        var held = await HeldAsync(record.Owner, compartment, cancellationToken).ConfigureAwait(false);
        var kept = keep is null ? held : keep.Distinct(StringComparer.Ordinal).ToList();
        if (kept.FirstOrDefault(role => !held.Contains(role, StringComparer.Ordinal)) is { } notHeld)
            return Result.Failure<OwnershipTransfer>(RegistryErrors.RoleNotHeld(notHeld));

        var now = time.GetUtcNow();
        var transfer = new OwnershipTransfer(compartment, record.Owner, to, kept, new Stamp(now, actor), now + RegistryRoles.TransferLifetime);
        await store.SaveTransferAsync(transfer, cancellationToken).ConfigureAwait(false);
        return Result.Success(transfer);
    }

    public async Task<Result> AcceptAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        var transfer = await store.GetTransferAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (transfer is null)
            return Result.Failure(RegistryErrors.TransferNotFound(compartment));
        if (!IsPerson(actor, transfer.To))
            return Result.Failure(RegistryErrors.NotRecipient(compartment));
        var now = time.GetUtcNow();
        if (now >= transfer.ExpiresAt)
        {
            await store.DeleteTransferAsync(compartment, cancellationToken).ConfigureAwait(false);
            return Result.Failure(RegistryErrors.TransferExpired(compartment));
        }

        // Everything again, all or nothing: what the proposal assumed may have changed since.
        var record = await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Owner != transfer.From)
            return Result.Failure(RegistryErrors.TransferStale(compartment, "the proposer no longer owns it"));
        if (await WhyNotAsync(record, transfer.To, cancellationToken).ConfigureAwait(false) is { } why)
            return Result.Failure(RegistryErrors.TransferStale(compartment, why));

        var dropped = (await HeldAsync(transfer.From, compartment, cancellationToken).ConfigureAwait(false))
            .Where(role => !transfer.Kept.Contains(role, StringComparer.Ordinal)).ToList();
        await EnsureOwnerRoleAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!await store.TransferOwnershipAsync(transfer, dropped, new Stamp(now, actor), cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.TransferStale(compartment, "the proposer no longer owns it"));

        await projector.ProjectMembershipsAsync(transfer.To, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(transfer.To, compartment, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(transfer.From, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new OwnershipChangedEvent { OccurredAt = now, Compartment = compartment, Owner = transfer.To, Previous = transfer.From, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> CancelAsync(Compartment compartment, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(actor);
        var transfer = await store.GetTransferAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (transfer is null)
            return Result.Failure(RegistryErrors.TransferNotFound(compartment));
        if (!IsPerson(actor, transfer.From) && !IsPerson(actor, transfer.To))
            return Result.Failure(RegistryErrors.NotOwner(compartment));
        await store.DeleteTransferAsync(compartment, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<OwnershipTransfer>> GetTransferAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => await store.GetTransferAsync(Guard.NotNull(compartment), cancellationToken).ConfigureAwait(false) is { } transfer
            ? Result.Success(transfer)
            : Result.Failure<OwnershipTransfer>(RegistryErrors.TransferNotFound(compartment));

    public async Task<Result<PendingAct?>> RescueAsync(Compartment compartment, string owner, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        owner = RegistryRules.NormalizeSubject(owner);
        var rescuable = await RescuableAsync(compartment, owner, actor, cancellationToken).ConfigureAwait(false);
        if (rescuable.IsFailure)
            return Result.Failure<PendingAct?>(rescuable.Error!);
        if (!AssignmentRules.IsJob(actor))
        {
            var proposed = await proposals.ProposeAsync(PendingActKind.OwnershipRescue, owner, null, compartment, null, null, actor, cancellationToken).ConfigureAwait(false);
            return proposed.IsSuccess ? Result.Success<PendingAct?>(proposed.Value) : Result.Failure<PendingAct?>(proposed.Error!);
        }
        var applied = await ApplyRescueAsync(compartment, owner, actor, cancellationToken).ConfigureAwait(false);
        return applied.IsSuccess ? Result.Success<PendingAct?>(null) : Result.Failure<PendingAct?>(applied.Error!);
    }

    /// <summary>Applies a rescue validated, checked again; its grantor is the one who proposed it.</summary>
    internal async Task<Result> ApplyRescueAsync(Compartment compartment, string owner, Actor proposer, CancellationToken cancellationToken)
    {
        var rescuable = await RescuableAsync(compartment, owner, proposer, cancellationToken).ConfigureAwait(false);
        if (rescuable.IsFailure)
            return rescuable;
        var previous = (await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false))?.Owner;
        await EnsureOwnerRoleAsync(proposer, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (!await store.RescueOwnershipAsync(compartment, owner, new Stamp(now, proposer), cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.CompartmentNotFound(compartment));
        await projector.ProjectMembershipsAsync(owner, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(owner, compartment, cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous != owner)
            await projector.ProjectUserGrantsAsync(previous, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new OwnershipChangedEvent { OccurredAt = now, Compartment = compartment, Owner = owner, Previous = previous, By = proposer }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    // A compartment not terminated, an active user who is not its owner already, never the actor themselves.
    private async Task<Result> RescuableAsync(Compartment compartment, string owner, Actor actor, CancellationToken cancellationToken)
    {
        var seatable = await CheckSeatableAsync(owner, actor, cancellationToken).ConfigureAwait(false);
        if (seatable is not null)
            return Result.Failure(seatable);
        var record = await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Status == CompartmentStatus.Terminated)
            return Result.Failure(RegistryErrors.CompartmentTerminated(compartment));
        return record.Owner == owner ? Result.Failure(RegistryErrors.OwnerSeated(compartment)) : Result.Success();
    }

    /// <summary>The refusal when <paramref name="owner"/> cannot be seated by <paramref name="actor"/>: an active user, never by themselves.</summary>
    internal async Task<Error?> CheckSeatableAsync(string owner, Actor actor, CancellationToken cancellationToken)
    {
        Guard.NotNull(actor);
        if (AssignmentRules.Own(owner, actor) is { } own)
            return own;
        var user = await users.GetAsync(owner, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return RegistryErrors.UserNotFound(owner);
        return user.Status == UserStatus.Deactivated ? RegistryErrors.UserDeactivated(owner) : null;
    }

    private static bool IsPerson(Actor actor, string subject)
        => RegistryRules.IsSubject(actor.Value) && RegistryRules.NormalizeSubject(actor.Value) == subject;

    // Why the recipient cannot take the compartment now, or null: it is active, and they are an active member of it.
    private async Task<string?> WhyNotAsync(CompartmentRecord record, string to, CancellationToken cancellationToken)
    {
        if (record.Status != CompartmentStatus.Active)
            return "the compartment is not active";
        var user = await users.GetAsync(to, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatus.Active)
            return "the recipient is not an active user";
        return await memberships.IsMemberAsync(to, record.Compartment, cancellationToken).ConfigureAwait(false)
            ? null
            : "the recipient is not a member";
    }

    // The roles the owner holds in the compartment, but that of the owner.
    private async Task<List<string>> HeldAsync(string owner, Compartment compartment, CancellationToken cancellationToken)
        => (await memberships.ListByUserAsync(owner, cancellationToken).ConfigureAwait(false))
            .Where(membership => membership.Compartment == compartment)
            .SelectMany(membership => membership.Roles)
            .Where(role => role != RegistryRoles.Owner)
            .ToList();

    // The role of the owner exists before anyone holds it: an application that never declared it gets it without operations.
    private async Task EnsureOwnerRoleAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (await roles.GetAsync(RegistryRoles.Owner, cancellationToken).ConfigureAwait(false) is not null)
            return;
        var definition = RegistryRoles.OwnerDefinition(null);
        await roles.SaveAsync(new Role(RegistryRoles.Owner, RoleScope.Compartment, definition.Category, definition.DisplayName, Declared: true,
            definition.Operations, definition.Assignable!, definition.RequiresSecondFactor, Audit.CreatedBy(actor, time.GetUtcNow())), cancellationToken).ConfigureAwait(false);
    }
}
