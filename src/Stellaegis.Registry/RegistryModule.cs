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

    /// <summary>Creating, renaming, suspending or terminating a compartment, <c>compartments.write</c>.</summary>
    public static readonly Operation CompartmentsWrite = new("compartments", "write", OperationKind.Write);

    /// <summary>
    /// Reading the directory of users, <c>directory.read</c>. The target is the directory, not <c>users</c>: an
    /// operation's identity is the key a rights system grants, across every module of a host, and
    /// <c>Stellaegis.Identity</c> already declares <c>users.read</c> for its accounts.
    /// </summary>
    public static readonly Operation DirectoryRead = new("directory", "read", OperationKind.Read);

    /// <summary>Deactivating or reactivating a user in the directory, <c>directory.write</c>.</summary>
    public static readonly Operation DirectoryWrite = new("directory", "write", OperationKind.Write);

    /// <summary>Reading the catalogue of operations, <c>catalog.read</c>.</summary>
    public static readonly Operation CatalogRead = new("catalog", "read", OperationKind.Read);

    /// <summary>Reading the roles, <c>roles.read</c>.</summary>
    public static readonly Operation RolesRead = new("roles", "read", OperationKind.Read);

    /// <summary>Creating, changing or deleting a role, <c>roles.write</c>.</summary>
    public static readonly Operation RolesWrite = new("roles", "write", OperationKind.Write);

    /// <summary>Reading the memberships and the roles of the members, <c>memberships.read</c>.</summary>
    public static readonly Operation MembershipsRead = new("memberships", "read", OperationKind.Read);

    /// <summary>Adding or removing a member, assigning or unassigning a compartment role, <c>memberships.write</c>.</summary>
    public static readonly Operation MembershipsWrite = new("memberships", "write", OperationKind.Write);

    /// <summary>Reading the operators of the platform and their roles, <c>operators.read</c>.</summary>
    public static readonly Operation OperatorsRead = new("operators", "read", OperationKind.Read);

    /// <summary>Assigning or unassigning a platform role, <c>operators.write</c>.</summary>
    public static readonly Operation OperatorsWrite = new("operators", "write", OperationKind.Write);

    /// <summary>
    /// Proposing, accepting, declining or cancelling a transfer of the ownership of a compartment, <c>ownership.write</c>.
    /// It opens the routes; the registry checks the author is the owner, or the recipient.
    /// </summary>
    public static readonly Operation OwnershipWrite = new("ownership", "write", OperationKind.Write);

    /// <summary>Every operation of the module.</summary>
    public static IReadOnlyList<Operation> All { get; } =
    [
        CompartmentsRead, CompartmentsWrite, DirectoryRead, DirectoryWrite, CatalogRead,
        RolesRead, RolesWrite, MembershipsRead, MembershipsWrite, OperatorsRead, OperatorsWrite, OwnershipWrite,
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
