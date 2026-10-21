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

/// <summary>The roles: a name and a set of operations of the catalogue, in one scope.</summary>
public interface IRoles
{
    /// <summary>
    /// Records the roles the application declares: creates each when missing, resets its display name and operations
    /// when present and declared. A role of the same name created by hand is refused (<see cref="RegistryErrors.RoleNotDeclared"/>).
    /// Every holder of a changed role has their rights recomputed.
    /// </summary>
    Task<Result> DeclareAsync(IEnumerable<RoleDeclaration> declarations, CancellationToken cancellationToken = default);

    /// <summary>Creates a role by hand.</summary>
    Task<Result<Role>> CreateAsync(string name, RoleScope scope, string displayName, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default);

    /// <summary>Changes the display name and the operations of a role; every holder has their rights recomputed. The installation resets a declared role.</summary>
    Task<Result<Role>> UpdateAsync(string name, string displayName, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default);

    /// <summary>Deletes a role created by hand that nobody holds.</summary>
    Task<Result> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>The role, or a failure.</summary>
    Task<Result<Role>> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Every role, or those of one scope.</summary>
    Task<IReadOnlyList<Role>> ListAsync(RoleScope? scope = null, CancellationToken cancellationToken = default);
}

/// <summary>The compartments. A compartment is never deleted: terminated, its name stays taken.</summary>
public interface ICompartments
{
    /// <summary>Creates the compartment, active.</summary>
    Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Renames the compartment.</summary>
    Task<Result<CompartmentRecord>> RenameAsync(Compartment compartment, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Suspends the compartment: its members keep their roles in the registry and hold no right until it resumes.</summary>
    Task<Result<CompartmentRecord>> SuspendAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Resumes the compartment: its members hold their rights again.</summary>
    Task<Result<CompartmentRecord>> ResumeAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Terminates the compartment: its memberships are gone, from the registry and from the decision point, and its name stays taken.</summary>
    Task<Result<CompartmentRecord>> TerminateAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>The compartment, or a failure.</summary>
    Task<Result<CompartmentRecord>> GetAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Every compartment, terminated ones included.</summary>
    Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The users, projected from Identity: the host calls <see cref="RecordAsync"/> from its subscription to the registration, <see cref="DeactivateAsync"/> from the blocking.</summary>
public interface IUserDirectory
{
    /// <summary>Records the user, active, or updates their e-mail and display name.</summary>
    Task<Result<UserRecord>> RecordAsync(string subject, string email, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Deactivates the user: they keep their memberships and roles in the registry and hold no right anywhere.</summary>
    Task<Result<UserRecord>> DeactivateAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Reactivates the user: they hold their rights again.</summary>
    Task<Result<UserRecord>> ReactivateAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The user, or a failure.</summary>
    Task<Result<UserRecord>> GetAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Every user.</summary>
    Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The memberships, and the compartment roles of the members.</summary>
public interface IMemberships
{
    /// <summary>Makes the user a member of the compartment, without any role yet.</summary>
    Task<Result> AddAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Removes the user from the compartment, with their roles there.</summary>
    Task<Result> RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Assigns a compartment role to a member.</summary>
    Task<Result> AssignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default);

    /// <summary>Unassigns a compartment role from a member.</summary>
    Task<Result> UnassignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default);

    /// <summary>The memberships of the user, with their roles.</summary>
    Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The members of the compartment, with their roles.</summary>
    Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);
}

/// <summary>The operators of the platform: the platform roles of the users.</summary>
public interface IOperators
{
    /// <summary>Assigns a platform role to a user.</summary>
    Task<Result> AssignAsync(string subject, string role, CancellationToken cancellationToken = default);

    /// <summary>Unassigns a platform role from a user.</summary>
    Task<Result> UnassignAsync(string subject, string role, CancellationToken cancellationToken = default);

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
    public async Task<Result> DeclareAsync(IEnumerable<RoleDeclaration> declarations, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(declarations);
        foreach (var declaration in declarations)
        {
            if (!RegistryRules.IsRoleName(declaration.Name))
                return Result.Failure(RegistryErrors.RoleNameInvalid(declaration.Name));
            var operations = declaration.OperationIdentities;
            var unknown = await UnknownAsync(operations, cancellationToken).ConfigureAwait(false);
            if (unknown is not null)
                return Result.Failure(RegistryErrors.UnknownOperation(unknown));

            var existing = await store.GetAsync(declaration.Name, cancellationToken).ConfigureAwait(false);
            if (existing is { Declared: false })
                return Result.Failure(RegistryErrors.RoleNotDeclared(declaration.Name));
            var role = new Role(declaration.Name, declaration.Scope, declaration.DisplayName, Declared: true, operations);
            if (existing is not null && existing == role with { Operations = existing.Operations } && existing.Operations.ToHashSet(StringComparer.Ordinal).SetEquals(operations))
                continue;
            await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
            await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = time.GetUtcNow(), Role = role.Name, Exists = true }, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                await projector.ProjectRoleHoldersAsync(role, cancellationToken).ConfigureAwait(false);
        }
        return Result.Success();
    }

    public async Task<Result<Role>> CreateAsync(string name, RoleScope scope, string displayName, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(operations);
        if (!RegistryRules.IsRoleName(name))
            return Result.Failure<Role>(RegistryErrors.RoleNameInvalid(name));
        if (await store.GetAsync(name, cancellationToken).ConfigureAwait(false) is not null)
            return Result.Failure<Role>(RegistryErrors.RoleExists(name));
        var unknown = await UnknownAsync(operations, cancellationToken).ConfigureAwait(false);
        if (unknown is not null)
            return Result.Failure<Role>(RegistryErrors.UnknownOperation(unknown));

        var role = new Role(name, scope, Guard.NotWhiteSpace(displayName), Declared: false, operations.Distinct(StringComparer.Ordinal).ToList());
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = time.GetUtcNow(), Role = name, Exists = true }, cancellationToken).ConfigureAwait(false);
        return Result.Success(role);
    }

    public async Task<Result<Role>> UpdateAsync(string name, string displayName, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(operations);
        var existing = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<Role>(RegistryErrors.RoleNotFound(name));
        var unknown = await UnknownAsync(operations, cancellationToken).ConfigureAwait(false);
        if (unknown is not null)
            return Result.Failure<Role>(RegistryErrors.UnknownOperation(unknown));

        var role = existing with { DisplayName = Guard.NotWhiteSpace(displayName), Operations = operations.Distinct(StringComparer.Ordinal).ToList() };
        await store.SaveAsync(role, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = time.GetUtcNow(), Role = name, Exists = true }, cancellationToken).ConfigureAwait(false);
        await projector.ProjectRoleHoldersAsync(role, cancellationToken).ConfigureAwait(false);
        return Result.Success(role);
    }

    public async Task<Result> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
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

        await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new RoleChangedEvent { OccurredAt = time.GetUtcNow(), Role = name, Exists = false }, cancellationToken).ConfigureAwait(false);
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
}

internal sealed class Compartments(
    ICompartmentStore store,
    IMembershipStore memberships,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : ICompartments
{
    public async Task<Result<CompartmentRecord>> CreateAsync(Compartment compartment, string displayName, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        if (await store.GetAsync(compartment, cancellationToken).ConfigureAwait(false) is not null)
            return Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentExists(compartment));
        var record = new CompartmentRecord(compartment, Guard.NotWhiteSpace(displayName), CompartmentStatus.Active, time.GetUtcNow());
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = compartment, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> RenameAsync(Compartment compartment, string displayName, CancellationToken cancellationToken = default)
    {
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        var record = current.Value with { DisplayName = Guard.NotWhiteSpace(displayName) };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = compartment, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> SuspendAsync(Compartment compartment, CancellationToken cancellationToken = default)
    {
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        if (current.Value.Status == CompartmentStatus.Suspended)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Suspended };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = compartment, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> ResumeAsync(Compartment compartment, CancellationToken cancellationToken = default)
    {
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        if (current.Value.Status == CompartmentStatus.Active)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Active };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.ProjectCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = compartment, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> TerminateAsync(Compartment compartment, CancellationToken cancellationToken = default)
    {
        var current = await LiveAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
            return current;
        var record = current.Value with { Status = CompartmentStatus.Terminated };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await memberships.RemoveCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new CompartmentChangedEvent { OccurredAt = time.GetUtcNow(), Compartment = compartment, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<CompartmentRecord>> GetAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => await store.GetAsync(Guard.NotNull(compartment), cancellationToken).ConfigureAwait(false) is { } record
            ? Result.Success(record)
            : Result.Failure<CompartmentRecord>(RegistryErrors.CompartmentNotFound(compartment));

    public Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);

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
    public async Task<Result<UserRecord>> RecordAsync(string subject, string email, string displayName, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotWhiteSpace(email);
        Guard.NotWhiteSpace(displayName);
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        var record = existing is null
            ? new UserRecord(subject, email, displayName, UserStatus.Active, time.GetUtcNow())
            : existing with { Email = email, DisplayName = displayName };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new UserChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<UserRecord>> DeactivateAsync(string subject, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<UserRecord>(RegistryErrors.UserNotFound(subject));
        if (existing.Status == UserStatus.Deactivated)
            return Result.Success(existing);
        var record = existing with { Status = UserStatus.Deactivated };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.RemoveSubjectAsync(subject, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new UserChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Status = record.Status }, cancellationToken).ConfigureAwait(false);
        return Result.Success(record);
    }

    public async Task<Result<UserRecord>> ReactivateAsync(string subject, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        var existing = await store.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return Result.Failure<UserRecord>(RegistryErrors.UserNotFound(subject));
        if (existing.Status == UserStatus.Active)
            return Result.Success(existing);
        var record = existing with { Status = UserStatus.Active };
        await store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        await projector.ProjectSubjectAsync(subject, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new UserChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Status = record.Status }, cancellationToken).ConfigureAwait(false);
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
}

internal sealed class Memberships(
    IMembershipStore store,
    IUserStore users,
    ICompartmentStore compartments,
    IRoleStore roles,
    RightsProjector projector,
    IEventPublisher publisher,
    TimeProvider time) : IMemberships
{
    public async Task<Result> AddAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        var check = await CheckAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
            return check;
        if (await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Success();
        await store.AddAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await projector.ProjectMembershipsAsync(subject, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new MembershipChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment, IsMember = true }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(compartment);
        if (!await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Success();
        await store.RemoveAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await projector.ProjectMembershipsAsync(subject, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new MembershipChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment, IsMember = false }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> AssignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
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

        await store.AssignRoleAsync(subject, compartment, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new MembershipChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment, IsMember = true }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UnassignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotNull(compartment);
        Guard.NotWhiteSpace(role);
        if (!await store.IsMemberAsync(subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.NotMember(subject, compartment));
        await store.UnassignRoleAsync(subject, compartment, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectUserGrantsAsync(subject, compartment, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new MembershipChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment, IsMember = true }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default)
        => store.ListByUserAsync(RegistryRules.NormalizeSubject(subject), cancellationToken);

    public Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => store.ListByCompartmentAsync(Guard.NotNull(compartment), cancellationToken);

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
    RightsProjector projector) : IOperators
{
    public async Task<Result> AssignAsync(string subject, string role, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
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

        await store.AssignAsync(subject, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectAdminGrantsAsync(subject, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> UnassignAsync(string subject, string role, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        Guard.NotWhiteSpace(role);
        await store.UnassignAsync(subject, role, cancellationToken).ConfigureAwait(false);
        await projector.ProjectAdminGrantsAsync(subject, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken = default)
        => store.ListRolesAsync(RegistryRules.NormalizeSubject(subject), cancellationToken);

    public Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken = default)
        => store.ListAsync(cancellationToken);
}
