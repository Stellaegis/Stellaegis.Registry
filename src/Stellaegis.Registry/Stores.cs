using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>The compartments. An adapter implements it; <c>Stellaegis.Registry.PostgreSQL</c> on the table <c>reg.compartments</c>.</summary>
public interface ICompartmentStore
{
    /// <summary>The compartment, or <see langword="null"/>.</summary>
    Task<CompartmentRecord?> GetAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Every compartment, terminated ones included, by name.</summary>
    Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts the compartment, or replaces its display name and status.</summary>
    Task SaveAsync(CompartmentRecord compartment, CancellationToken cancellationToken = default);
}

/// <summary>The users, the projection of the accounts of Identity.</summary>
public interface IUserStore
{
    /// <summary>The user, or <see langword="null"/>.</summary>
    Task<UserRecord?> GetAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Every user, by subject.</summary>
    Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts the user, or replaces their e-mail, display name and status.</summary>
    Task SaveAsync(UserRecord user, CancellationToken cancellationToken = default);
}

/// <summary>The catalogue of operations.</summary>
public interface ICatalogStore
{
    /// <summary>Every entry, by module then operation.</summary>
    Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts each entry, or replaces its module and kind; removes nothing.</summary>
    Task SaveAsync(IReadOnlyCollection<CatalogEntry> entries, CancellationToken cancellationToken = default);
}

/// <summary>The roles and their operations.</summary>
public interface IRoleStore
{
    /// <summary>The role with its operations, or <see langword="null"/>.</summary>
    Task<Role?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Every role, or those of one scope, by name.</summary>
    Task<IReadOnlyList<Role>> ListAsync(RoleScope? scope = null, CancellationToken cancellationToken = default);

    /// <summary>Inserts the role with its operations, or replaces them.</summary>
    Task SaveAsync(Role role, CancellationToken cancellationToken = default);

    /// <summary>Deletes the role and its operations; nothing when absent.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>The memberships and the compartment roles of the members.</summary>
public interface IMembershipStore
{
    /// <summary>Whether the user is a member of the compartment.</summary>
    Task<bool> IsMemberAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>The memberships of the user, with their roles, by compartment.</summary>
    Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The members of the compartment, with their roles, by subject.</summary>
    Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>The members, in every compartment, that hold the role.</summary>
    Task<IReadOnlyList<Membership>> ListByRoleAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>Adds the membership with who granted it and when; nothing when present.</summary>
    Task AddAsync(string subject, Compartment compartment, Stamp granted, CancellationToken cancellationToken = default);

    /// <summary>Removes the membership and its roles; nothing when absent.</summary>
    Task RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Assigns the role to the member with who granted it and when; nothing when assigned.</summary>
    Task AssignRoleAsync(string subject, Compartment compartment, string role, Stamp granted, CancellationToken cancellationToken = default);

    /// <summary>Unassigns the role; nothing when absent.</summary>
    Task UnassignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default);

    /// <summary>Removes every membership of the compartment, with their roles.</summary>
    Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);
}

/// <summary>The operators of the platform: the platform roles of the users.</summary>
public interface IOperatorStore
{
    /// <summary>The platform roles of the user, by name.</summary>
    Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Every operator with their roles, by subject.</summary>
    Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The operators that hold the role.</summary>
    Task<IReadOnlyList<string>> ListByRoleAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>Assigns the role with who granted it and when; nothing when assigned.</summary>
    Task AssignAsync(string subject, string role, Stamp granted, CancellationToken cancellationToken = default);

    /// <summary>Unassigns the role; nothing when absent.</summary>
    Task UnassignAsync(string subject, string role, CancellationToken cancellationToken = default);
}
