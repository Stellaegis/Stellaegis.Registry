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

/// <summary>
/// Who wrote a row, and when: the trace every write of the registry carries. The actor is never defaulted — a
/// write without one is an error, not "system".
/// </summary>
/// <param name="At">When.</param>
/// <param name="By">Who: the person on the routes, <c>job:install</c> for the installation, the person themselves when they registered.</param>
public sealed record Stamp(DateTimeOffset At, Actor By);

/// <summary>The trace of a row that is created once and changed after: its creation, and its last change.</summary>
/// <param name="Created">When and by whom the row was created.</param>
/// <param name="Modified">When and by whom it was last changed; the creation, until a change.</param>
public sealed record Audit(Stamp Created, Stamp Modified)
{
    /// <summary>The audit of a row just created.</summary>
    public static Audit CreatedBy(Actor by, DateTimeOffset at) => new(new Stamp(at, by), new Stamp(at, by));

    /// <summary>The audit after a change.</summary>
    public Audit ModifiedBy(Actor by, DateTimeOffset at) => this with { Modified = new Stamp(at, by) };
}

/// <summary>A compartment of the application, as the registry knows it.</summary>
/// <param name="Compartment">Its name, the one the frame and the routes use.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Status">Its state.</param>
/// <param name="Audit">Who created it and who last changed it, and when.</param>
/// <param name="Owner">
/// The subject of its owner, the legal holder of the compartment, or <see langword="null"/> while none was seated — a
/// compartment created for a designation by invitation. Once seated, the owner changes only by a transfer.
/// </param>
public sealed record CompartmentRecord(Compartment Compartment, string DisplayName, CompartmentStatus Status, Audit Audit, string? Owner = null);

/// <summary>
/// A transfer of the ownership of a compartment, proposed by its owner and waiting for its recipient: the ownership is
/// the legal holding of the compartment, which engages whoever takes it, so it is accepted, never imposed.
/// </summary>
/// <param name="Compartment">The compartment.</param>
/// <param name="From">The owner who proposed it.</param>
/// <param name="To">The member who would become the owner.</param>
/// <param name="Kept">The roles the outgoing owner keeps in the compartment once it is done; the others go with the ownership.</param>
/// <param name="Proposed">When and by whom it was proposed.</param>
/// <param name="ExpiresAt">When it lapses, unanswered.</param>
public sealed record OwnershipTransfer(Compartment Compartment, string From, string To, IReadOnlyCollection<string> Kept, Stamp Proposed, DateTimeOffset ExpiresAt);

/// <summary>The roles the registry reserves for itself.</summary>
public static class RegistryRoles
{
    /// <summary>
    /// The role of the owner of a compartment, <c>owner</c>: an administration role whose assignable list covers every
    /// compartment role and which requires a second factor, whatever its declaration says. The registry creates it; the
    /// application may declare it to give it its operations. It is seated and transferred with the ownership, never
    /// assigned nor unassigned.
    /// </summary>
    public const string Owner = "owner";

    /// <summary>How long a transfer of ownership waits for its recipient: seven days.</summary>
    public static readonly TimeSpan TransferLifetime = TimeSpan.FromDays(7);

    /// <summary>What the role of the owner always is, whatever else a declaration gives it.</summary>
    internal static RoleDefinition OwnerDefinition(RoleDefinition? declared) => new(
        RoleCategory.Administration,
        declared?.DisplayName ?? "Owner",
        declared?.Operations ?? [],
        AssignableRoles.Every(CompartmentRoleSelector.All));
}

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
/// <param name="Audit">Who recorded them — the person themselves, when they registered — and who last changed them, and when.</param>
public sealed record UserRecord(string Subject, string Email, string DisplayName, UserStatus Status, Audit Audit);

/// <summary>
/// An operation of the catalogue: what can be granted, as a module declared it. The catalogue is written by the
/// installation from the declarations of the modules, never by hand.
/// </summary>
/// <param name="Module">The module that declares it.</param>
/// <param name="Operation">Its identity, <c>target.name</c>.</param>
/// <param name="Kind">What it does to the data.</param>
/// <param name="DeclaredSecondFactor">Whether the module declares it requires a second factor (<see cref="Primitives.Operation.RequiresSecondFactor"/>).</param>
/// <param name="HardenedSecondFactor">Whether the installation made it require one, beyond its declaration — never the reverse.</param>
public sealed record CatalogEntry(string Module, string Operation, OperationKind Kind, bool DeclaredSecondFactor = false, bool HardenedSecondFactor = false)
{
    /// <summary>Whether exercising it requires a session whose person used a second factor: declared so, or hardened.</summary>
    public bool RequiresSecondFactor => DeclaredSecondFactor || HardenedSecondFactor;
}

/// <summary>
/// An operation granted, as the registry hands it to the decision point: its identity, and whether exercising it
/// requires a session whose person used a second factor — sensitive in itself in a compartment, always in the shared
/// space, where every operation administers.
/// </summary>
/// <param name="Operation">Its identity, <c>target.name</c>.</param>
/// <param name="RequiresSecondFactor">Whether the decision point refuses it to a session without a second factor in its <c>amr</c>.</param>
public sealed record OperationGrant(string Operation, bool RequiresSecondFactor);

/// <summary>The two scopes of a role, the two parallel models of the registry, never mixed.</summary>
public enum RoleScope
{
    /// <summary>A role a member holds in a compartment, applicable in every compartment.</summary>
    Compartment,

    /// <summary>A role an operator of the platform holds, in the shared space.</summary>
    Platform,
}

/// <summary>
/// What a role is for, declared with it and never deduced from its operations: an operation has no nature of
/// administering, and the same operation may enter a role of each category.
/// </summary>
public enum RoleCategory
{
    /// <summary>Administering who holds what: the people, their roles, the compartment.</summary>
    Administration,

    /// <summary>The business of the application: what its modules do.</summary>
    Business,
}

/// <summary>Which compartment roles an assignable list covers as a whole, the roles created later included.</summary>
public enum CompartmentRoleSelector
{
    /// <summary>None by selector: only those the list names.</summary>
    None,

    /// <summary>Every business compartment role.</summary>
    Business,

    /// <summary>Every compartment role, administration included.</summary>
    All,
}

/// <summary>
/// The roles a role lets its holder assign. Assigning a role is a right of its own, bounded by this list, and does
/// not suppose holding the roles it names: whoever manages the accesses of a company may read none of its books. A
/// selector covers the roles to come, which a list of names would forget until someone edits it.
/// <code>
/// AssignableRoles.Every(CompartmentRoleSelector.Business)      // the manager of the accesses
/// AssignableRoles.Of("operator", "setter")                       // a workshop manager
/// </code>
/// </summary>
/// <param name="Compartment">The compartment roles it covers as a whole.</param>
/// <param name="AllPlatform">Whether it covers every platform role; only a platform role may.</param>
/// <param name="Roles">The roles it names one by one, besides its selectors.</param>
public sealed record AssignableRoles(CompartmentRoleSelector Compartment, bool AllPlatform, IReadOnlyCollection<string> Roles)
{
    /// <summary>A role that lets its holder assign nothing — every business role, by default.</summary>
    public static AssignableRoles None { get; } = new(CompartmentRoleSelector.None, false, []);

    /// <summary>The compartment roles of a selector.</summary>
    public static AssignableRoles Every(CompartmentRoleSelector compartment, bool allPlatform = false) => new(compartment, allPlatform, []);

    /// <summary>The roles named.</summary>
    public static AssignableRoles Of(params string[] roles) => new(CompartmentRoleSelector.None, false, Guard.NotNull(roles).Distinct(StringComparer.Ordinal).ToList());

    /// <summary>Whether the list covers <paramref name="role"/>.</summary>
    public bool Covers(Role role)
    {
        Guard.NotNull(role);
        if (Roles.Contains(role.Name, StringComparer.Ordinal))
            return true;
        return role.Scope == RoleScope.Platform
            ? AllPlatform
            : Compartment == CompartmentRoleSelector.All || (Compartment == CompartmentRoleSelector.Business && role.Category == RoleCategory.Business);
    }

    /// <summary>Whether the two lists say the same, whatever the order of their names.</summary>
    public bool SameAs(AssignableRoles other)
        => other is not null && Compartment == other.Compartment && AllPlatform == other.AllPlatform
           && Roles.ToHashSet(StringComparer.Ordinal).SetEquals(other.Roles);
}

/// <summary>A role: a name and a set of operations of the catalogue, in one scope.</summary>
/// <param name="Name">Its name, an identifier: <c>super_admin</c>, <c>tenant_administrator</c>.</param>
/// <param name="Scope">Where it is held.</param>
/// <param name="Category">What it is for: administration or business.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Declared">Whether the application declares it in code: the installation re-applies it, and it cannot be deleted.</param>
/// <param name="Operations">The identities of its operations, each in the catalogue.</param>
/// <param name="Assignable">The roles it lets its holder assign.</param>
/// <param name="Audit">Who created it — the installation, for a declared role — and who last changed it, and when.</param>
/// <remarks>
/// A role requires no second factor of its own: a role is powerful only by some of its operations, and the requirement
/// belongs to the elementary operation (<see cref="Operation.RequiresSecondFactor"/>, <see cref="CatalogEntry"/>).
/// </remarks>
public sealed record Role(
    string Name, RoleScope Scope, RoleCategory Category, string DisplayName, bool Declared, IReadOnlyCollection<string> Operations,
    AssignableRoles Assignable, Audit Audit);

/// <summary>What a role is, besides its name and its scope, which never change: what the creation and the change of a role take.</summary>
/// <param name="Category">What it is for.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Operations">The identities of its operations, each in the catalogue.</param>
/// <param name="Assignable">The roles it lets its holder assign; none unless given.</param>
public sealed record RoleDefinition(
    RoleCategory Category, string DisplayName, IReadOnlyCollection<string> Operations,
    AssignableRoles? Assignable = null);

/// <summary>
/// A role the application declares in code, which the installation records and re-applies: created when missing,
/// its definition reset when present. A role created by hand is never touched by a declaration. Its category is
/// stated, never defaulted; its assignable list is fixed by the code, as its operations are.
/// <code>
/// new RoleDeclaration("chef_atelier", RoleScope.Compartment, RoleCategory.Business, "Workshop manager",
///     [WorkOrderOperations.Read, WorkOrderOperations.Schedule], AssignableRoles.Of("operator"));
/// </code>
/// </summary>
/// <param name="Name">Its name, an identifier.</param>
/// <param name="Scope">Where it is held.</param>
/// <param name="Category">What it is for.</param>
/// <param name="DisplayName">What people call it.</param>
/// <param name="Operations">Its operations, each declared by a module recorded in the catalogue.</param>
/// <param name="Assignable">The roles it lets its holder assign; none unless given.</param>
public sealed record RoleDeclaration(
    string Name, RoleScope Scope, RoleCategory Category, string DisplayName, IReadOnlyCollection<Operation> Operations,
    AssignableRoles? Assignable = null)
{
    /// <summary>The identities of the operations.</summary>
    public IReadOnlyCollection<string> OperationIdentities => Operations.Select(operation => operation.Identity).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The definition the declaration gives the role.</summary>
    public RoleDefinition Definition => new(Category, DisplayName, OperationIdentities, Assignable);
}

/// <summary>The roles of a member in a compartment.</summary>
/// <param name="Subject">The member.</param>
/// <param name="Compartment">The compartment.</param>
/// <param name="Roles">The names of their compartment roles there.</param>
/// <param name="Granted">Who admitted them in the compartment, and when. Each role carries its own trace in the store.</param>
public sealed record Membership(string Subject, Compartment Compartment, IReadOnlyCollection<string> Roles, Stamp Granted);

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
