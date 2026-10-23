using Stellaegis.Primitives;

namespace Stellaegis.Registry.AspNetCore;

/// <summary>A compartment, as the administration routes answer it.</summary>
/// <param name="Compartment">Its name.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Status">Its state: <c>active</c>, <c>suspended</c> or <c>terminated</c>.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="CreatedBy">Who created it.</param>
/// <param name="ModifiedAt">When it was last changed.</param>
/// <param name="ModifiedBy">Who last changed it.</param>
/// <param name="Owner">The subject of its owner, or <see langword="null"/> while none was seated.</param>
public sealed record CompartmentSummary(string Compartment, string DisplayName, string Status, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy, string? Owner)
{
    internal static CompartmentSummary Of(CompartmentRecord record)
        => new(record.Compartment.Value, record.DisplayName, Statuses.Of(record.Status),
            record.Audit.Created.At, record.Audit.Created.By.Value, record.Audit.Modified.At, record.Audit.Modified.By.Value, record.Owner);
}

/// <summary>The body of the creation of a compartment.</summary>
/// <param name="Compartment">Its name, in the grammar of a compartment.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Owner">The subject of its first owner, seated with it; none when absent, for a later designation.</param>
public sealed record CreateCompartmentRequest(string Compartment, string DisplayName, string? Owner = null);

/// <summary>A transfer of ownership waiting for its recipient, as the routes answer it.</summary>
/// <param name="Compartment">The compartment.</param>
/// <param name="From">The owner who proposed it.</param>
/// <param name="To">The member who would become the owner.</param>
/// <param name="Kept">The roles the outgoing owner keeps.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="ExpiresAt">When it lapses, unanswered.</param>
public sealed record TransferSummary(string Compartment, string From, string To, string[] Kept, DateTimeOffset ProposedAt, DateTimeOffset ExpiresAt)
{
    internal static TransferSummary Of(OwnershipTransfer transfer)
        => new(transfer.Compartment.Value, transfer.From, transfer.To, transfer.Kept.ToArray(), transfer.Proposed.At, transfer.ExpiresAt);
}

/// <summary>The body of a proposal of transfer.</summary>
/// <param name="To">The subject of the member who would become the owner.</param>
/// <param name="Keep">The roles the outgoing owner keeps; every role they hold when absent.</param>
public sealed record ProposeTransferRequest(string To, string[]? Keep = null);

/// <summary>The body of the renaming of a compartment.</summary>
/// <param name="DisplayName">What people call it from now on.</param>
public sealed record RenameCompartmentRequest(string DisplayName);

/// <summary>A user, as the administration routes answer them.</summary>
/// <param name="Subject">Their <c>sub</c>.</param>
/// <param name="Email">Their e-mail.</param>
/// <param name="DisplayName">What people call them.</param>
/// <param name="Status">Their state: <c>active</c> or <c>deactivated</c>.</param>
/// <param name="CreatedAt">When they were recorded.</param>
/// <param name="CreatedBy">Who recorded them: themselves, when they registered.</param>
/// <param name="ModifiedAt">When they were last changed.</param>
/// <param name="ModifiedBy">Who last changed them.</param>
public sealed record UserSummary(string Subject, string Email, string DisplayName, string Status, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy)
{
    internal static UserSummary Of(UserRecord record)
        => new(record.Subject, record.Email, record.DisplayName, Statuses.Of(record.Status),
            record.Audit.Created.At, record.Audit.Created.By.Value, record.Audit.Modified.At, record.Audit.Modified.By.Value);
}

/// <summary>An operation of the catalogue, as the administration routes answer it.</summary>
/// <param name="Module">The module that declares it.</param>
/// <param name="Operation">Its identity, <c>target.name</c>.</param>
/// <param name="Kind">What it does to the data: <c>read</c>, <c>write</c> or <c>delete</c>.</param>
public sealed record CatalogEntrySummary(string Module, string Operation, string Kind)
{
    internal static CatalogEntrySummary Of(CatalogEntry entry)
        => new(entry.Module, entry.Operation, entry.Kind.ToString().ToLowerInvariant());
}

/// <summary>A role with its operations, as the administration routes answer it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Scope">Where it is held: <c>compartment</c> or <c>platform</c>.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Declared">Whether the application declares it in code — then it cannot be deleted.</param>
/// <param name="Operations">The identities of its operations.</param>
/// <param name="Category">What it is for: <c>administration</c> or <c>business</c>.</param>
/// <param name="Assignable">The roles it lets its holder assign.</param>
/// <param name="RequiresSecondFactor">Whether a second factor is required to exercise it; always for a platform role.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="CreatedBy">Who created it: the installation, for a declared role.</param>
/// <param name="ModifiedAt">When it was last changed.</param>
/// <param name="ModifiedBy">Who last changed it.</param>
public sealed record RoleSummary(
    string Name, string Scope, string DisplayName, bool Declared, string[] Operations, string Category, AssignableRolesBody Assignable,
    bool RequiresSecondFactor, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy)
{
    internal static RoleSummary Of(Role role)
        => new(role.Name, Statuses.Of(role.Scope), role.DisplayName, role.Declared, role.Operations.ToArray(),
            Statuses.Of(role.Category), AssignableRolesBody.Of(role.Assignable), role.RequiresSecondFactor,
            role.Audit.Created.At, role.Audit.Created.By.Value, role.Audit.Modified.At, role.Audit.Modified.By.Value);
}

/// <summary>The roles a role lets its holder assign, as the routes read and write them.</summary>
/// <param name="Compartment">The compartment roles covered as a whole, those created later included: <c>none</c>, <c>business</c> or <c>all</c>.</param>
/// <param name="Platform">Whether every platform role is covered; a platform role only.</param>
/// <param name="Roles">The roles named one by one.</param>
public sealed record AssignableRolesBody(string Compartment, bool Platform, string[] Roles)
{
    internal static AssignableRolesBody Of(AssignableRoles assignable)
        => new(Statuses.Of(assignable.Compartment), assignable.AllPlatform, assignable.Roles.ToArray());

    internal bool TryRead(out AssignableRoles assignable)
    {
        assignable = AssignableRoles.None;
        if (!Statuses.TryParse(Compartment, out CompartmentRoleSelector compartment) || Roles is null || Roles.Any(role => !RegistryRules.IsRoleName(role)))
            return false;
        assignable = new AssignableRoles(compartment, Platform, Roles.Distinct(StringComparer.Ordinal).ToList());
        return true;
    }
}

/// <summary>The body of the creation of a role.</summary>
/// <param name="Name">Its name, an identifier.</param>
/// <param name="Scope">Where it is held: <c>compartment</c> or <c>platform</c>.</param>
/// <param name="Category">What it is for: <c>administration</c> or <c>business</c>.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Operations">The identities of its operations, each in the catalogue.</param>
/// <param name="Assignable">The roles it lets its holder assign; none when absent.</param>
/// <param name="RequiresSecondFactor">Whether a second factor is required to exercise it; forced for a platform role.</param>
public sealed record CreateRoleRequest(
    string Name, string Scope, string Category, string DisplayName, string[] Operations,
    AssignableRolesBody? Assignable = null, bool RequiresSecondFactor = false);

/// <summary>The body of the change of a role: its definition, replaced as a whole.</summary>
/// <param name="Category">What it is for: <c>administration</c> or <c>business</c>.</param>
/// <param name="DisplayName">What people call it from now on.</param>
/// <param name="Operations">The identities of its operations, each in the catalogue.</param>
/// <param name="Assignable">The roles it lets its holder assign; none when absent.</param>
/// <param name="RequiresSecondFactor">Whether a second factor is required to exercise it; forced for a platform role.</param>
public sealed record UpdateRoleRequest(
    string Category, string DisplayName, string[] Operations,
    AssignableRolesBody? Assignable = null, bool RequiresSecondFactor = false);

/// <summary>A member of a compartment with their roles there, as the administration routes answer them.</summary>
/// <param name="Subject">The member.</param>
/// <param name="Compartment">The compartment.</param>
/// <param name="Roles">The names of their compartment roles there.</param>
/// <param name="GrantedAt">When they were admitted in the compartment.</param>
/// <param name="GrantedBy">Who admitted them.</param>
public sealed record MemberSummary(string Subject, string Compartment, string[] Roles, DateTimeOffset GrantedAt, string GrantedBy)
{
    internal static MemberSummary Of(Membership membership)
        => new(membership.Subject, membership.Compartment.Value, membership.Roles.ToArray(), membership.Granted.At, membership.Granted.By.Value);
}

/// <summary>An operator of the platform with their roles, as the administration routes answer them.</summary>
/// <param name="Subject">The operator.</param>
/// <param name="Roles">The names of their platform roles.</param>
public sealed record OperatorSummary(string Subject, string[] Roles);

/// <summary>The states and scopes on the wire: lowercase words, never the numbers of the enums.</summary>
internal static class Statuses
{
    public static string Of(CompartmentStatus status) => status.ToString().ToLowerInvariant();

    public static string Of(UserStatus status) => status.ToString().ToLowerInvariant();

    public static string Of(RoleScope scope) => scope.ToString().ToLowerInvariant();

    public static bool TryParseScope(string? value, out RoleScope scope)
        => TryParse(value, out scope);

    public static string Of(RoleCategory category) => category.ToString().ToLowerInvariant();

    public static string Of(CompartmentRoleSelector selector) => selector.ToString().ToLowerInvariant();

    // A name only, as the routes write it: never a number, which Enum.TryParse would take.
    public static bool TryParse<T>(string? value, out T parsed) where T : struct, Enum
    {
        parsed = default;
        return value is not null && value.Length > 0 && char.IsLetter(value[0])
               && Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }
}
