using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stellaegis.Primitives;
using Stellaegis.Registry.PostgreSQL;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The six stores against a real PostgreSQL, under the application's role: what the schema itself guarantees —
/// grammars, statuses, the keys between the tables —, a role written whole or not at all, the catalogue
/// re-recorded, the readings by role, and the record of the installation closed to the application.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlStoreTests(PostgreSqlFixture database) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TestHost? _host;

    private TestHost Host
    {
        get
        {
            database.SkipWhenUnavailable();
            return _host ??= new TestHost(stores: services => services.AddStellaegisRegistryPostgreSQL(options => options.ConnectionString = database.AppConnectionString));
        }
    }

    private ICompartmentStore Compartments => Host.Services.GetRequiredService<ICompartmentStore>();

    private IUserStore Users => Host.Services.GetRequiredService<IUserStore>();

    private ICatalogStore Catalog => Host.Services.GetRequiredService<ICatalogStore>();

    private IRoleStore Roles => Host.Services.GetRequiredService<IRoleStore>();

    private IMembershipStore Memberships => Host.Services.GetRequiredService<IMembershipStore>();

    private IOperatorStore Operators => Host.Services.GetRequiredService<IOperatorStore>();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        if (database.Unavailable is null)
            await database.ClearAsync(Ct);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The trace of the rows the tests seed: written by the installation of the tests, at <see cref="Now"/>.</summary>
    private static readonly Audit Seeded = Audit.CreatedBy(By, Now);

    /// <summary>The trace of the grants the tests seed.</summary>
    private static readonly Stamp Granted = new(Now, By);

    private async Task SeedAsync()
    {
        await Catalog.SaveAsync([new CatalogEntry("inventory", "items.read", OperationKind.Read), new CatalogEntry("inventory", "items.write", OperationKind.Write), new CatalogEntry("billing", "invoices.read", OperationKind.Read)], Ct);
        await Roles.SaveAsync(new Role("reader", RoleScope.Compartment, "Reader", Declared: true, ["items.read", "invoices.read"], Seeded), Ct);
        await Roles.SaveAsync(new Role("ops", RoleScope.Platform, "Operations", Declared: false, ["items.write"], Seeded), Ct);
        await Compartments.SaveAsync(new CompartmentRecord(Acme, "Acme SA", CompartmentStatus.Active, Seeded), Ct);
        await Compartments.SaveAsync(new CompartmentRecord(Globex, "Globex", CompartmentStatus.Active, Seeded), Ct);
        await Users.SaveAsync(new UserRecord(Jane, "jane@acme.test", "Jane Admin", UserStatus.Active, Seeded), Ct);
        await Users.SaveAsync(new UserRecord(John, "john@acme.test", "John Doe", UserStatus.Active, Seeded), Ct);
    }

    // ── What the schema guarantees ───────────────────────────────────────────

    [Theory]
    [InlineData("insert into reg.compartments values ('Acme', 'x', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('ac--me', 'x', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'gone', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'active', now(), ' ', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.compartments values ('acme', 'x', 'active', now(), 'job:tests', now(), null)", "23502")]
    [InlineData("insert into reg.users values ('jane', 'jane@acme.test', 'Jane', 'active', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.users values ('3f2504e04f8941d39a0c0305e82c3301', 'jane@acme.test', 'Jane', 'blocked', now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.operations values ('items-read', 'inventory', 'read')", "23514")]
    [InlineData("insert into reg.operations values ('items.read', 'Inventory', 'read')", "23514")]
    [InlineData("insert into reg.operations values ('items.read', 'inventory', 'purge')", "23514")]
    [InlineData("insert into reg.roles values ('tenant-administrator', 'compartment', 'x', true, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('super__admin', 'platform', 'x', true, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.roles values ('reader', 'tenant', 'x', true, now(), 'job:tests', now(), 'job:tests')", "23514")]
    [InlineData("insert into reg.role_operations values ('nobody', 'items.read')", "23503")]
    [InlineData("insert into reg.memberships values ('3f2504e04f8941d39a0c0305e82c3301', 'nowhere', now(), 'job:tests')", "23503")]
    [InlineData("insert into reg.operator_roles values ('3f2504e04f8941d39a0c0305e82c3301', 'nobody', now(), 'job:tests')", "23503")]
    public async Task The_schema_refuses_what_the_registry_would_never_produce(string sql, string sqlState)
    {
        database.SkipWhenUnavailable();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsApplicationAsync(sql, Ct));

        Assert.Equal(sqlState, exception.SqlState);
    }

    [Fact]
    public async Task A_member_role_needs_the_membership_and_goes_with_it()
    {
        await SeedAsync();

        // Assigning a role to someone who is not a member is refused by the schema (the service refuses it before).
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Memberships.AssignRoleAsync(Jane, Acme, "reader", Granted, Ct));
        Assert.Equal("23503", exception.SqlState);

        await Memberships.AddAsync(Jane, Acme, Granted, Ct);
        await Memberships.AssignRoleAsync(Jane, Acme, "reader", Granted, Ct);
        await Memberships.AssignRoleAsync(Jane, Acme, "reader", new Stamp(Now.AddDays(1), Actor.Job("later")), Ct);   // again: nothing, the first grant stays
        var membership = Assert.Single(await Memberships.ListByUserAsync(Jane, Ct));
        Assert.Equal(["reader"], membership.Roles);
        Assert.Equal(Granted, membership.Granted);
        Assert.Equal("job:tests", await database.ScalarAsApplicationAsync<string>($"select granted_by from reg.member_roles where subject = '{Jane}' and role = 'reader'", Ct));

        await Memberships.RemoveAsync(Jane, Acme, Ct);
        Assert.Empty(await Memberships.ListByUserAsync(Jane, Ct));
        Assert.Empty(await Memberships.ListByRoleAsync("reader", Ct));
    }

    [Fact]
    public async Task A_role_still_held_is_not_deleted()
    {
        await SeedAsync();
        await Memberships.AddAsync(Jane, Acme, Granted, Ct);
        await Memberships.AssignRoleAsync(Jane, Acme, "reader", Granted, Ct);
        await Operators.AssignAsync(John, "ops", Granted, Ct);

        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => Roles.DeleteAsync("reader", Ct))).SqlState);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => Roles.DeleteAsync("ops", Ct))).SqlState);

        await Operators.UnassignAsync(John, "ops", Ct);
        await Roles.DeleteAsync("ops", Ct);
        Assert.Null(await Roles.GetAsync("ops", Ct));
        Assert.Empty(await Operators.ListAsync(Ct));
    }

    [Fact]
    public async Task The_record_of_the_installation_is_closed_to_the_application()
    {
        database.SkipWhenUnavailable();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsApplicationAsync(
            $"insert into {RegistryDatabase.Value.MigrationsTable} (version, script_name, checksum, applied_by) values (9, 'x', 'x', 'x')", Ct));

        Assert.Equal("42501", exception.SqlState);
    }

    // ── A role written whole or not at all ───────────────────────────────────

    [Fact]
    public async Task A_role_is_saved_with_its_operations_in_one_transaction()
    {
        await SeedAsync();

        var changed = Seeded.ModifiedBy(Actor.Parse("3f2504e04f8941d39a0c0305e82c3301"), Now.AddHours(1));
        await Roles.SaveAsync(new Role("reader", RoleScope.Compartment, "Reader of items", Declared: true, ["items.read", "items.read"], changed), Ct);
        var role = (await Roles.GetAsync("reader", Ct))!;
        Assert.Equal("Reader of items", role.DisplayName);
        Assert.Equal(["items.read"], role.Operations);
        Assert.Equal(changed, role.Audit);   // the creation kept, the change recorded, both with their actor

        // An operation out of the catalogue fails the save, and the previous operations stay in place.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => Roles.SaveAsync(role with { Operations = ["items.read", "items.ship"] }, Ct));
        Assert.Equal("23503", exception.SqlState);
        Assert.Equal(["items.read"], (await Roles.GetAsync("reader", Ct))!.Operations);

        await Roles.SaveAsync(role with { Operations = [] }, Ct);
        Assert.Empty((await Roles.GetAsync("reader", Ct))!.Operations);
    }

    [Fact]
    public async Task The_roles_are_listed_by_scope_with_their_operations()
    {
        await SeedAsync();

        var all = await Roles.ListAsync(null, Ct);
        Assert.Equal(["ops", "reader"], all.Select(r => r.Name));
        Assert.Equal(["invoices.read", "items.read"], all.Single(r => r.Name == "reader").Operations);
        Assert.True(all.Single(r => r.Name == "reader").Declared);
        Assert.Equal(["ops"], (await Roles.ListAsync(RoleScope.Platform, Ct)).Select(r => r.Name));
        Assert.Null(await Roles.GetAsync("nobody", Ct));
    }

    // ── The catalogue ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_catalogue_is_recorded_again_without_removing_and_with_the_kind_updated()
    {
        await SeedAsync();

        await Catalog.SaveAsync([new CatalogEntry("inventory", "items.read", OperationKind.Write), new CatalogEntry("inventory", "items.purge", OperationKind.Delete)], Ct);

        var entries = await Catalog.ListAsync(Ct);
        Assert.Equal(["invoices.read", "items.purge", "items.read", "items.write"], entries.Select(e => e.Operation));
        Assert.Equal(OperationKind.Write, entries.Single(e => e.Operation == "items.read").Kind);
        Assert.Equal("billing", entries.Single(e => e.Operation == "invoices.read").Module);

        await Catalog.SaveAsync([], Ct);   // nothing to record: nothing happens
        Assert.Equal(4, (await Catalog.ListAsync(Ct)).Count);
    }

    // ── The readings by role, and the memberships without roles ──────────────

    [Fact]
    public async Task The_holders_of_a_role_are_read_with_every_role_they_hold()
    {
        await SeedAsync();
        await Roles.SaveAsync(new Role("accountant", RoleScope.Compartment, "Accountant", Declared: false, ["invoices.read"], Seeded), Ct);
        await Memberships.AddAsync(Jane, Acme, Granted, Ct);
        await Memberships.AddAsync(Jane, Globex, Granted, Ct);
        await Memberships.AddAsync(John, Globex, Granted, Ct);
        await Memberships.AssignRoleAsync(Jane, Acme, "reader", Granted, Ct);
        await Memberships.AssignRoleAsync(John, Globex, "reader", Granted, Ct);
        await Memberships.AssignRoleAsync(John, Globex, "accountant", Granted, Ct);

        var holders = await Memberships.ListByRoleAsync("reader", Ct);
        Assert.Equal(2, holders.Count);
        Assert.Equal(["reader"], holders.Single(m => m.Subject == Jane).Roles);
        Assert.Equal(["accountant", "reader"], holders.Single(m => m.Subject == John).Roles);

        // A membership without a role is a membership still.
        var janes = await Memberships.ListByUserAsync(Jane, Ct);
        Assert.Equal([Acme, Globex], janes.Select(m => m.Compartment));
        Assert.Empty(janes.Single(m => m.Compartment == Globex).Roles);
        Assert.True(await Memberships.IsMemberAsync(Jane, Globex, Ct));
        Assert.False(await Memberships.IsMemberAsync(John, Acme, Ct));

        Assert.Equal(2, (await Memberships.ListByCompartmentAsync(Globex, Ct)).Count);
        await Memberships.RemoveCompartmentAsync(Globex, Ct);
        Assert.Empty(await Memberships.ListByCompartmentAsync(Globex, Ct));
        Assert.Single(await Memberships.ListByUserAsync(Jane, Ct));
    }

    [Fact]
    public async Task The_operators_are_read_by_role_and_as_a_whole()
    {
        await SeedAsync();
        await Roles.SaveAsync(new Role("super_admin", RoleScope.Platform, "Platform administrator", Declared: true, ["items.read", "items.write"], Seeded), Ct);
        await Operators.AssignAsync(Jane, "super_admin", Granted, Ct);
        await Operators.AssignAsync(Jane, "ops", Granted, Ct);
        await Operators.AssignAsync(John, "ops", Granted, Ct);
        await Operators.AssignAsync(John, "ops", Granted, Ct);   // again: nothing
        Assert.Equal("job:tests", await database.ScalarAsApplicationAsync<string>($"select granted_by from reg.operator_roles where subject = '{John}'", Ct));

        Assert.Equal(["ops", "super_admin"], await Operators.ListRolesAsync(Jane, Ct));
        Assert.Equal([Jane, John], await Operators.ListByRoleAsync("ops", Ct));
        var all = await Operators.ListAsync(Ct);
        Assert.Equal([Jane, John], all.Select(o => o.Subject));
        Assert.Equal(["ops"], all.Single(o => o.Subject == John).Roles);

        await Operators.UnassignAsync(John, "ops", Ct);
        Assert.Empty(await Operators.ListRolesAsync(John, Ct));
    }

    // ── Compartments and users ───────────────────────────────────────────────

    [Fact]
    public async Task A_compartment_and_a_user_are_saved_whole_and_their_status_replaced()
    {
        await SeedAsync();

        var acme = (await Compartments.GetAsync(Acme, Ct))!;
        Assert.Equal(new CompartmentRecord(Acme, "Acme SA", CompartmentStatus.Active, Seeded), acme);
        var terminated = acme with { DisplayName = "Acme", Status = CompartmentStatus.Terminated, Audit = acme.Audit.ModifiedBy(Actor.Parse(Jane), Now.AddDays(1)) };
        await Compartments.SaveAsync(terminated, Ct);
        Assert.Equal(terminated, await Compartments.GetAsync(Acme, Ct));
        Assert.Equal(Jane, await database.ScalarAsApplicationAsync<string>("select modified_by from reg.compartments where compartment = 'acme'", Ct));
        Assert.Equal("job:tests", await database.ScalarAsApplicationAsync<string>("select created_by from reg.compartments where compartment = 'acme'", Ct));
        Assert.Equal([Acme, Globex], (await Compartments.ListAsync(Ct)).Select(c => c.Compartment));
        Assert.Null(await Compartments.GetAsync(Compartment.Parse("nowhere"), Ct));

        var jane = (await Users.GetAsync(Jane, Ct))!;
        Assert.Equal(new UserRecord(Jane, "jane@acme.test", "Jane Admin", UserStatus.Active, Seeded), jane);
        await Users.SaveAsync(jane with { Email = "jane@globex.test", Status = UserStatus.Deactivated }, Ct);
        var saved = (await Users.GetAsync(Jane, Ct))!;
        Assert.Equal(("jane@globex.test", UserStatus.Deactivated), (saved.Email, saved.Status));
        Assert.Equal([Jane, John], (await Users.ListAsync(Ct)).Select(u => u.Subject));
        Assert.Null(await Users.GetAsync(Guid.NewGuid().ToString("N"), Ct));
    }
}
