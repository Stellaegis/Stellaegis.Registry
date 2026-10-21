using Stellaegis.Registry.PostgreSQL;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The scenarios of the projection replayed on the PostgreSQL stores, under the application's role: every change
/// read at the recording sink exactly as in memory.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlProjectionTests(PostgreSqlFixture database) : ProjectionTestsBase, IAsyncLifetime
{
    private protected override TestHost CreateHost(bool withSink = true)
    {
        database.SkipWhenUnavailable();
        return new TestHost(withSink, services => services.AddStellaegisRegistryPostgreSQL(options => options.ConnectionString = database.AppConnectionString));
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        if (database.Unavailable is null)
            await database.ClearAsync(Ct);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
