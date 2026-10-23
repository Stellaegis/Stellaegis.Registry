using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry.PostgreSQL;

/// <summary>Registers the PostgreSQL stores of the registry.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL stores of the six ports, their options bound from <paramref name="sectionName"/>,
    /// along with the services of the module
    /// (<see cref="Registry.ServiceCollectionExtensions.AddStellaegisRegistry(IServiceCollection)"/>). The stores
    /// declare their database, <see cref="RegistryDatabase.Value"/>, which a host verifies at startup under the
    /// application's role with <c>AddStellaegisDatabaseVerification()</c>, and which an installer installs under an
    /// administration role.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryPostgreSQL(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = RegistryDatabaseOptions.SectionName)
    {
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);

        services.AddStellaegisOptions<RegistryDatabaseOptions>(configuration, sectionName);
        return services.AddPostgreSqlStores();
    }

    /// <summary>
    /// Registers the PostgreSQL stores of the six ports and the services of the module, their options configured in
    /// code; otherwise as <see cref="AddStellaegisRegistryPostgreSQL(IServiceCollection, IConfiguration, string)"/>.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryPostgreSQL(this IServiceCollection services, Action<RegistryDatabaseOptions> configure)
    {
        Guard.NotNull(configure);

        services.AddStellaegisOptions(configure);
        return services.AddPostgreSqlStores();
    }

    private static IServiceCollection AddPostgreSqlStores(this IServiceCollection services)
    {
        services.TryAddSingleton<PostgreSqlRegistryStore>();
        services.TryAddSingleton<ICompartmentStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IUserStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<ICatalogStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IRoleStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IMembershipStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IOperatorStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IInvitationStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.TryAddSingleton<IPendingActStore>(provider => provider.GetRequiredService<PostgreSqlRegistryStore>());
        services.AddStellaegisModuleDatabase(RegistryDatabase.Value,
            (provider, cancellationToken) => provider.GetRequiredService<PostgreSqlRegistryStore>().OpenConnectionAsync(cancellationToken));
        return services.AddStellaegisRegistry();
    }
}
