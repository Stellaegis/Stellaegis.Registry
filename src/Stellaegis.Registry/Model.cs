using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>The state of a compartment. A compartment is never deleted: its name is never reused.</summary>
public enum CompartmentStatus
{
    /// <summary>In service: its members hold their rights.</summary>
    Active,

    /// <summary>Suspended: its members keep their memberships and roles in the registry, but hold no right until it resumes.</summary>
    Suspended,

    /// <summary>Terminated: its memberships are gone, the name stays taken.</summary>
    Terminated,
}

/// <summary>A compartment of the application, as the registry knows it.</summary>
/// <param name="Compartment">Its name, the one the frame and the routes use.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Status">Its state.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record CompartmentRecord(Compartment Compartment, string DisplayName, CompartmentStatus Status, DateTimeOffset CreatedAt);

/// <summary>The state of a user in the registry.</summary>
public enum UserStatus
{
    /// <summary>Active: holds the rights of their roles.</summary>
    Active,

    /// <summary>Deactivated — blocked or deleted in Identity: keeps their memberships and roles in the registry, holds no right.</summary>
    Deactivated,
}

/// <summary>
/// A user as the registry knows them: the projection of an account of Identity. The registry authenticates nothing;
/// it records who exists, to give a membership or a role to.
/// </summary>
/// <param name="Subject">The <c>sub</c> of the account, a GUID written without hyphens.</param>
/// <param name="Email">Their e-mail, as Identity gave it.</param>
/// <param name="DisplayName">What people call them.</param>
/// <param name="Status">Their state.</param>
/// <param name="CreatedAt">When they were recorded.</param>
public sealed record UserRecord(string Subject, string Email, string DisplayName, UserStatus Status, DateTimeOffset CreatedAt);

/// <summary>
/// An operation of the catalogue: what can be granted, as a module declared it. The catalogue is written by the
/// installation from the declarations of the modules, never by hand.
/// </summary>
/// <param name="Module">The module that declares it.</param>
/// <param name="Operation">Its identity, <c>target.name</c>.</param>
/// <param name="Kind">What it does to the data.</param>
public sealed record CatalogEntry(string Module, string Operation, OperationKind Kind);

/// <summary>The two scopes of a role, the two parallel models of the registry, never mixed.</summary>
public enum RoleScope
{
    /// <summary>A role a member holds in a compartment, applicable in every compartment.</summary>
    Compartment,

    /// <summary>A role an operator of the platform holds, in the shared space.</summary>
    Platform,
}

/// <summary>A role: a name and a set of operations of the catalogue, in one scope.</summary>
/// <param name="Name">Its name, an identifier: <c>super_admin</c>, <c>tenant_administrator</c>.</param>
/// <param name="Scope">Where it is held.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Declared">Whether the application declares it in code: the installation re-applies it, and it cannot be deleted.</param>
/// <param name="Operations">The identities of its operations, each in the catalogue.</param>
public sealed record Role(string Name, RoleScope Scope, string DisplayName, bool Declared, IReadOnlyCollection<string> Operations);

/// <summary>
/// A role the application declares in code, which the installation records and re-applies: created when missing,
/// its operations and display name reset when present. A role created by hand is never touched by a declaration.
/// <code>
/// new RoleDeclaration("chef_atelier", RoleScope.Compartment, "Workshop manager", [WorkOrderOperations.Read, WorkOrderOperations.Schedule]);
/// </code>
/// </summary>
/// <param name="Name">Its name, an identifier.</param>
/// <param name="Scope">Where it is held.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Operations">Its operations, each declared by a module recorded in the catalogue.</param>
public sealed record RoleDeclaration(string Name, RoleScope Scope, string DisplayName, IReadOnlyCollection<Operation> Operations)
{
    /// <summary>The identities of the operations.</summary>
    public IReadOnlyCollection<string> OperationIdentities => Operations.Select(operation => operation.Identity).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>The roles of a member in a compartment.</summary>
/// <param name="Subject">The member.</param>
/// <param name="Compartment">The compartment.</param>
/// <param name="Roles">The names of their compartment roles there.</param>
public sealed record Membership(string Subject, Compartment Compartment, IReadOnlyCollection<string> Roles);

/// <summary>The rules the registry holds its names to.</summary>
public static class RegistryRules
{
    /// <summary>Whether the value is the subject of a person: a GUID, which is written without hyphens.</summary>
    public static bool IsSubject(string? value) => Guid.TryParse(value, out _);

    /// <summary>A subject in the form the registry stores: the GUID without hyphens, as Identity emits <c>sub</c>.</summary>
    public static string Subject(Guid userUniqueId) => Guard.NotEmpty(userUniqueId).ToString("N");

    /// <summary>The subject normalized, whatever format the GUID came in.</summary>
    public static string NormalizeSubject(string subject)
    {
        Guard.NotWhiteSpace(subject);
        return Guid.TryParse(subject, out var id) && id != Guid.Empty ? id.ToString("N") : throw new ArgumentException($"'{subject}' is not the subject of a person: a GUID.", nameof(subject));
    }

    /// <summary>Whether the value is a role name: an identifier.</summary>
    public static bool IsRoleName(string? value) => NameRules.IsIdentifier(value);
}
