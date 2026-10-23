using System.Data.Common;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Registry.SqlServer;

/// <summary>
/// The stores of the registry on SQL Server, in the schema <c>reg</c>: one object, one connection string, the
/// repositories of <see cref="RegistryDatabase"/> handed out by a unit of work of <c>Stellaegis.Primitives.Data</c>.
/// Each read is one query on its own unit of work; a role and its operations are written in one transaction, as
/// is the catalogue, so that the projector never reads a role half replaced. No connection outlives a call, and
/// none is shared between two.
/// </summary>
internal sealed class SqlServerRegistryStore
    : ICompartmentStore, IUserStore, ICatalogStore, IRoleStore, IMembershipStore, IOperatorStore, IInvitationStore, IPendingActStore
{
    private readonly Func<CancellationToken, Task<DbConnection>> _open;
    private readonly AdoUnitOfWorkFactory<RegistryDatabase> _units;

    /// <summary>Creates the store from options the host validated at startup, or that a caller built by hand.</summary>
    /// <exception cref="InvalidOperationException">The options do not hold: <see cref="RegistryDatabaseOptions.FindError"/> says why.</exception>
    public SqlServerRegistryStore(IOptions<RegistryDatabaseOptions> options)
        : this(OpenFrom(Guard.NotNull(options).Value))
    {
    }

    /// <summary>Creates the store on connections <paramref name="open"/> opens — the tests take the application's identity on them.</summary>
    internal SqlServerRegistryStore(Func<CancellationToken, Task<DbConnection>> open)
    {
        _open = Guard.NotNull(open);
        var registry = new RepositoryRegistry<RegistryDatabase>()
            .Add<CompartmentRepository>((connection, transaction) => new CompartmentRepository(connection, transaction))
            .Add<UserRepository>((connection, transaction) => new UserRepository(connection, transaction))
            .Add<CatalogRepository>((connection, transaction) => new CatalogRepository(connection, transaction))
            .Add<RoleRepository>((connection, transaction) => new RoleRepository(connection, transaction))
            .Add<MembershipRepository>((connection, transaction) => new MembershipRepository(connection, transaction))
            .Add<OperatorRepository>((connection, transaction) => new OperatorRepository(connection, transaction))
            .Add<InvitationRepository>((connection, transaction) => new InvitationRepository(connection, transaction))
            .Add<PendingActRepository>((connection, transaction) => new PendingActRepository(connection, transaction));

        _units = new AdoUnitOfWorkFactory<RegistryDatabase>(_open, registry);
    }

    /// <summary>Opens a connection under the application's role, as the verification of the database needs one.</summary>
    public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken) => _open(cancellationToken);

    private static Func<CancellationToken, Task<DbConnection>> OpenFrom(RegistryDatabaseOptions options)
    {
        if (options.FindError() is { } error)
            throw new InvalidOperationException($"{nameof(RegistryDatabaseOptions)}: {error}");
        var connectionString = options.ConnectionString;
        return async cancellationToken =>
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        };
    }

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

    // The ownership changes in one transaction: the owner, the membership and the roles together, or nothing.
    async Task<bool> ICompartmentStore.SeatOwnerAsync(Compartment compartment, string owner, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        if (!await uow.Repo<CompartmentRepository>().SetOwnerIfNoneAsync(compartment.Value, owner, cancellationToken).ConfigureAwait(false))
            return false;
        var memberships = uow.Repo<MembershipRepository>();
        await memberships.InsertAsync(owner, compartment.Value, granted, cancellationToken).ConfigureAwait(false);
        await memberships.InsertRoleAsync(owner, compartment.Value, RegistryRoles.Owner, granted, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    async Task<bool> ICompartmentStore.RescueOwnershipAsync(Compartment compartment, string owner, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        var compartments = uow.Repo<CompartmentRepository>();
        if (!await compartments.ExistsAsync(compartment.Value, cancellationToken).ConfigureAwait(false))
            return false;
        var previous = await compartments.ReplaceOwnerAnyAsync(compartment.Value, owner, cancellationToken).ConfigureAwait(false);
        var memberships = uow.Repo<MembershipRepository>();
        if (previous is not null && previous != owner)
            await memberships.DeleteRoleAsync(previous, compartment.Value, RegistryRoles.Owner, cancellationToken).ConfigureAwait(false);
        await memberships.InsertAsync(owner, compartment.Value, granted, cancellationToken).ConfigureAwait(false);
        await memberships.InsertRoleAsync(owner, compartment.Value, RegistryRoles.Owner, granted, cancellationToken).ConfigureAwait(false);
        await compartments.DeleteTransferAsync(compartment.Value, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ── Acts waiting for a validation ────────────────────────────────────────

    async Task IPendingActStore.InsertAsync(PendingAct act, CancellationToken cancellationToken)
    {
        Guard.NotNull(act);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<PendingActRepository>().InsertAsync(act, cancellationToken).ConfigureAwait(false);
    }

    async Task<PendingAct?> IPendingActStore.GetAsync(Guid actId, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await uow.Repo<PendingActRepository>().ListAsync(actId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    async Task<IReadOnlyList<PendingAct>> IPendingActStore.ListAsync(CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<PendingActRepository>().ListAsync(null, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IPendingActStore.MarkValidatedAsync(Guid actId, Stamp validated, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<PendingActRepository>().CloseAsync(actId, validated: true, validated, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IPendingActStore.MarkRejectedAsync(Guid actId, Stamp rejected, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<PendingActRepository>().CloseAsync(actId, validated: false, rejected, cancellationToken).ConfigureAwait(false);
    }

    async Task<OwnershipTransfer?> ICompartmentStore.GetTransferAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<CompartmentRepository>().GetTransferAsync(compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task ICompartmentStore.SaveTransferAsync(OwnershipTransfer transfer, CancellationToken cancellationToken)
    {
        Guard.NotNull(transfer);
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CompartmentRepository>().SaveTransferAsync(transfer, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ICompartmentStore.DeleteTransferAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CompartmentRepository>().DeleteTransferAsync(compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> ICompartmentStore.TransferOwnershipAsync(OwnershipTransfer transfer, IReadOnlyCollection<string> dropped, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotNull(transfer);
        Guard.NotNull(dropped);
        var compartment = transfer.Compartment.Value;
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        var compartments = uow.Repo<CompartmentRepository>();
        if (!await compartments.ReplaceOwnerAsync(compartment, transfer.From, transfer.To, cancellationToken).ConfigureAwait(false))
            return false;
        var memberships = uow.Repo<MembershipRepository>();
        await memberships.InsertRoleAsync(transfer.To, compartment, RegistryRoles.Owner, granted, cancellationToken).ConfigureAwait(false);
        await memberships.DeleteRoleAsync(transfer.From, compartment, RegistryRoles.Owner, cancellationToken).ConfigureAwait(false);
        foreach (var role in dropped)
            await memberships.DeleteRoleAsync(transfer.From, compartment, role, cancellationToken).ConfigureAwait(false);
        await compartments.DeleteTransferAsync(compartment, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
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

    async Task<UserRecord?> IUserStore.FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(email);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<UserRepository>().FindByEmailAsync(email, cancellationToken).ConfigureAwait(false);
    }

    // ── Invitations ──────────────────────────────────────────────────────────

    async Task IInvitationStore.InsertAsync(Invitation invitation, CancellationToken cancellationToken)
    {
        Guard.NotNull(invitation);
        // The invitation and its roles in one transaction: never one without the other.
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<InvitationRepository>().InsertAsync(invitation, cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<Invitation?> IInvitationStore.GetAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await uow.Repo<InvitationRepository>().ListAsync("i.invitation_id = @invitationId", new { invitationId }, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    async Task<Invitation?> IInvitationStore.GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await uow.Repo<InvitationRepository>().ListAsync("i.token_hash = @tokenHash", new { tokenHash }, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    async Task<IReadOnlyList<Invitation>> IInvitationStore.ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<InvitationRepository>().ListAsync("i.compartment = @compartment", new { compartment = compartment.Value }, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IInvitationStore.RevokeAsync(Guid invitationId, Stamp revoked, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<InvitationRepository>().RevokeAsync(invitationId, revoked, cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IInvitationStore.TryAcceptAsync(string tokenHash, string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<InvitationRepository>().TryAcceptAsync(tokenHash, subject, at, cancellationToken).ConfigureAwait(false);
    }

    async Task<int> IInvitationStore.DeleteDeadBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await uow.Repo<InvitationRepository>().DeleteDeadBeforeAsync(before, cancellationToken).ConfigureAwait(false);
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
        // One transaction: the catalogue is never read half recorded.
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CatalogRepository>().UpsertAsync(entries.DistinctBy(e => e.Operation, StringComparer.Ordinal).ToList(), cancellationToken).ConfigureAwait(false);
        await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ICatalogStore.HardenAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
    {
        Guard.NotNull(operations);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<CatalogRepository>().HardenAsync(operations, cancellationToken).ConfigureAwait(false);
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
        // The role, its operations and its assignable roles in one transaction: a projector recomputing the holders
        // of a role never reads it half written, and a save the schema refuses leaves the previous ones in place.
        await using var uow = await _units.OpenAndBeginAsync(cancellationToken).ConfigureAwait(false);
        var roles = uow.Repo<RoleRepository>();
        await roles.UpsertAsync(role, cancellationToken).ConfigureAwait(false);
        await roles.DeleteOperationsAsync(role.Name, cancellationToken).ConfigureAwait(false);
        var operations = role.Operations.Distinct(StringComparer.Ordinal).ToArray();
        if (operations.Length > 0)
            await roles.InsertOperationsAsync(role.Name, operations, cancellationToken).ConfigureAwait(false);
        await roles.DeleteAssignableAsync(role.Name, cancellationToken).ConfigureAwait(false);
        var assignable = role.Assignable.Roles.Distinct(StringComparer.Ordinal).ToArray();
        if (assignable.Length > 0)
            await roles.InsertAssignableAsync(role.Name, assignable, cancellationToken).ConfigureAwait(false);
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

    async Task IMembershipStore.AddAsync(string subject, Compartment compartment, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        Guard.NotNull(granted);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().InsertAsync(subject, compartment.Value, granted, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().DeleteAsync(subject, compartment.Value, cancellationToken).ConfigureAwait(false);
    }

    async Task IMembershipStore.AssignRoleAsync(string subject, Compartment compartment, string role, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotNull(compartment);
        Guard.NotWhiteSpace(role);
        Guard.NotNull(granted);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<MembershipRepository>().InsertRoleAsync(subject, compartment.Value, role, granted, cancellationToken).ConfigureAwait(false);
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

    async Task IOperatorStore.AssignAsync(string subject, string role, Stamp granted, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotWhiteSpace(role);
        Guard.NotNull(granted);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<OperatorRepository>().InsertAsync(subject, role, granted, cancellationToken).ConfigureAwait(false);
    }

    async Task IOperatorStore.UnassignAsync(string subject, string role, CancellationToken cancellationToken)
    {
        Guard.NotWhiteSpace(subject);
        Guard.NotWhiteSpace(role);
        await using var uow = await _units.OpenAsync(cancellationToken).ConfigureAwait(false);
        await uow.Repo<OperatorRepository>().DeleteAsync(subject, role, cancellationToken).ConfigureAwait(false);
    }

}
