using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Primitives;
using Stellaegis.Registry.InMemory;

namespace Stellaegis.Registry;

/// <summary>Registers the registry module.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services of the registry — the catalogue, the roles, the compartments, the user directory,
    /// the memberships, the operators — and the projector that hands the effective rights to the
    /// <see cref="IRightsSink"/> after every change, and declares the module to the application. The stores come
    /// from an adapter: <see cref="AddStellaegisRegistryInMemory"/>, or <c>Stellaegis.Registry.PostgreSQL</c>, which
    /// call this. The sink comes from the host, <see cref="AddStellaegisRightsSink{TSink}"/>; without one, the
    /// snapshots go nowhere and a warning says so once. Events go to the registered <see cref="IEventPublisher"/>,
    /// or nowhere when the host registers none.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistry(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.AddStellaegisRegistryModule();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher>(NullEventPublisher.Instance);
        services.TryAddScoped<IRightsSink, NullRightsSink>();
        services.TryAddScoped<RightsProjector>();
        services.TryAddScoped<AssignmentRules>();
        services.TryAddScoped<ICatalog, Catalog>();
        services.TryAddScoped<IRoles, Roles>();
        services.TryAddScoped<ICompartments, Compartments>();
        services.TryAddScoped<IUserDirectory, UserDirectory>();
        services.TryAddScoped<IMemberships, Memberships>();
        services.TryAddScoped<IOperators, Operators>();
        return services;
    }

    /// <summary>
    /// Registers the in-memory stores along with the services. For development and tests only: the registry is lost
    /// with the process.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryInMemory(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.TryAddSingleton<InMemoryRegistryData>();
        services.TryAddSingleton<ICompartmentStore, InMemoryCompartmentStore>();
        services.TryAddSingleton<IUserStore, InMemoryUserStore>();
        services.TryAddSingleton<ICatalogStore, InMemoryCatalogStore>();
        services.TryAddSingleton<IRoleStore, InMemoryRoleStore>();
        services.TryAddSingleton<IMembershipStore, InMemoryMembershipStore>();
        services.TryAddSingleton<IOperatorStore, InMemoryOperatorStore>();
        return services.AddStellaegisRegistry();
    }

    /// <summary>
    /// Registers where the effective rights go: the host's adapter on the projection of Access, in process or over
    /// HTTP. Call it before or after <see cref="AddStellaegisRegistry"/>; it replaces the sink that drops everything.
    /// </summary>
    public static IServiceCollection AddStellaegisRightsSink<TSink>(this IServiceCollection services) where TSink : class, IRightsSink
    {
        Guard.NotNull(services);
        services.RemoveAll<IRightsSink>();
        services.AddScoped<IRightsSink, TSink>();
        return services;
    }

    /// <summary>Registers an instance as where the effective rights go — a <see cref="RecordingRightsSink"/> in a test, for instance.</summary>
    public static IServiceCollection AddStellaegisRightsSink(this IServiceCollection services, IRightsSink sink)
    {
        Guard.NotNull(services);
        Guard.NotNull(sink);
        services.RemoveAll<IRightsSink>();
        services.AddSingleton(sink);
        return services;
    }
}
