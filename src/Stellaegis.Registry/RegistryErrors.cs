using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>
/// The errors the registry answers, each with the code that lets the caller recognize it: in process through a
/// failed <see cref="Result"/>, over HTTP through the <c>code</c> of the problem details.
/// </summary>
public static class RegistryErrors
{
    /// <summary>The code of <see cref="CompartmentNotFound"/>.</summary>
    public const string CompartmentNotFoundCode = "Compartment.NotFound";

    /// <summary>The code of <see cref="CompartmentExists"/>.</summary>
    public const string CompartmentExistsCode = "Compartment.Exists";

    /// <summary>The code of <see cref="CompartmentTerminated"/>.</summary>
    public const string CompartmentTerminatedCode = "Compartment.Terminated";

    /// <summary>The code of <see cref="UserNotFound"/>.</summary>
    public const string UserNotFoundCode = "User.NotFound";

    /// <summary>The code of <see cref="UserDeactivated"/>.</summary>
    public const string UserDeactivatedCode = "User.Deactivated";

    /// <summary>The code of <see cref="RoleNotFound"/>.</summary>
    public const string RoleNotFoundCode = "Role.NotFound";

    /// <summary>The code of <see cref="RoleExists"/>.</summary>
    public const string RoleExistsCode = "Role.Exists";

    /// <summary>The code of <see cref="RoleScopeMismatch"/>.</summary>
    public const string RoleScopeMismatchCode = "Role.ScopeMismatch";

    /// <summary>The code of <see cref="RoleDeclared"/>.</summary>
    public const string RoleDeclaredCode = "Role.Declared";

    /// <summary>The code of <see cref="RoleNotDeclared"/>.</summary>
    public const string RoleNotDeclaredCode = "Role.NotDeclared";

    /// <summary>The code of <see cref="RoleInUse"/>.</summary>
    public const string RoleInUseCode = "Role.InUse";

    /// <summary>The code of <see cref="RoleNameInvalid"/>.</summary>
    public const string RoleNameInvalidCode = "Role.NameInvalid";

    /// <summary>The code of <see cref="RoleAssignsPlatform"/>.</summary>
    public const string RoleAssignsPlatformCode = "Role.AssignsPlatform";

    /// <summary>The code of <see cref="RoleHeldByAuthor"/>.</summary>
    public const string RoleHeldByAuthorCode = "Role.HeldByAuthor";

    /// <summary>The code of <see cref="RoleNamedAssignable"/>.</summary>
    public const string RoleNamedAssignableCode = "Role.NamedAssignable";

    /// <summary>The code of <see cref="RoleNotAssignable"/>.</summary>
    public const string RoleNotAssignableCode = "Role.NotAssignable";

    /// <summary>The code of <see cref="OwnRoles"/>.</summary>
    public const string OwnRolesCode = "Role.OwnRoles";

    /// <summary>The code of <see cref="UnknownOperation"/>.</summary>
    public const string UnknownOperationCode = "Catalog.UnknownOperation";

    /// <summary>The code of <see cref="NotMember"/>.</summary>
    public const string NotMemberCode = "Membership.NotMember";

    /// <summary>The compartment is not in the registry.</summary>
    public static Error CompartmentNotFound(Compartment compartment)
        => Error.NotFound(CompartmentNotFoundCode, $"Compartment '{compartment}' is not in the registry.");

    /// <summary>A compartment of that name exists, or existed: a name is never reused.</summary>
    public static Error CompartmentExists(Compartment compartment)
        => Error.Conflict(CompartmentExistsCode, $"Compartment '{compartment}' already exists; a name is never reused.");

    /// <summary>The compartment is terminated: nothing changes in it any more.</summary>
    public static Error CompartmentTerminated(Compartment compartment)
        => Error.PreconditionFailed(CompartmentTerminatedCode, $"Compartment '{compartment}' is terminated.");

    /// <summary>The user is not in the registry.</summary>
    public static Error UserNotFound(string subject)
        => Error.NotFound(UserNotFoundCode, $"User '{subject}' is not in the registry.");

    /// <summary>The user is deactivated: nothing is granted to them any more.</summary>
    public static Error UserDeactivated(string subject)
        => Error.PreconditionFailed(UserDeactivatedCode, $"User '{subject}' is deactivated.");

    /// <summary>The role is not in the registry.</summary>
    public static Error RoleNotFound(string role)
        => Error.NotFound(RoleNotFoundCode, $"Role '{role}' is not in the registry.");

    /// <summary>A role of that name exists.</summary>
    public static Error RoleExists(string role)
        => Error.Conflict(RoleExistsCode, $"Role '{role}' already exists.");

    /// <summary>The role is of the other scope: a compartment role is assigned to a member, a platform role to an operator.</summary>
    public static Error RoleScopeMismatch(string role, RoleScope expected)
        => Error.Validation(RoleScopeMismatchCode, $"Role '{role}' is not a {(expected == RoleScope.Compartment ? "compartment" : "platform")} role.");

    /// <summary>The role is declared by the application: the installation re-applies it, and it cannot be deleted.</summary>
    public static Error RoleDeclared(string role)
        => Error.PreconditionFailed(RoleDeclaredCode, $"Role '{role}' is declared by the application and cannot be deleted.");

    /// <summary>A role of that name was created by hand: a declaration cannot take it over.</summary>
    public static Error RoleNotDeclared(string role)
        => Error.Conflict(RoleNotDeclaredCode, $"Role '{role}' exists and was not declared; rename the declaration or delete the role.");

    /// <summary>The role is held by someone: unassign it before deleting it.</summary>
    public static Error RoleInUse(string role)
        => Error.PreconditionFailed(RoleInUseCode, $"Role '{role}' is assigned; unassign it before deleting it.");

    /// <summary>The name of a role follows the grammar of an identifier.</summary>
    public static Error RoleNameInvalid(string role)
        => Error.Validation(RoleNameInvalidCode, $"'{role}' is not a role name: {NameRules.IdentifierRule}.");

    /// <summary>A compartment role lets its holder assign only compartment roles: a member never makes an operator of the platform.</summary>
    public static Error RoleAssignsPlatform(string role)
        => Error.Validation(RoleAssignsPlatformCode, $"Role '{role}' is a compartment role: it cannot let its holder assign a platform role.");

    /// <summary>The author of the change holds the role: nobody changes the definition of a role they hold, which would give them whatever they put in it.</summary>
    public static Error RoleHeldByAuthor(string role)
        => Error.Forbidden(RoleHeldByAuthorCode, $"Role '{role}' is held by the author of the change: nobody changes the definition of a role they hold.");

    /// <summary>Another role names this one in its assignable list: remove it from there before deleting it.</summary>
    public static Error RoleNamedAssignable(string role, string by)
        => Error.PreconditionFailed(RoleNamedAssignableCode, $"Role '{role}' is named in the assignable list of role '{by}'; remove it from there before deleting it.");

    /// <summary>
    /// The role is in the assignable list of no role the actor holds: assigning a role is a right of its own, bounded by
    /// that list. Unassigning it, or removing a member who holds it, follows the same rule.
    /// </summary>
    public static Error RoleNotAssignable(string role)
        => Error.Forbidden(RoleNotAssignableCode, $"Role '{role}' is in the assignable list of no role its author holds.");

    /// <summary>Nobody changes their own memberships or roles, in either direction: who wants to drop a role asks for it.</summary>
    public static Error OwnRoles(string subject)
        => Error.Forbidden(OwnRolesCode, $"User '{subject}' is the author of the change: nobody changes their own memberships or roles.");

    /// <summary>The operation is not in the catalogue: no module declares it.</summary>
    public static Error UnknownOperation(string operation)
        => Error.Validation(UnknownOperationCode, $"Operation '{operation}' is not in the catalogue: no module declares it.");

    /// <summary>The user is not a member of the compartment.</summary>
    public static Error NotMember(string subject, Compartment compartment)
        => Error.PreconditionFailed(NotMemberCode, $"User '{subject}' is not a member of '{compartment}'.");
}
