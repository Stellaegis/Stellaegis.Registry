using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry.SqlServer;

/// <summary>
/// The options of the SQL Server stores, bound from <see cref="SectionName"/> or configured in code, and validated
/// when the host starts.
/// <code>
/// Stellaegis:Registry:SqlServer:ConnectionString = Server=…;Database=…;Integrated Security=true
/// </code>
/// </summary>
public sealed class RegistryDatabaseOptions : IValidatableOptions
{
    /// <summary>The configuration section of the adapter, <c>Stellaegis:Registry:SqlServer</c>.</summary>
    public const string SectionName = "Stellaegis:Registry:SqlServer";

    /// <summary>
    /// The connection string to the database holding the <c>reg</c> schema, under a member of the application's role
    /// — the one the installation granted its data rights, never the login that owns the schema.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string? FindError()
        => string.IsNullOrWhiteSpace(ConnectionString) ? $"{nameof(ConnectionString)} is required." : null;
}
