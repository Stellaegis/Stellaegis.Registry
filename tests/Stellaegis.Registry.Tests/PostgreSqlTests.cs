using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stellaegis.Primitives.Data;
using Stellaegis.Registry.PostgreSQL;

namespace Stellaegis.Registry.Tests;

/// <summary>The stores on PostgreSQL (<see cref="RegistryStoreTests"/>), and the grammars its schema checks.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlStoreTests(PostgreSqlFixture database) : RegistryStoreTests(database)
{
    private readonly PostgreSqlFixture _database = database;

    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => RegistryDatabase.Value;

    /// <inheritdoc/>
    protected override async Task AssertRefusedAsync(Func<Task> write, Refusal refusal)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(write);
        Assert.Equal(refusal switch
        {
            Refusal.Check => "23514",
            Refusal.MissingKey => "23503",
            Refusal.NotNull => "23502",
            Refusal.Duplicate => "23505",
            _ => "42501",
        }, exception.SqlState);
    }

    [Theory]
    [InlineData("insert into reg.compartments values ('Acme', 'x', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('ac--me', 'x', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'gone', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'active', now(), ' ', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'active', now(), 'job:tests', now(), null)", "23502")]
    [InlineData("insert into reg.users values ('jane', 'jane@acme.test', 'Jane', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.users values ('3f2504e04f8941d39a0c0305e82c3301', 'jane@acme.test', 'Jane', 'blocked', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.operations values ('items-read', 'inventory', 'read', false, false)", "23514")]
    [InlineData("insert into reg.operations values ('items.read', 'Inventory', 'read', false, false)", "23514")]
    [InlineData("insert into reg.operations values ('items.read', 'inventory', 'purge', false, false)", "23514")]
    [InlineData("insert into reg.operations values ('items.read', 'inventory', 'read', null, false)", "23502")]
    [InlineData("insert into reg.roles values ('tenant-administrator', 'compartment', 'administration', 'x', true, 'none', false, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('super__admin', 'platform', 'administration', 'x', true, 'all', true, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('reader', 'tenant', 'business', 'x', true, 'none', false, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('reader', 'compartment', 'boss', 'x', true, 'none', false, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('reader', 'compartment', 'business', 'x', true, 'some', false, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('reader', 'compartment', 'business', 'x', true, 'none', true, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.role_assignable_roles values ('nobody', 'nobody')", "23503")]
    [InlineData("insert into reg.pending_acts (act_id, kind, subject, role, proposed_at, proposed_by, expires_at) values (gen_random_uuid(), 'platform_role', null, 'ops', now(), 'job:tests', now() + interval '7 days')", "23514")]
    [InlineData("insert into reg.pending_acts (act_id, kind, role, proposed_at, proposed_by, expires_at) values (gen_random_uuid(), 'promotion', 'ops', now(), 'job:tests', now() + interval '7 days')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'active', now(), 'job:tests', now(), 'job:tests', 'jane')", "23514")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (gen_random_uuid(), 'ZZ', 'acme', 'a@b.c', false, 'email', now(), 'job:tests', now() + interval '1 day')", "23514")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (gen_random_uuid(), repeat('a', 64), 'acme', 'A@b.c', false, 'email', now(), 'job:tests', now() + interval '1 day')", "23514")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (gen_random_uuid(), repeat('a', 64), 'acme', 'a@b.c', false, 'sms', now(), 'job:tests', now() + interval '1 day')", "23514")]
    [InlineData("insert into reg.ownership_transfers values ('acme', '3f2504e04f8941d39a0c0305e82c3301', '3f2504e04f8941d39a0c0305e82c3301', now(), 'job:tests', now() + interval '7 days')", "23514")]
    [InlineData("insert into reg.ownership_transfers values ('nowhere', '3f2504e04f8941d39a0c0305e82c3301', '8d1e3b2a6c4f4a8e9b1d2f3e4a5b6c7d', now(), 'job:tests', now() + interval '7 days')", "23503")]
    [InlineData("insert into reg.role_operations values ('nobody', 'items.read')", "23503")]
    [InlineData("insert into reg.memberships values ('3f2504e04f8941d39a0c0305e82c3301', 'nowhere', now(), 'job:tests')", "23503")]
    [InlineData("insert into reg.operator_roles values ('3f2504e04f8941d39a0c0305e82c3301', 'nobody', now(), 'job:tests')", "23503")]
    public async Task The_schema_refuses_what_the_registry_would_never_produce(string sql, string sqlState)
    {
        _database.SkipWhenUnavailable();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => _database.ExecuteAsApplicationAsync(sql, Ct));

        Assert.Equal(sqlState, exception.SqlState);
    }
}

/// <summary>A host on the PostgreSQL stores (<see cref="RegistryHostTests"/>), its connection string under the application's role.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlHostTests(PostgreSqlFixture database) : RegistryHostTests(database)
{
    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => RegistryDatabase.Value;

    /// <inheritdoc/>
    protected override string ConnectionStringKey => "Stellaegis:Registry:PostgreSQL:ConnectionString";

    /// <inheritdoc/>
    protected override void AddRegistry(IServiceCollection services, IConfiguration configuration) => services.AddStellaegisRegistryPostgreSQL(configuration);

    /// <inheritdoc/>
    protected override void AddRegistry(IServiceCollection services, string connectionString)
        => services.AddStellaegisRegistryPostgreSQL(options => options.ConnectionString = connectionString);

    /// <inheritdoc/>
    protected override string RenameRecordSql(bool back) => back
        ? $"alter table {RegistryDatabase.Schema}._schema_migrations_gone rename to _schema_migrations"
        : $"alter table {RegistryDatabase.Value.MigrationsTable} rename to _schema_migrations_gone";
}

/// <summary>The scenarios of the projection replayed on the PostgreSQL stores, under the application's role.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlProjectionTests(PostgreSqlFixture database) : ProjectionTestsBase, IAsyncLifetime
{
    private protected override TestHost CreateHost(bool withSink = true)
    {
        database.SkipWhenUnavailable();
        return new TestHost(withSink, database.AddStores);
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
