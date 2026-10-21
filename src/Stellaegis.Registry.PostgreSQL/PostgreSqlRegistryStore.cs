using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Registry.PostgreSQL;

/// <summary>
/// The six stores of the registry on PostgreSQL, in the schema <c>reg</c>: one object, one data source, the
/// repositories of <see cref="RegistryDatabase"/> handed out by a unit of work of <c>Stellaegis.Primitives.Data</c>.
/// Each read is one query on its own unit of work; a role and its operations are written in one transaction, as
/// is the catalogue, so that the projector never reads a role half replaced. No connection outlives a call, and
/// none is shared between two.
/// </summary>
internal sealed class PostgreSqlRegistryStore
    : ICompartmentStore, IUserStore, ICatalogStore, IRoleStore, IMembershipStore, IOperatorStore, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly AdoUnitOfWorkFactory<RegistryDatabase> _units;

    /// <summary>Creates the store from options the host validated at startup, or that a caller built by hand.</summary>
    /// <exception cref="InvalidOperationException">The options do not hold: <see cref="RegistryDatabaseOptions.FindError"/> says why.</exception>
    public PostgreSqlRegistryStore(IOptions<RegistryDatabaseOptions> options)
    {
        var value = Guard.NotNull(options).Value;
        if (value.FindError() is { } error)
            throw new InvalidOperationException($"{nameof(RegistryDatabaseOptions)}: {error}");

        var registry = new RepositoryRegistry<RegistryDatabase>()
            .Add<CompartmentRepository>((connection, transaction) => new CompartmentRepository(connection, transaction))
            .Add<UserRepository>((connection, transaction) => new UserRepository(connection, transaction))
            .Add<CatalogRepository>((connection, transaction) => new CatalogRepository(connection, transaction))
            .Add<RoleRepository>((connection, transaction) => new RoleRepository(connection, transaction))
            .Add<MembershipRepository>((connection, transaction) => new MembershipRepository(connection, transaction))
            .Add<OperatorRepository>((connection, transaction) => new OperatorRepository(connection, transaction));

        _dataSource = NpgsqlDataSource.Create(value.ConnectionString);
        _units = new AdoUnitOfWorkFactory<RegistryDatabase>(_dataSource, registry);
    }

    /// <summary>Opens a connection under the application's role, as the verification of the database needs one.</summary>
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        => await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    // ── Compartments ─────────────────────────────────────────────────────────

    async Task<CompartmentRecord?> ICompartmentStore.GetAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<CompartmentRepository>().GetAsync(compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<CompartmentRecord>> ICompartmentStore.ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<CompartmentRepository>().ListAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ICompartmentStore.SaveAsync(CompartmentRecord compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CompartmentRepository>().UpsertAsync(compartment, cancellationToken).ConfigureAwait(false);
    }

    // ── Users ────────────────────────────────────────────────────────────────

    async Task<UserRecord?> IUserStore.GetAsync(string subject, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<UserRepository>().GetAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<UserRecord>> IUserStore.ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<UserRepository>().ListAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IUserStore.SaveAsync(UserRecord user, CancellationToken cancellationToken)
    {
        Guard.NotNull(user);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<UserRepository>().UpsertAsync(user, cancellationToken).ConfigureAwait(false);
    }

    // ── Catalogue ────────────────────────────────────────────────────────────

    async Task<IReadOnlyList<CatalogEntry>> ICatalogStore.ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<CatalogRepository>().ListAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ICatalogStore.SaveAsync(IReadOnlyCollection<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        Guard.NotNull(entries);
        if (entries.Count == 0)
            return;
        // One statement, hence one transaction: the catalogue is never read half recorded.
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CatalogRepository>().UpsertAsync(entries.DistinctBy(e => e.Operation, StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false);
    }

    // ── Roles ────────────────────────────────────────────────────────────────

    async Task<Role?> IRoleStore.GetAsync(string name, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(name);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await uow.Repo<RoleRepository>().ListAsync(name, null, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    async Task<IReadOnlyList<Role>> IRoleStore.ListAsync(RoleScope? scope, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<RoleRepository>().ListAsync(null, scope, cancellationToken).ConfigureAwait(false);
    }

    async Task IRoleStore.SaveAsync(Role role, CancellationToken cancellationToken)
    {
        Guard.NotNull(role);
        // The role and its operations in one transaction: a projector recomputing the holders of a role never
        // reads it with half its operations, and a save the schema refuses leaves the previous ones in place.
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        var roles = uow.Repo<RoleRepository>();
        await roles.UpsertAsync(role, cancellationToken).ConfigureAwait(false);
        await roles.DeleteOperationsAsync(role.Name, cancellationToken).ConfigureAwait(false);
        var operations = role.Operations.Distinct(StringComparer.Ordinal).ToArray();
        if (operations.Length > 0)
            await roles.InsertOperationsAsync(role.Name, operations, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IRoleStore.DeleteAsync(string name, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(name);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<RoleRepository>().DeleteAsync(name, cancellationToken).ConfigureAwait(false);
    }

    // ── Memberships ──────────────────────────────────────────────────────────

    async Task<bool> IMembershipStore.IsMemberAsync(string subject, Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<MembershipRepository>().ExistsAsync(subject, compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<Membership>> IMembershipStore.ListByUserAsync(string subject, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<MembershipRepository>().ListAsync(subject: subject, compartment: null, role: null, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<Membership>> IMembershipStore.ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<MembershipRepository>().ListAsync(subject: null, compartment: compartment.Value, role: null, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<Membership>> IMembershipStore.ListByRoleAsync(string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<MembershipRepository>().ListAsync(subject: null, compartment: null, role: role, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.AddAsync(string subject, Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().InsertAsync(subject, compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().DeleteAsync(subject, compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.AssignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().InsertRoleAsync(subject, compartment.Value, role, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.UnassignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().DeleteRoleAsync(subject, compartment.Value, role, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().DeleteByCompartmentAsync(compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    // ── Operators ────────────────────────────────────────────────────────────

    async Task<IReadOnlyList<string>> IOperatorStore.ListRolesAsync(string subject, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<OperatorRepository>().ListRolesAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> IOperatorStore.ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<OperatorRepository>().ListAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<string>> IOperatorStore.ListByRoleAsync(string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<OperatorRepository>().ListByRoleAsync(role, cancellationToken).ConfigureAwait(false);
    }

    async Task IOperatorStore.AssignAsync(string subject, string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<OperatorRepository>().InsertAsync(subject, role, cancellationToken).ConfigureAwait(false);
    }

    async Task IOperatorStore.UnassignAsync(string subject, string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<OperatorRepository>().DeleteAsync(subject, role, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <inheritdoc/>
    public void Dispose() => _dataSource.Dispose();
}
