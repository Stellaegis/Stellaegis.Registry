using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry.SqlServer;

/// <summary>Registers the SQL Server stores of the registry.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server stores of the six ports, their options bound from <paramref name="sectionName"/>,
    /// along with the services of the module
    /// (<see cref="Registry.ServiceCollectionExtensions.AddStellaegisRegistry(IServiceCollection)"/>). The stores
    /// declare their database, <see cref="RegistryDatabase.Value"/>, which a host verifies at startup under the
    /// application's role with <c>AddStellaegisDatabaseVerification()</c>, and which an installer installs under an
    /// administration login.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistrySqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = RegistryDatabaseOptions.SectionName)
    {
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);

        services.AddStellaegisOptions<RegistryDatabaseOptions>(configuration, sectionName);
        return services.AddSqlServerStores();
    }

    /// <summary>
    /// Registers the SQL Server stores of the six ports and the services of the module, their options configured in
    /// code; otherwise as <see cref="AddStellaegisRegistrySqlServer(IServiceCollection, IConfiguration, string)"/>.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistrySqlServer(this IServiceCollection services, Action<RegistryDatabaseOptions> configure)
    {
        Guard.NotNull(configure);

        services.AddStellaegisOptions(configure);
        return services.AddSqlServerStores();
    }

    private static IServiceCollection AddSqlServerStores(this IServiceCollection services)
    {
        services.TryAddSingleton<SqlServerRegistryStore>();
        services.TryAddSingleton<ICompartmentStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IUserStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<ICatalogStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IRoleStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IMembershipStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IOperatorStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IInvitationStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.TryAddSingleton<IPendingActStore>(provider => provider.GetRequiredService<SqlServerRegistryStore>());
        services.AddStellaegisModuleDatabase(RegistryDatabase.Value,
            (provider, cancellationToken) => provider.GetRequiredService<SqlServerRegistryStore>().OpenConnectionAsync(cancellationToken));
        return services.AddStellaegisRegistry();
    }
}
