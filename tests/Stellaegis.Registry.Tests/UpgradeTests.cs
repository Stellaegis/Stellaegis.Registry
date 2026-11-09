using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using PostgreSqlDatabase = Stellaegis.Registry.PostgreSQL.RegistryDatabase;
using SqlServerDatabase = Stellaegis.Registry.SqlServer.RegistryDatabase;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// A database installed by Registry 6 — the script 0001 alone — takes the script 0002 by running the installation
/// again, as an installer upgrading does, and refuses the application until it has; an act waiting stays as it was.
/// Each engine in a database of its own, created and dropped by the test.
/// </summary>
public abstract class UpgradeTests
{
    /// <summary>The variable naming the server; the test is skipped without it.</summary>
    protected abstract string Variable { get; }

    /// <summary>The full declaration of the engine.</summary>
    protected abstract ModuleDatabase Declaration { get; }

    /// <summary>The same declaration, as Registry 6 shipped it: the script 0001 alone.</summary>
    protected abstract ModuleDatabase Version6 { get; }

    /// <summary>The expression of the current time, and of a week later, in the SQL of the engine.</summary>
    protected abstract (string Now, string Later) Times { get; }

    /// <summary>Creates the database of the test on the server, dropping one left by an earlier run.</summary>
    protected abstract Task CreateDatabaseAsync(string server);

    /// <summary>Drops the database of the test.</summary>
    protected abstract Task DropDatabaseAsync(string server);

    /// <summary>Opens a connection to the database of the test under the installer.</summary>
    protected abstract Task<DbConnection> OpenAsync(string server, CancellationToken cancellationToken);

    /// <summary>The role of the application the installation grants.</summary>
    protected const string AppRole = "stellaegis_registry_app";

    /// <summary>The database of the test.</summary>
    protected const string Database = "stellaegis_registry_upgrade_tests";

    [Fact]
    public async Task A_database_of_Registry_6_takes_the_script_0002_by_installing_again()
    {
        var server = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(server), $"{Variable} is not set.");
        var ct = TestContext.Current.CancellationToken;
        Task<DbConnection> Open(ModuleDatabase _, CancellationToken token) => OpenAsync(server!, token);
        var (now, later) = Times;

        await CreateDatabaseAsync(server!);
        try
        {
            await DatabaseLifecycle.InstallAsync([Version6], Fixtures.Frame, Open, AppRole, Actor.Job("tests"), ct);
            await ExecuteAsync(server!, "insert into reg.users (subject, email, display_name, status, created_at, created_by, modified_at, modified_by) "
                + $"values ('{Fixtures.Jane}', 'jane@acme.test', 'Jane', 'active', {now}, 'job:tests', {now}, 'job:tests')", ct);
            await ExecuteAsync(server!, "insert into reg.pending_acts (act_id, kind, subject, role, proposed_at, proposed_by, expires_at) "
                + $"values ('{Guid.NewGuid()}', 'platform_role', '{Fixtures.Jane}', 'super_admin', {now}, 'job:tests', {later})", ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseLifecycle.VerifyAsync([Declaration], Fixtures.Frame, Open, ct));

            var report = await DatabaseLifecycle.InstallAsync([Declaration], Fixtures.Frame, Open, AppRole, Actor.Job("tests"), ct);
            Assert.Equal(["registry: 0002_reg.sql"], report.Applied);
            await DatabaseLifecycle.VerifyAsync([Declaration], Fixtures.Frame, Open, ct);

            // The act waiting is there as it was; a hand delivery is one now.
            Assert.Equal(1, await CountAsync(server!, "select count(*) from reg.pending_acts where consumed_at is null", ct));
            await ExecuteAsync(server!, "insert into reg.pending_acts (act_id, kind, subject, proposed_at, proposed_by, expires_at) "
                + $"values ('{Guid.NewGuid()}', 'hand_delivery', '{Fixtures.Jane}', {now}, 'job:tests', {later})", ct);
            Assert.Equal(2, await CountAsync(server!, "select count(*) from reg.pending_acts", ct));
        }
        finally
        {
            await DropDatabaseAsync(server!);
        }
    }

    private async Task ExecuteAsync(string server, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(server, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> CountAsync(string server, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(server, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Runs <paramref name="sql"/> on a connection that is closed after it.</summary>
    protected static async Task RunAsync(DbConnection connection, string sql)
    {
        await using (connection)
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>The upgrade on PostgreSQL (<see cref="UpgradeTests"/>).</summary>
public sealed class PostgreSqlUpgradeTests : UpgradeTests
{
    /// <inheritdoc/>
    protected override string Variable => "STELLAEGIS_TEST_POSTGRESQL";

    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => PostgreSqlDatabase.Value;

    /// <inheritdoc/>
    protected override ModuleDatabase Version6 { get; } = ModuleDatabase.PostgreSql<RegistryModule>(PostgreSqlDatabase.Schema)
        .WithScripts(PostgreSqlDatabase.Value.Scripts.Take(1));

    /// <inheritdoc/>
    protected override (string Now, string Later) Times => ("now()", "now() + interval '7 days'");

    /// <inheritdoc/>
    protected override async Task CreateDatabaseAsync(string server)
    {
        await RunAsync(new NpgsqlConnection(server), $"drop database if exists \"{Database}\" with (force)");
        await RunAsync(new NpgsqlConnection(server), $"create database \"{Database}\"");
        await RunAsync(new NpgsqlConnection(server), $"""
            do $$ begin
              create role {AppRole} login password '{AppRole}';
            exception when duplicate_object or unique_violation then null;
            end $$;
            """);
    }

    /// <inheritdoc/>
    protected override Task DropDatabaseAsync(string server)
        => RunAsync(new NpgsqlConnection(server), $"drop database if exists \"{Database}\" with (force)");

    /// <inheritdoc/>
    protected override async Task<DbConnection> OpenAsync(string server, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = Database, Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

/// <summary>The upgrade on SQL Server (<see cref="UpgradeTests"/>).</summary>
public sealed class SqlServerUpgradeTests : UpgradeTests
{
    /// <inheritdoc/>
    protected override string Variable => "STELLAEGIS_TEST_SQLSERVER";

    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => SqlServerDatabase.Value;

    /// <inheritdoc/>
    protected override ModuleDatabase Version6 { get; } = ModuleDatabase.SqlServer<RegistryModule>(SqlServerDatabase.Schema)
        .WithScripts(SqlServerDatabase.Value.Scripts.Take(1));

    /// <inheritdoc/>
    protected override (string Now, string Later) Times => ("sysdatetimeoffset()", "dateadd(day, 7, sysdatetimeoffset())");

    /// <inheritdoc/>
    protected override async Task CreateDatabaseAsync(string server)
    {
        await DropDatabaseAsync(server);
        await RunAsync(Master(server), $"create database [{Database}] collate French_100_CI_AS");
    }

    /// <inheritdoc/>
    protected override Task DropDatabaseAsync(string server)
        => RunAsync(Master(server), $"""
            if db_id(N'{Database}') is not null
            begin
              alter database [{Database}] set single_user with rollback immediate;
              drop database [{Database}];
            end
            """);

    /// <inheritdoc/>
    protected override async Task<DbConnection> OpenAsync(string server, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = Database, Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static SqlConnection Master(string server)
        => new(new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false }.ConnectionString);
}
