using Stellaegis.Primitives.Data;

namespace Stellaegis.Registry.PostgreSQL;

/// <summary>
/// The database of the registry module on PostgreSQL, as <c>Stellaegis.Primitives.Data</c> installs and verifies
/// it: the schema <c>reg</c> and its numbered scripts embedded in this package. No extension, no row-level
/// security: the module has no compartment, it acts on the compartments, and every row belongs to the application.
/// </summary>
/// <remarks>
/// Registered by <see cref="ServiceCollectionExtensions.AddStellaegisRegistryPostgreSQL(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, string)"/>,
/// so that a host verifies it at startup under the application's role (<c>AddStellaegisDatabaseVerification</c>).
/// An installer installs it under an administration role: <c>DatabaseLifecycle.InstallAsync([RegistryDatabase.Value], frame, …)</c>.
/// Also the marker of the database for the units of work of the store.
/// </remarks>
public sealed class RegistryDatabase
{
    /// <summary>The schema holding the module's tables, <c>reg</c>.</summary>
    public const string Schema = "reg";

    private RegistryDatabase()
    {
    }

    /// <summary>The declaration of the database.</summary>
    public static ModuleDatabase Value { get; } = ModuleDatabase.PostgreSql<RegistryModule>(Schema)
        .WithScripts(SqlScripts.FromResources(typeof(RegistryDatabase).Assembly, "Stellaegis.Registry.PostgreSQL.Sql.PostgreSql"));
}
