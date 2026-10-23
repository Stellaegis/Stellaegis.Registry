using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.Data;
using Stellaegis.Registry.SqlServer;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The stores on SQL Server (<see cref="RegistryStoreTests"/>), in a case-insensitive database, their connections
/// running as the application's user; and the grammars its schema checks.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerStoreTests(SqlServerFixture database) : RegistryStoreTests(database)
{
    private readonly SqlServerFixture _database = database;

    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => RegistryDatabase.Value;

    /// <inheritdoc/>
    protected override async Task AssertRefusedAsync(Func<Task> write, Refusal refusal)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(write);
        switch (refusal)
        {
            case Refusal.Duplicate:
                Assert.Contains(exception.Number, new[] { 2627, 2601 });   // a key, a unique index
                break;
            case Refusal.MissingKey:
                Assert.Equal(547, exception.Number);
                Assert.Contains("\"fk_", exception.Message);   // the message speaks the language of the server; the name of the constraint is ours
                break;
            case Refusal.Check:
                Assert.Equal(547, exception.Number);
                Assert.Contains("\"ck_", exception.Message);
                break;
            case Refusal.NotNull:
                Assert.Equal(515, exception.Number);
                break;
            default:
                Assert.Equal(229, exception.Number);   // the permission was denied
                break;
        }
    }

    // Each statement breaks one rule, the one named: its constraint, or the column a value is missing from.
    [Theory]
    [InlineData("insert into reg.compartments values (N'Acme', N'x', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests', null)", "ck_compartments_compartment")]
    [InlineData("insert into reg.compartments values (N'ac--me', N'x', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests', null)", "ck_compartments_compartment")]
    [InlineData("insert into reg.compartments values (N'acme', N'x', N'gone', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests', null)", "ck_compartments_status")]
    [InlineData("insert into reg.compartments values (N'acme', N'x', N'Active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests', null)", "ck_compartments_status")]
    [InlineData("insert into reg.compartments values (N'acme', N'x', N'active', sysdatetimeoffset(), N' ', sysdatetimeoffset(), N'job:tests', null)", "ck_compartments_created_by")]
    [InlineData("insert into reg.compartments values (N'acme', N'x', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), null, null)", "modified_by")]
    [InlineData("insert into reg.compartments values (N'acme', N'x', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests', N'jane')", "ck_compartments_owner_subject")]
    [InlineData("insert into reg.users values (N'jane', N'jane@acme.test', N'Jane', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_users_subject")]
    [InlineData("insert into reg.users values (N'3F2504E04F8941D39A0C0305E82C3301', N'jane@acme.test', N'Jane', N'active', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_users_subject")]
    [InlineData("insert into reg.users values (N'3f2504e04f8941d39a0c0305e82c3301', N'jane@acme.test', N'Jane', N'blocked', sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_users_status")]
    [InlineData("insert into reg.operations values (N'items-read', N'inventory', N'read', 0, 0)", "ck_operations_operation")]
    [InlineData("insert into reg.operations values (N'items.Read', N'inventory', N'read', 0, 0)", "ck_operations_operation")]
    [InlineData("insert into reg.operations values (N'items.read_', N'inventory', N'read', 0, 0)", "ck_operations_operation")]
    [InlineData("insert into reg.operations values (N'items.read', N'Inventory', N'read', 0, 0)", "ck_operations_module")]
    [InlineData("insert into reg.operations values (N'items.read', N'inventory', N'purge', 0, 0)", "ck_operations_kind")]
    [InlineData("insert into reg.operations values (N'items.read', N'inventory', N'read', null, 0)", "declared_second_factor")]
    [InlineData("insert into reg.roles values (N'tenant-administrator', N'compartment', N'administration', N'x', 1, N'none', 0, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_role")]
    [InlineData("insert into reg.roles values (N'super__admin', N'platform', N'administration', N'x', 1, N'all', 1, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_role")]
    [InlineData("insert into reg.roles values (N'Reader', N'compartment', N'business', N'x', 1, N'none', 0, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_role")]
    [InlineData("insert into reg.roles values (N'reader', N'tenant', N'business', N'x', 1, N'none', 0, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_scope")]
    [InlineData("insert into reg.roles values (N'reader', N'compartment', N'boss', N'x', 1, N'none', 0, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_category")]
    [InlineData("insert into reg.roles values (N'reader', N'compartment', N'business', N'x', 1, N'some', 0, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_assigns_compartment")]
    [InlineData("insert into reg.roles values (N'reader', N'compartment', N'business', N'x', 1, N'none', 1, sysdatetimeoffset(), N'job:tests', sysdatetimeoffset(), N'job:tests')", "ck_roles_assigns_platform")]
    [InlineData("insert into reg.role_assignable_roles values (N'nobody', N'nobody')", "fk_role_assignable_roles_")]
    [InlineData("insert into reg.role_operations values (N'nobody', N'items.read')", "fk_role_operations_")]
    [InlineData("insert into reg.memberships values (N'3f2504e04f8941d39a0c0305e82c3301', N'nowhere', sysdatetimeoffset(), N'job:tests')", "fk_memberships_")]
    [InlineData("insert into reg.operator_roles values (N'3f2504e04f8941d39a0c0305e82c3301', N'nobody', sysdatetimeoffset(), N'job:tests')", "fk_operator_roles_")]
    [InlineData("insert into reg.pending_acts (act_id, kind, subject, role, proposed_at, proposed_by, expires_at) values (newid(), N'platform_role', null, N'ops', sysdatetimeoffset(), N'job:tests', dateadd(day, 7, sysdatetimeoffset()))", "ck_pending_acts_target")]
    [InlineData("insert into reg.pending_acts (act_id, kind, role, definition, proposed_at, proposed_by, expires_at) values (newid(), N'role_definition', N'ops', N'{not json', sysdatetimeoffset(), N'job:tests', dateadd(day, 7, sysdatetimeoffset()))", "ck_pending_acts_definition")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (newid(), N'ZZ', N'acme', N'a@b.c', 0, N'email', sysdatetimeoffset(), N'job:tests', dateadd(day, 1, sysdatetimeoffset()))", "ck_invitations_token_hash")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (newid(), replicate(N'a', 64), N'acme', N'A@b.c', 0, N'email', sysdatetimeoffset(), N'job:tests', dateadd(day, 1, sysdatetimeoffset()))", "ck_invitations_email")]
    [InlineData("insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at) values (newid(), replicate(N'a', 64), N'acme', N'a@b.c', 0, N'sms', sysdatetimeoffset(), N'job:tests', dateadd(day, 1, sysdatetimeoffset()))", "ck_invitations_delivery")]
    [InlineData("insert into reg.ownership_transfers values (N'acme', N'3f2504e04f8941d39a0c0305e82c3301', N'3f2504e04f8941d39a0c0305e82c3301', sysdatetimeoffset(), N'job:tests', dateadd(day, 7, sysdatetimeoffset()))", "ck_ownership_transfers_parties")]
    [InlineData("insert into reg.ownership_transfers values (N'nowhere', N'3f2504e04f8941d39a0c0305e82c3301', N'8d1e3b2a6c4f4a8e9b1d2f3e4a5b6c7d', sysdatetimeoffset(), N'job:tests', dateadd(day, 7, sysdatetimeoffset()))", "fk_ownership_transfers_")]
    public async Task The_schema_refuses_what_the_registry_would_never_produce(string sql, string broken)
    {
        _database.SkipWhenUnavailable();

        var exception = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsApplicationAsync(sql, Ct));

        Assert.Equal(broken.StartsWith("ck_", StringComparison.Ordinal) || broken.StartsWith("fk_", StringComparison.Ordinal) ? 547 : 515, exception.Number);
        Assert.Contains(broken, exception.Message);
    }

    [Fact]
    public void An_empty_connection_string_refuses_the_store()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SqlServerRegistryStore(
            Microsoft.Extensions.Options.Options.Create(new RegistryDatabaseOptions { ConnectionString = " " })));

        Assert.Contains("ConnectionString is required", exception.Message);
    }

    [Fact]
    public void The_declaration_runs_on_SqlServer_with_its_own_script()
    {
        Assert.Equal(DatabaseEngine.SqlServer, RegistryDatabase.Value.Engine);
        Assert.Equal(["0001_reg.sql"], RegistryDatabase.Value.Scripts.Select(script => script.FileName));
        Assert.Contains("collate Latin1_General_100_BIN2", RegistryDatabase.Value.Scripts[0].Sql);
    }
}

/// <summary>
/// A host on the SQL Server stores (<see cref="RegistryHostTests"/>): configured with the login of the variable, its
/// store — and so the verification at startup — connecting as the application's user.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerHostTests(SqlServerFixture database) : RegistryHostTests(database)
{
    /// <inheritdoc/>
    protected override ModuleDatabase Declaration => RegistryDatabase.Value;

    /// <inheritdoc/>
    protected override string ConnectionStringKey => "Stellaegis:Registry:SqlServer:ConnectionString";

    /// <inheritdoc/>
    protected override void AddRegistry(IServiceCollection services, IConfiguration configuration) => services.AddStellaegisRegistrySqlServer(configuration);

    /// <inheritdoc/>
    protected override void AddRegistry(IServiceCollection services, string connectionString)
        => services.AddStellaegisRegistrySqlServer(options => options.ConnectionString = connectionString);

    /// <inheritdoc/>
    protected override void UnderApplicationRole(IServiceCollection services) => database.UnderApplicationRole(services);

    /// <inheritdoc/>
    protected override string RenameRecordSql(bool back) => back
        ? $"exec sp_rename N'{RegistryDatabase.Schema}._schema_migrations_gone', N'_schema_migrations'"
        : $"exec sp_rename N'{RegistryDatabase.Value.MigrationsTable}', N'_schema_migrations_gone'";
}

/// <summary>The scenarios of the projection replayed on the SQL Server stores, as the application's user.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerProjectionTests(SqlServerFixture database) : ProjectionTestsBase, IAsyncLifetime
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
