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

    /// <summary>The code of <see cref="RoleReserved"/>.</summary>
    public const string RoleReservedCode = "Role.Reserved";

    /// <summary>The code of <see cref="OwnerUntouchable"/>.</summary>
    public const string OwnerUntouchableCode = "Owner.Untouchable";

    /// <summary>The code of <see cref="OwnerSeated"/>.</summary>
    public const string OwnerSeatedCode = "Owner.Seated";

    /// <summary>The code of <see cref="NotOwner"/>.</summary>
    public const string NotOwnerCode = "Owner.NotOwner";

    /// <summary>The code of <see cref="RoleNotHeld"/>.</summary>
    public const string RoleNotHeldCode = "Role.NotHeld";

    /// <summary>The code of <see cref="TransferNotFound"/>.</summary>
    public const string TransferNotFoundCode = "Transfer.NotFound";

    /// <summary>The code of <see cref="TransferToSelf"/>.</summary>
    public const string TransferToSelfCode = "Transfer.ToSelf";

    /// <summary>The code of <see cref="NotRecipient"/>.</summary>
    public const string NotRecipientCode = "Transfer.NotRecipient";

    /// <summary>The code of <see cref="TransferExpired"/>.</summary>
    public const string TransferExpiredCode = "Transfer.Expired";

    /// <summary>The code of <see cref="TransferStale"/>.</summary>
    public const string TransferStaleCode = "Transfer.Stale";

    /// <summary>The code of <see cref="CompartmentNotActive"/>.</summary>
    public const string CompartmentNotActiveCode = "Compartment.NotActive";

    /// <summary>The code of <see cref="InvitationEmailInvalid"/>.</summary>
    public const string InvitationEmailInvalidCode = "Invitation.EmailInvalid";

    /// <summary>The code of <see cref="HandDeliveryClosed"/>.</summary>
    public const string HandDeliveryClosedCode = "Invitation.HandDeliveryClosed";

    /// <summary>The code of <see cref="NoInvitationSender"/>.</summary>
    public const string NoInvitationSenderCode = "Invitation.NoSender";

    /// <summary>The code of <see cref="AlreadyMember"/>.</summary>
    public const string AlreadyMemberCode = "Invitation.AlreadyMember";

    /// <summary>The code of <see cref="InvitationNotFound"/>.</summary>
    public const string InvitationNotFoundCode = "Invitation.NotFound";

    /// <summary>The code of <see cref="InvitationNotPending"/>.</summary>
    public const string InvitationNotPendingCode = "Invitation.NotPending";

    /// <summary>The code of <see cref="InvitationInvalid"/>.</summary>
    public const string InvitationInvalidCode = "Invitation.Invalid";

    /// <summary>The code of <see cref="InvitationStale"/>.</summary>
    public const string InvitationStaleCode = "Invitation.Stale";

    /// <summary>The code of <see cref="ActPending"/>.</summary>
    public const string ActPendingCode = "Act.Pending";

    /// <summary>The code of <see cref="ActNotFound"/>.</summary>
    public const string ActNotFoundCode = "Act.NotFound";

    /// <summary>The code of <see cref="ActClosed"/>.</summary>
    public const string ActClosedCode = "Act.Closed";

    /// <summary>The code of <see cref="ActExpired"/>.</summary>
    public const string ActExpiredCode = "Act.Expired";

    /// <summary>The code of <see cref="OwnAct"/>.</summary>
    public const string OwnActCode = "Act.Own";

    /// <summary>The code of <see cref="NotValidator"/>.</summary>
    public const string NotValidatorCode = "Act.NotValidator";

    /// <summary>The code of <see cref="CatalogInstallationOnly"/>.</summary>
    public const string CatalogInstallationOnlyCode = "Catalog.InstallationOnly";

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

    /// <summary>The role is reserved to the registry: the role of the owner is seated and transferred with the ownership, never created, assigned nor unassigned.</summary>
    public static Error RoleReserved(string role)
        => Error.Forbidden(RoleReservedCode, $"Role '{role}' is reserved: it goes with the ownership of a compartment, seated or transferred, never assigned.");

    /// <summary>The user owns the compartment: nobody else changes their membership or roles there; a platform's rescue alone replaces an owner.</summary>
    public static Error OwnerUntouchable(string subject, Compartment compartment)
        => Error.Forbidden(OwnerUntouchableCode, $"User '{subject}' owns '{compartment}': nobody else changes their membership or roles there.");

    /// <summary>The compartment has an owner already: it changes by a transfer only.</summary>
    public static Error OwnerSeated(Compartment compartment)
        => Error.Conflict(OwnerSeatedCode, $"Compartment '{compartment}' has an owner already; the ownership changes by a transfer only.");

    /// <summary>The author of the change is not the owner of the compartment.</summary>
    public static Error NotOwner(Compartment compartment)
        => Error.Forbidden(NotOwnerCode, $"The author of the change is not the owner of '{compartment}'.");

    /// <summary>The outgoing owner would keep a role they do not hold.</summary>
    public static Error RoleNotHeld(string role)
        => Error.Validation(RoleNotHeldCode, $"Role '{role}' is not held by the owner in the compartment: only a role held can be kept.");

    /// <summary>No transfer of the ownership of the compartment is waiting.</summary>
    public static Error TransferNotFound(Compartment compartment)
        => Error.NotFound(TransferNotFoundCode, $"No transfer of the ownership of '{compartment}' is waiting.");

    /// <summary>The owner proposed the ownership to themselves.</summary>
    public static Error TransferToSelf(Compartment compartment)
        => Error.Validation(TransferToSelfCode, $"The owner of '{compartment}' cannot transfer it to themselves.");

    /// <summary>The author of the answer is not the recipient of the transfer.</summary>
    public static Error NotRecipient(Compartment compartment)
        => Error.Forbidden(NotRecipientCode, $"The author of the answer is not the recipient of the transfer of '{compartment}'.");

    /// <summary>The transfer lapsed, unanswered: the owner proposes it again.</summary>
    public static Error TransferExpired(Compartment compartment)
        => Error.PreconditionFailed(TransferExpiredCode, $"The transfer of the ownership of '{compartment}' lapsed unanswered.");

    /// <summary>What the transfer assumed no longer holds: the proposer is no longer the owner, the recipient no longer an active member, or the compartment no longer active.</summary>
    public static Error TransferStale(Compartment compartment, string why)
        => Error.PreconditionFailed(TransferStaleCode, $"The transfer of the ownership of '{compartment}' no longer holds: {why}.");

    /// <summary>The compartment is suspended or terminated: nobody is let in.</summary>
    public static Error CompartmentNotActive(Compartment compartment)
        => Error.PreconditionFailed(CompartmentNotActiveCode, $"Compartment '{compartment}' is not active.");

    /// <summary>The address is not one an invitation can go to.</summary>
    public static Error InvitationEmailInvalid(string email)
        => Error.Validation(InvitationEmailInvalidCode, $"'{email}' is not an e-mail address.");

    /// <summary>The host does not declare the hand delivery: an invitation is sent by e-mail.</summary>
    public static Error HandDeliveryClosed()
        => Error.Forbidden(HandDeliveryClosedCode, "The host does not let an invitation be handed over: it is sent by e-mail.");

    /// <summary>The host has no sender of invitations: one is sent by e-mail only through an <see cref="IInvitationSender"/>.</summary>
    public static Error NoInvitationSender()
        => Error.PreconditionFailed(NoInvitationSenderCode, "The host registers no IInvitationSender: an invitation cannot be sent by e-mail.");

    /// <summary>The address belongs to a member of the compartment already: their roles change by a direct assignment.</summary>
    public static Error AlreadyMember(string email, Compartment compartment)
        => Error.Conflict(AlreadyMemberCode, $"'{email}' is a member of '{compartment}' already: assign the roles directly.");

    /// <summary>The invitation is not in the registry.</summary>
    public static Error InvitationNotFound()
        => Error.NotFound(InvitationNotFoundCode, "The invitation is not in the registry.");

    /// <summary>The invitation is accepted or revoked already.</summary>
    public static Error InvitationNotPending()
        => Error.PreconditionFailed(InvitationNotPendingCode, "The invitation is accepted or revoked already.");

    /// <summary>The token opens nothing: unknown, spent, revoked or lapsed — which the answer never tells apart.</summary>
    public static Error InvitationInvalid()
        => Error.NotFound(InvitationInvalidCode, "The invitation is invalid or has lapsed.");

    /// <summary>What the invitation assumed no longer holds: it does not serve, and nothing is applied.</summary>
    public static Error InvitationStale(string why)
        => Error.PreconditionFailed(InvitationStaleCode, $"The invitation no longer holds: {why}.");

    /// <summary>An act on the same target waits already: it is validated, rejected or lapses first.</summary>
    public static Error ActPending(Guid actId)
        => Error.Conflict(ActPendingCode, $"An act on the same target waits for its validation already: {actId}.");

    /// <summary>The act is not in the registry.</summary>
    public static Error ActNotFound()
        => Error.NotFound(ActNotFoundCode, "The act is not in the registry.");

    /// <summary>The act was validated or rejected already.</summary>
    public static Error ActClosed()
        => Error.PreconditionFailed(ActClosedCode, "The act was validated or rejected already.");

    /// <summary>The act lapsed, unvalidated: it is proposed again.</summary>
    public static Error ActExpired()
        => Error.PreconditionFailed(ActExpiredCode, "The act lapsed unvalidated; propose it again.");

    /// <summary>Nobody validates their own act on screen: the validate verb of the host is the other channel.</summary>
    public static Error OwnAct()
        => Error.Forbidden(OwnActCode, "Nobody validates their own act on screen; the validate verb of the host is the other channel.");

    /// <summary>The author of the validation does not hold validations.write by a platform role.</summary>
    public static Error NotValidator()
        => Error.Forbidden(NotValidatorCode, "The author of the validation holds no platform role carrying validations.write.");

    /// <summary>The catalogue is written by the installation only.</summary>
    public static Error CatalogInstallationOnly()
        => Error.Forbidden(CatalogInstallationOnlyCode, "The catalogue is written by the installation only.");

    /// <summary>The operation is not in the catalogue: no module declares it.</summary>
    public static Error UnknownOperation(string operation)
        => Error.Validation(UnknownOperationCode, $"Operation '{operation}' is not in the catalogue: no module declares it.");

    /// <summary>The user is not a member of the compartment.</summary>
    public static Error NotMember(string subject, Compartment compartment)
        => Error.PreconditionFailed(NotMemberCode, $"User '{subject}' is not a member of '{compartment}'.");
}
