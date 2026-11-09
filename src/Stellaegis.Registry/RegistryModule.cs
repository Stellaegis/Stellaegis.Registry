using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry;

/// <summary>
/// The identity of the registry module, <c>registry</c>: what its routes belong to, and what the application
/// declares it as. The registry is what gives a right its meaning — the compartments, the users, the catalogue of
/// operations, the roles, the memberships, the assignments — and computes the effective rights it hands to the
/// decision point. It has no compartments: it acts on them, from the shared space.
/// </summary>
public sealed class RegistryModule : IStellaegisModule
{
    /// <inheritdoc/>
    public static string Name => "registry";

    /// <summary>What the module declares to the application: no compartments, and the operations of its routes.</summary>
    public static ModuleDeclaration Declaration { get; } = ModuleDeclaration.WithoutCompartments<RegistryModule>()
        .WithOperations(RegistryOperations.All);
}

/// <summary>The operations of the registry module, which its administration routes are labelled with.</summary>
public static class RegistryOperations
{
    /// <summary>Reading the compartments, <c>compartments.read</c>.</summary>
    public static readonly Operation CompartmentsRead = new("compartments", "read", OperationKind.Read);

    /// <summary>Creating a compartment, with its first owner or without, <c>compartments.create</c>.</summary>
    public static readonly Operation CompartmentsCreate = new("compartments", "create", OperationKind.Write);

    /// <summary>Renaming a compartment, <c>compartments.rename</c>.</summary>
    public static readonly Operation CompartmentsRename = new("compartments", "rename", OperationKind.Write);

    /// <summary>Suspending a compartment and resuming it, <c>compartments.suspend</c>: its members lose every right while it is suspended.</summary>
    public static readonly Operation CompartmentsSuspend = new("compartments", "suspend", OperationKind.Write);

    /// <summary>Terminating a compartment, <c>compartments.terminate</c>: what follows is its purge.</summary>
    public static readonly Operation CompartmentsTerminate = new("compartments", "terminate", OperationKind.Write);

    /// <summary>
    /// Reading the directory of users, <c>directory.read</c>. The target is the directory, not <c>users</c>: an
    /// operation's identity is the key a rights system grants, across every module of a host, and
    /// <c>Stellaegis.Identity</c> already declares <c>users.read</c> for its accounts.
    /// </summary>
    public static readonly Operation DirectoryRead = new("directory", "read", OperationKind.Read);

    /// <summary>Deactivating a person in the directory and reactivating them, <c>directory.deactivate</c>: every right of the person goes, or comes back.</summary>
    public static readonly Operation DirectoryDeactivate = new("directory", "deactivate", OperationKind.Write);

    /// <summary>Reading the catalogue of operations, <c>catalog.read</c>.</summary>
    public static readonly Operation CatalogRead = new("catalog", "read", OperationKind.Read);

    /// <summary>Reading the roles, <c>roles.read</c>.</summary>
    public static readonly Operation RolesRead = new("roles", "read", OperationKind.Read);

    /// <summary>Creating a role, <c>roles.create</c>.</summary>
    public static readonly Operation RolesCreate = new("roles", "create", OperationKind.Write);

    /// <summary>
    /// Defining a role again — its operations, the roles it lets its holder assign, its name on screen —,
    /// <c>roles.define</c>: what its holders may do changes with it.
    /// </summary>
    public static readonly Operation RolesDefine = new("roles", "define", OperationKind.Write);

    /// <summary>Deleting a role nobody holds, <c>roles.delete</c>.</summary>
    public static readonly Operation RolesDelete = new("roles", "delete", OperationKind.Delete);

    /// <summary>Reading the memberships and the roles of the members, <c>memberships.read</c>.</summary>
    public static readonly Operation MembershipsRead = new("memberships", "read", OperationKind.Read);

    /// <summary>Making a person a member of a compartment, <c>memberships.add</c>.</summary>
    public static readonly Operation MembershipsAdd = new("memberships", "add", OperationKind.Write);

    /// <summary>Removing a member from a compartment, <c>memberships.remove</c>: every right they held there goes.</summary>
    public static readonly Operation MembershipsRemove = new("memberships", "remove", OperationKind.Delete);

    /// <summary>Assigning a compartment role to a member, <c>memberships.grant</c>, within the roles the author may assign.</summary>
    public static readonly Operation MembershipsGrant = new("memberships", "grant", OperationKind.Write);

    /// <summary>Unassigning a compartment role from a member, <c>memberships.revoke</c>, within the roles the author may assign.</summary>
    public static readonly Operation MembershipsRevoke = new("memberships", "revoke", OperationKind.Write);

    /// <summary>Reading the operators of the platform and their roles, <c>operators.read</c>.</summary>
    public static readonly Operation OperatorsRead = new("operators", "read", OperationKind.Read);

    /// <summary>Assigning a platform role, <c>operators.grant</c>: an act validated by a second channel.</summary>
    public static readonly Operation OperatorsGrant = new("operators", "grant", OperationKind.Write);

    /// <summary>Unassigning a platform role, <c>operators.revoke</c>: at once, since it takes rights away.</summary>
    public static readonly Operation OperatorsRevoke = new("operators", "revoke", OperationKind.Write);

    /// <summary>
    /// Sending an invitation, <c>invitations.send</c>: distinct from the memberships, since letting a person in from
    /// outside is not reorganizing the roles of the members, and sending e-mails is abused apart.
    /// </summary>
    public static readonly Operation InvitationsSend = new("invitations", "send", OperationKind.Write);

    /// <summary>Cancelling an invitation waiting, <c>invitations.cancel</c>.</summary>
    public static readonly Operation InvitationsCancel = new("invitations", "cancel", OperationKind.Write);

    /// <summary>Seating the owner of a compartment that has none, <c>ownership.designate</c>: an act of the platform.</summary>
    public static readonly Operation OwnershipDesignate = new("ownership", "designate", OperationKind.Write);

    /// <summary>
    /// Proposing, accepting, declining or cancelling a transfer of the ownership of a compartment, <c>ownership.transfer</c>.
    /// It opens the routes; the registry checks the author is the owner, or the recipient.
    /// </summary>
    public static readonly Operation OwnershipTransfer = new("ownership", "transfer", OperationKind.Write);

    /// <summary>Proposing the rescue of a compartment whose owner is unreachable, <c>ownership.rescue</c>: an act of the platform, validated by a second channel.</summary>
    public static readonly Operation OwnershipRescue = new("ownership", "rescue", OperationKind.Write);

    /// <summary>
    /// Deciding on screen an act waiting — validating or rejecting the assignment of a platform role, the definition of
    /// a role, the rescue of an ownership —, <c>validations.decide</c>: never one's own.
    /// </summary>
    public static readonly Operation ValidationsDecide = new("validations", "decide", OperationKind.Write);

    /// <summary>Every operation of the module.</summary>
    public static IReadOnlyList<Operation> All { get; } =
    [
        CompartmentsRead, CompartmentsCreate, CompartmentsRename, CompartmentsSuspend, CompartmentsTerminate,
        DirectoryRead, DirectoryDeactivate, CatalogRead, RolesRead, RolesCreate, RolesDefine, RolesDelete,
        MembershipsRead, MembershipsAdd, MembershipsRemove, MembershipsGrant, MembershipsRevoke,
        OperatorsRead, OperatorsGrant, OperatorsRevoke, InvitationsSend, InvitationsCancel,
        OwnershipDesignate, OwnershipTransfer, OwnershipRescue, ValidationsDecide,
    ];
}

/// <summary>Registers the registry module itself, whatever stores hold its records.</summary>
public static class RegistryModuleServiceCollectionExtensions
{
    /// <summary>
    /// Declares the registry module to the application (<see cref="RegistryModule.Declaration"/>). Every store's
    /// registration calls it; calling it again registers nothing more.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryModule(this IServiceCollection services)
    {
        Guard.NotNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistryModuleRegistered)))
            return services;
        services.AddSingleton(RegistryModuleRegistered.Instance);
        return services.AddStellaegisModule(RegistryModule.Declaration);
    }

    private sealed class RegistryModuleRegistered
    {
        public static readonly RegistryModuleRegistered Instance = new();
    }
}
