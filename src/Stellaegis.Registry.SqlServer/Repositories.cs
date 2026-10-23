using System.Text.Json;
using System.Data.Common;
using Dapper;
using Stellaegis.Primitives;

namespace Stellaegis.Registry.SqlServer;

/// <summary>The statuses and kinds as the schema stores them: lowercase names, constrained by a check.</summary>
internal static class Names
{
    public static string Of(CompartmentStatus status) => status switch
    {
        CompartmentStatus.Active => "active",
        CompartmentStatus.Suspended => "suspended",
        CompartmentStatus.Terminated => "terminated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown compartment status."),
    };

    public static CompartmentStatus CompartmentStatusOf(string name) => name switch
    {
        "active" => CompartmentStatus.Active,
        "suspended" => CompartmentStatus.Suspended,
        "terminated" => CompartmentStatus.Terminated,
        _ => throw new InvalidOperationException($"reg.compartments holds the status '{name}', which this package does not know."),
    };

    public static string Of(UserStatus status) => status switch
    {
        UserStatus.Active => "active",
        UserStatus.Deactivated => "deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown user status."),
    };

    public static UserStatus UserStatusOf(string name) => name switch
    {
        "active" => UserStatus.Active,
        "deactivated" => UserStatus.Deactivated,
        _ => throw new InvalidOperationException($"reg.users holds the status '{name}', which this package does not know."),
    };

    public static string Of(RoleCategory category) => category switch
    {
        RoleCategory.Administration => "administration",
        RoleCategory.Business => "business",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown role category."),
    };

    public static RoleCategory CategoryOf(string name) => name switch
    {
        "administration" => RoleCategory.Administration,
        "business" => RoleCategory.Business,
        _ => throw new InvalidOperationException($"reg.roles holds the category '{name}', which this package does not know."),
    };

    public static string Of(CompartmentRoleSelector selector) => selector switch
    {
        CompartmentRoleSelector.None => "none",
        CompartmentRoleSelector.Business => "business",
        CompartmentRoleSelector.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, "Unknown compartment role selector."),
    };

    public static CompartmentRoleSelector SelectorOf(string name) => name switch
    {
        "none" => CompartmentRoleSelector.None,
        "business" => CompartmentRoleSelector.Business,
        "all" => CompartmentRoleSelector.All,
        _ => throw new InvalidOperationException($"reg.roles holds the selector '{name}', which this package does not know."),
    };

    public static string Of(OperationKind kind) => kind switch
    {
        OperationKind.Read => "read",
        OperationKind.Write => "write",
        OperationKind.Delete => "delete",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown operation kind."),
    };

    public static OperationKind KindOf(string name) => name switch
    {
        "read" => OperationKind.Read,
        "write" => OperationKind.Write,
        "delete" => OperationKind.Delete,
        _ => throw new InvalidOperationException($"reg.operations holds the kind '{name}', which this package does not know."),
    };

    public static string Of(RoleScope scope) => scope switch
    {
        RoleScope.Compartment => "compartment",
        RoleScope.Platform => "platform",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown role scope."),
    };

    public static RoleScope ScopeOf(string name) => name switch
    {
        "compartment" => RoleScope.Compartment,
        "platform" => RoleScope.Platform,
        _ => throw new InvalidOperationException($"reg.roles holds the scope '{name}', which this package does not know."),
    };

    /// <summary>A timestamp as the column holds it, <c>datetimeoffset(3)</c>, in UTC.</summary>
    public static DateTimeOffset Utc(DateTimeOffset at) => at.ToUniversalTime();

    /// <summary>A timestamp as the record carries it.</summary>
    public static DateTimeOffset At(DateTimeOffset at) => at;

    /// <summary>The trace of a row, from its four audit columns.</summary>
    public static Audit AuditOf(DateTimeOffset createdAt, string createdBy, DateTimeOffset modifiedAt, string modifiedBy)
        => new(new Stamp(At(createdAt), Actor.Parse(createdBy)), new Stamp(At(modifiedAt), Actor.Parse(modifiedBy)));
}

/// <summary>The rows of <c>reg.compartments</c>, read and written on the connection of a unit of work.</summary>
internal sealed class CompartmentRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Compartment, string DisplayName, string Status, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy, string? Owner);

    private sealed record TransferRow(string Compartment, string From, string To, DateTimeOffset ProposedAt, string ProposedBy, DateTimeOffset ExpiresAt, string? Kept);

    private const string Columns = "compartment, display_name, status, created_at, created_by, modified_at, modified_by";

    // Dapper matches a positional record by the names of its parameters: the columns are read under those names.
    private const string Selected = "compartment as Compartment, display_name as DisplayName, status as Status, created_at as CreatedAt, created_by as CreatedBy, modified_at as ModifiedAt, modified_by as ModifiedBy, owner_subject as Owner";

    public async Task<CompartmentRecord?> GetAsync(string compartment, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"select {Selected} from reg.compartments where compartment = @compartment",
            new { compartment }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"select {Selected} from reg.compartments order by compartment",
            transaction: transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public Task UpsertAsync(CompartmentRecord compartment, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            $"""
            begin try
                update reg.compartments set display_name = @displayName, status = @status, modified_at = @modifiedAt, modified_by = @modifiedBy where compartment = @compartment;
                if @@rowcount = 0
                    insert into reg.compartments ({Columns}) values (@compartment, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
                update reg.compartments set display_name = @displayName, status = @status, modified_at = @modifiedAt, modified_by = @modifiedBy where compartment = @compartment;
            end catch
            """,
            new
            {
                compartment = compartment.Compartment.Value,
                displayName = compartment.DisplayName,
                status = Names.Of(compartment.Status),
                createdAt = Names.Utc(compartment.Audit.Created.At),
                createdBy = compartment.Audit.Created.By.Value,
                modifiedAt = Names.Utc(compartment.Audit.Modified.At),
                modifiedBy = compartment.Audit.Modified.By.Value,
            }, transaction(), cancellationToken: cancellationToken));

    /// <summary>Seats the owner when there is none; whether it was seated.</summary>
    public async Task<bool> SetOwnerIfNoneAsync(string compartment, string owner, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition(
            "update reg.compartments set owner_subject = @owner where compartment = @compartment and owner_subject is null",
            new { compartment, owner }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    /// <summary>Replaces the owner when it is still <paramref name="from"/>; whether it was replaced.</summary>
    public async Task<bool> ReplaceOwnerAsync(string compartment, string from, string to, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition(
            "update reg.compartments set owner_subject = @to where compartment = @compartment and owner_subject = @from",
            new { compartment, from, to }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    public async Task<OwnershipTransfer?> GetTransferAsync(string compartment, CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<TransferRow>(new CommandDefinition(
            """
            select t.compartment as Compartment, t.from_subject as [From], t.to_subject as [To], t.proposed_at as ProposedAt,
                   t.proposed_by as ProposedBy, t.expires_at as ExpiresAt, k.role as Kept
            from reg.ownership_transfers t
            left join reg.ownership_transfer_kept_roles k on k.compartment = t.compartment
            where t.compartment = @compartment
            order by k.role
            """,
            new { compartment }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
        if (rows.Count == 0)
            return null;
        var first = rows[0];
        return new OwnershipTransfer(Compartment.Parse(first.Compartment), first.From, first.To,
            rows.Where(row => row.Kept is not null).Select(row => row.Kept!).ToList(),
            new Stamp(first.ProposedAt, Actor.Parse(first.ProposedBy)),
            first.ExpiresAt);
    }

    public async Task SaveTransferAsync(OwnershipTransfer transfer, CancellationToken cancellationToken)
    {
        await DeleteTransferAsync(transfer.Compartment.Value, cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.ownership_transfers (compartment, from_subject, to_subject, proposed_at, proposed_by, expires_at)
            values (@compartment, @from, @to, @proposedAt, @proposedBy, @expiresAt)
            """,
            new
            {
                compartment = transfer.Compartment.Value,
                from = transfer.From,
                to = transfer.To,
                proposedAt = Names.Utc(transfer.Proposed.At),
                proposedBy = transfer.Proposed.By.Value,
                expiresAt = Names.Utc(transfer.ExpiresAt),
            }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (transfer.Kept.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into reg.ownership_transfer_kept_roles (compartment, role) values (@compartment, @role)",
                transfer.Kept.Distinct(StringComparer.Ordinal).Select(role => new { compartment = transfer.Compartment.Value, role }).ToList(),
                transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Seats <paramref name="owner"/> whoever owns the compartment now, in the transaction of the caller; the former owner, or <see langword="null"/>.</summary>
    public async Task<string?> ReplaceOwnerAnyAsync(string compartment, string owner, CancellationToken cancellationToken)
    {
        var previous = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "select owner_subject from reg.compartments with (updlock, rowlock) where compartment = @compartment",
            new { compartment }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "update reg.compartments set owner_subject = @owner where compartment = @compartment",
            new { compartment, owner }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return previous;
    }

    public Task<bool> ExistsAsync(string compartment, CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select case when exists (select 1 from reg.compartments where compartment = @compartment) then cast(1 as bit) else cast(0 as bit) end",
            new { compartment }, transaction(), cancellationToken: cancellationToken));

    public Task DeleteTransferAsync(string compartment, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.ownership_transfers where compartment = @compartment",
            new { compartment }, transaction(), cancellationToken: cancellationToken));

    private static CompartmentRecord ToRecord(Row row)
        => new(Compartment.Parse(row.Compartment), row.DisplayName, Names.CompartmentStatusOf(row.Status),
            Names.AuditOf(row.CreatedAt, row.CreatedBy, row.ModifiedAt, row.ModifiedBy), row.Owner);
}

/// <summary>The rows of <c>reg.users</c>: the projection of the accounts of Identity.</summary>
internal sealed class UserRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Subject, string Email, string DisplayName, string Status, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy);

    private const string Columns = "subject, email, display_name, status, created_at, created_by, modified_at, modified_by";

    private const string Selected = "subject as Subject, email as Email, display_name as DisplayName, status as Status, created_at as CreatedAt, created_by as CreatedBy, modified_at as ModifiedAt, modified_by as ModifiedBy";

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            $"select top (1) {Selected} from reg.users where email = @email order by subject",
            new { email }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<UserRecord?> GetAsync(string subject, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"select {Selected} from reg.users where subject = @subject",
            new { subject }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"select {Selected} from reg.users order by subject",
            transaction: transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToRecord).ToList();
    }

    public Task UpsertAsync(UserRecord user, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            $"""
            begin try
                update reg.users set email = @email, display_name = @displayName, status = @status, modified_at = @modifiedAt, modified_by = @modifiedBy where subject = @subject;
                if @@rowcount = 0
                    insert into reg.users ({Columns}) values (@subject, @email, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
                update reg.users set email = @email, display_name = @displayName, status = @status, modified_at = @modifiedAt, modified_by = @modifiedBy where subject = @subject;
            end catch
            """,
            new
            {
                subject = user.Subject,
                email = user.Email,
                displayName = user.DisplayName,
                status = Names.Of(user.Status),
                createdAt = Names.Utc(user.Audit.Created.At),
                createdBy = user.Audit.Created.By.Value,
                modifiedAt = Names.Utc(user.Audit.Modified.At),
                modifiedBy = user.Audit.Modified.By.Value,
            }, transaction(), cancellationToken: cancellationToken));

    private static UserRecord ToRecord(Row row)
        => new(row.Subject, row.Email, row.DisplayName, Names.UserStatusOf(row.Status),
            Names.AuditOf(row.CreatedAt, row.CreatedBy, row.ModifiedAt, row.ModifiedBy));
}

/// <summary>The rows of <c>reg.operations</c>: the catalogue of what can be granted.</summary>
internal sealed class CatalogRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    public async Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<(string Module, string Operation, string Kind, bool Declared, bool Hardened)>(new CommandDefinition(
            "select module, operation, kind, declared_second_factor, hardened_second_factor from reg.operations order by module, operation",
            transaction: transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(row => new CatalogEntry(row.Module, row.Operation, Names.KindOf(row.Kind), row.Declared, row.Hardened)).ToList();
    }

    /// <summary>Inserts each entry, or replaces its module, kind and declared second factor — Dapper runs the statement once per entry, in the transaction of the store.</summary>
    public Task UpsertAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            begin try
                update reg.operations set module = @module, kind = @kind, declared_second_factor = @declared where operation = @operation;
                if @@rowcount = 0
                    insert into reg.operations (operation, module, kind, declared_second_factor, hardened_second_factor) values (@operation, @module, @kind, @declared, 0);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
                update reg.operations set module = @module, kind = @kind, declared_second_factor = @declared where operation = @operation;
            end catch
            """,
            entries.Select(e => new { operation = e.Operation, module = e.Module, kind = Names.Of(e.Kind), declared = e.DeclaredSecondFactor }).ToList(),
            transaction(), cancellationToken: cancellationToken));

    public Task HardenAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "update reg.operations set hardened_second_factor = 1 where operation in @operations",
            new { operations = operations.ToArray() }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.roles</c> and <c>reg.role_operations</c>.</summary>
internal sealed class RoleRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(
        string Role, string Scope, string Category, string DisplayName, bool Declared, string AssignsCompartment, bool AssignsPlatform,
        DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ModifiedAt, string ModifiedBy);

    private sealed record Pair(string Role, string Value);

    /// <summary>The roles with their operations and their assignable roles: one, those of a scope, or all, by name.</summary>
    public async Task<IReadOnlyList<Role>> ListAsync(string? name, RoleScope? scope, CancellationToken cancellationToken)
    {
        // Three reads rather than one join: a role's operations and its assignable roles would multiply each other.
        const string Filter = "(@name is null or r.role = @name) and (@scope is null or r.scope = @scope)";
        var parameters = new { name, scope = scope is { } s ? Names.Of(s) : null };
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            select r.role as Role, r.scope as Scope, r.category as Category, r.display_name as DisplayName, r.declared as Declared,
                   r.assigns_compartment as AssignsCompartment, r.assigns_platform as AssignsPlatform,
                   r.created_at as CreatedAt, r.created_by as CreatedBy, r.modified_at as ModifiedAt, r.modified_by as ModifiedBy
            from reg.roles r
            where {Filter}
            order by r.role
            """,
            parameters, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        var operations = (await connection.QueryAsync<Pair>(new CommandDefinition(
            $"select o.role as Role, o.operation as Value from reg.role_operations o join reg.roles r on r.role = o.role where {Filter} order by o.operation",
            parameters, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false)).ToLookup(pair => pair.Role, pair => pair.Value, StringComparer.Ordinal);
        var assignable = (await connection.QueryAsync<Pair>(new CommandDefinition(
            $"select a.role as Role, a.assignable_role as Value from reg.role_assignable_roles a join reg.roles r on r.role = a.role where {Filter} order by a.assignable_role",
            parameters, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false)).ToLookup(pair => pair.Role, pair => pair.Value, StringComparer.Ordinal);
        return rows
            .Select(row => new Role(row.Role, Names.ScopeOf(row.Scope), Names.CategoryOf(row.Category), row.DisplayName, row.Declared,
                operations[row.Role].ToList(),
                new AssignableRoles(Names.SelectorOf(row.AssignsCompartment), row.AssignsPlatform, assignable[row.Role].ToList()),
                Names.AuditOf(row.CreatedAt, row.CreatedBy, row.ModifiedAt, row.ModifiedBy)))
            .ToList();
    }

    public Task UpsertAsync(Role role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            begin try
                update reg.roles set scope = @scope, category = @category, display_name = @displayName, declared = @declared, assigns_compartment = @assignsCompartment, assigns_platform = @assignsPlatform, modified_at = @modifiedAt, modified_by = @modifiedBy where role = @role;
                if @@rowcount = 0
                    insert into reg.roles (role, scope, category, display_name, declared, assigns_compartment, assigns_platform, created_at, created_by, modified_at, modified_by) values (@role, @scope, @category, @displayName, @declared, @assignsCompartment, @assignsPlatform, @createdAt, @createdBy, @modifiedAt, @modifiedBy);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
                update reg.roles set scope = @scope, category = @category, display_name = @displayName, declared = @declared, assigns_compartment = @assignsCompartment, assigns_platform = @assignsPlatform, modified_at = @modifiedAt, modified_by = @modifiedBy where role = @role;
            end catch
            """,
            new
            {
                role = role.Name,
                scope = Names.Of(role.Scope),
                category = Names.Of(role.Category),
                displayName = role.DisplayName,
                declared = role.Declared,
                assignsCompartment = Names.Of(role.Assignable.Compartment),
                assignsPlatform = role.Assignable.AllPlatform,
                createdAt = Names.Utc(role.Audit.Created.At),
                createdBy = role.Audit.Created.By.Value,
                modifiedAt = Names.Utc(role.Audit.Modified.At),
                modifiedBy = role.Audit.Modified.By.Value,
            },
            transaction(), cancellationToken: cancellationToken));

    public Task DeleteOperationsAsync(string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.role_operations where role = @role",
            new { role }, transaction(), cancellationToken: cancellationToken));

    public Task InsertOperationsAsync(string role, string[] operations, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.role_operations (role, operation) values (@role, @operation)
            """,
            operations.Select(operation => new { role, operation }).ToList(), transaction(), cancellationToken: cancellationToken));

    public Task DeleteAssignableAsync(string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.role_assignable_roles where role = @role",
            new { role }, transaction(), cancellationToken: cancellationToken));

    public Task InsertAssignableAsync(string role, string[] roles, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.role_assignable_roles (role, assignable_role) values (@role, @assignable)
            """,
            roles.Select(assignable => new { role, assignable }).ToList(), transaction(), cancellationToken: cancellationToken));

    /// <summary>Deletes the role; its operations go with it, and a role still held is refused by the schema.</summary>
    public Task DeleteAsync(string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.roles where role = @role",
            new { role }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.memberships</c> and <c>reg.member_roles</c>.</summary>
internal sealed class MembershipRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Subject, string Compartment, DateTimeOffset GrantedAt, string GrantedBy, string? Role);

    public Task<bool> ExistsAsync(string subject, string compartment, CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select case when exists (select 1 from reg.memberships where subject = @subject and compartment = @compartment) then cast(1 as bit) else cast(0 as bit) end",
            new { subject, compartment }, transaction(), cancellationToken: cancellationToken));

    /// <summary>
    /// The memberships with their roles: those of a user, those of a compartment, or those holding a role — in
    /// which case every role of the membership comes back, not only the one asked.
    /// </summary>
    public async Task<IReadOnlyList<Membership>> ListAsync(string? subject, string? compartment, string? role, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            select m.subject as Subject, m.compartment as Compartment, m.granted_at as GrantedAt, m.granted_by as GrantedBy, r.role as Role
            from reg.memberships m
            left join reg.member_roles r on r.subject = m.subject and r.compartment = m.compartment
            where (@subject is null or m.subject = @subject)
              and (@compartment is null or m.compartment = @compartment)
              and (@role is null or exists (
                    select 1 from reg.member_roles h
                    where h.subject = m.subject and h.compartment = m.compartment and h.role = @role))
            order by m.subject, m.compartment, r.role
            """,
            new { subject, compartment, role }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows
            .GroupBy(row => (row.Subject, row.Compartment))
            .Select(group =>
            {
                var first = group.First();
                return new Membership(group.Key.Subject, Compartment.Parse(group.Key.Compartment),
                    group.Where(row => row.Role is not null).Select(row => row.Role!).ToList(),
                    new Stamp(Names.At(first.GrantedAt), Actor.Parse(first.GrantedBy)));
            })
            .ToList();
    }

    public Task InsertAsync(string subject, string compartment, Stamp granted, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            begin try
                insert into reg.memberships (subject, compartment, granted_at, granted_by) select @subject, @compartment, @grantedAt, @grantedBy where not exists (select 1 from reg.memberships where subject = @subject and compartment = @compartment);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
            end catch
            """,
            new { subject, compartment, grantedAt = Names.Utc(granted.At), grantedBy = granted.By.Value }, transaction(), cancellationToken: cancellationToken));

    /// <summary>Deletes the membership; its roles go with it.</summary>
    public Task DeleteAsync(string subject, string compartment, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.memberships where subject = @subject and compartment = @compartment",
            new { subject, compartment }, transaction(), cancellationToken: cancellationToken));

    public Task InsertRoleAsync(string subject, string compartment, string role, Stamp granted, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            begin try
                insert into reg.member_roles (subject, compartment, role, granted_at, granted_by) select @subject, @compartment, @role, @grantedAt, @grantedBy where not exists (select 1 from reg.member_roles where subject = @subject and compartment = @compartment and role = @role);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
            end catch
            """,
            new { subject, compartment, role, grantedAt = Names.Utc(granted.At), grantedBy = granted.By.Value }, transaction(), cancellationToken: cancellationToken));

    public Task DeleteRoleAsync(string subject, string compartment, string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.member_roles where subject = @subject and compartment = @compartment and role = @role",
            new { subject, compartment, role }, transaction(), cancellationToken: cancellationToken));

    /// <summary>Deletes every membership of the compartment; their roles go with them.</summary>
    public Task DeleteByCompartmentAsync(string compartment, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.memberships where compartment = @compartment",
            new { compartment }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.operator_roles</c>: the platform roles of the users.</summary>
internal sealed class OperatorRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    public async Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "select role from reg.operator_roles where subject = @subject order by role",
            new { subject }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<(string Subject, string Role)>(new CommandDefinition(
            "select subject, role from reg.operator_roles order by subject, role",
            transaction: transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows
            .GroupBy(row => row.Subject, StringComparer.Ordinal)
            .Select(group => (group.Key, (IReadOnlyCollection<string>)group.Select(row => row.Role).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListByRoleAsync(string role, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "select subject from reg.operator_roles where role = @role order by subject",
            new { role }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.ToList();
    }

    public Task InsertAsync(string subject, string role, Stamp granted, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            begin try
                insert into reg.operator_roles (subject, role, granted_at, granted_by) select @subject, @role, @grantedAt, @grantedBy where not exists (select 1 from reg.operator_roles where subject = @subject and role = @role);
            end try
            begin catch
                if error_number() not in (2601, 2627) throw;
            end catch
            """,
            new { subject, role, grantedAt = Names.Utc(granted.At), grantedBy = granted.By.Value }, transaction(), cancellationToken: cancellationToken));

    public Task DeleteAsync(string subject, string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.operator_roles where subject = @subject and role = @role",
            new { subject, role }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.invitations</c> and <c>reg.invitation_roles</c>.</summary>
internal sealed class InvitationRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(
        Guid InvitationId, string TokenHash, string Compartment, string Email, bool DesignatesOwner, string Delivery,
        DateTimeOffset IssuedAt, string IssuedBy, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string? AcceptedBy, DateTimeOffset? RevokedAt, string? RevokedBy, string? Role);

    private const string Select = """
        select i.invitation_id as InvitationId, i.token_hash as TokenHash, i.compartment as Compartment, i.email as Email,
               i.designates_owner as DesignatesOwner, i.delivery as Delivery, i.issued_at as IssuedAt, i.issued_by as IssuedBy,
               i.expires_at as ExpiresAt, i.accepted_at as AcceptedAt, i.accepted_by as AcceptedBy, i.revoked_at as RevokedAt,
               i.revoked_by as RevokedBy, r.role as Role
        from reg.invitations i
        left join reg.invitation_roles r on r.invitation_id = i.invitation_id
        """;

    public async Task<IReadOnlyList<Invitation>> ListAsync(string where, object parameters, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"{Select} where {where} order by i.issued_at desc, i.invitation_id, r.role",
            parameters, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.GroupBy(row => row.InvitationId).Select(group =>
        {
            var first = group.First();
            return new Invitation(first.InvitationId, first.TokenHash, Compartment.Parse(first.Compartment), first.Email,
                group.Where(row => row.Role is not null).Select(row => row.Role!).ToList(), first.DesignatesOwner,
                first.Delivery == "hand" ? InvitationDelivery.Hand : InvitationDelivery.Email,
                new Stamp(Utc(first.IssuedAt), Actor.Parse(first.IssuedBy)), Utc(first.ExpiresAt),
                first.AcceptedAt is { } accepted ? Utc(accepted) : null, first.AcceptedBy,
                first.RevokedAt is { } revoked ? new Stamp(Utc(revoked), Actor.Parse(first.RevokedBy!)) : null);
        }).ToList();
    }

    public async Task InsertAsync(Invitation invitation, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at)
            values (@id, @hash, @compartment, @email, @designatesOwner, @delivery, @issuedAt, @issuedBy, @expiresAt)
            """,
            new
            {
                id = invitation.InvitationId,
                hash = invitation.TokenHash,
                compartment = invitation.Compartment.Value,
                email = invitation.Email,
                designatesOwner = invitation.DesignatesOwner,
                delivery = invitation.Delivery == InvitationDelivery.Hand ? "hand" : "email",
                issuedAt = Names.Utc(invitation.Issued.At),
                issuedBy = invitation.Issued.By.Value,
                expiresAt = Names.Utc(invitation.ExpiresAt),
            }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (invitation.Roles.Count > 0)
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into reg.invitation_roles (invitation_id, role) values (@id, @role)",
                invitation.Roles.Distinct(StringComparer.Ordinal).Select(role => new { id = invitation.InvitationId, role }).ToList(),
                transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(Guid id, Stamp revoked, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition(
            "update reg.invitations set revoked_at = @at, revoked_by = @by where invitation_id = @id and accepted_at is null and revoked_at is null",
            new { id, at = Names.Utc(revoked.At), by = revoked.By.Value }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    // The token is spent atomically: of two acceptances at once, one updates the row.
    public async Task<bool> TryAcceptAsync(string hash, string subject, DateTimeOffset at, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition(
            """
            update reg.invitations set accepted_at = @at, accepted_by = @subject
            where token_hash = @hash and accepted_at is null and revoked_at is null and expires_at > @at
            """,
            new { hash, subject, at = Names.Utc(at) }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    public Task<int> DeleteDeadBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.invitations where accepted_at is null and (expires_at < @before or revoked_at < @before)",
            new { before = Names.Utc(before) }, transaction(), cancellationToken: cancellationToken));

    private static DateTimeOffset Utc(DateTimeOffset value) => value;
}

/// <summary>The rows of <c>reg.pending_acts</c>.</summary>
internal sealed class PendingActRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(
        Guid ActId, string Kind, string? Subject, string? Role, string? Compartment, string? Definition, DateTimeOffset ProposedAt, string ProposedBy,
        DateTimeOffset ExpiresAt, DateTimeOffset? ValidatedAt, string? ValidatedBy, DateTimeOffset? RejectedAt, string? RejectedBy);

    // The definition as JSON, in the names of the routes.
    private sealed record DefinitionJson(string Scope, string Category, string DisplayName, string[] Operations, string AssignsCompartment, bool AssignsPlatform, string[] AssignableRoles);

    private const string Select = """
        select act_id as ActId, kind as Kind, subject as Subject, role as Role, compartment as Compartment, definition as Definition,
               proposed_at as ProposedAt, proposed_by as ProposedBy, expires_at as ExpiresAt, validated_at as ValidatedAt,
               validated_by as ValidatedBy, rejected_at as RejectedAt, rejected_by as RejectedBy
        from reg.pending_acts
        """;

    public async Task<IReadOnlyList<PendingAct>> ListAsync(Guid? actId, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"{Select} where (@actId is null or act_id = @actId) order by proposed_at desc, act_id",
            new { actId }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToAct).ToList();
    }

    public Task InsertAsync(PendingAct act, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.pending_acts (act_id, kind, subject, role, compartment, definition, proposed_at, proposed_by, expires_at)
            values (@id, @kind, @subject, @role, @compartment, @definition, @proposedAt, @proposedBy, @expiresAt)
            """,
            new
            {
                id = act.ActId,
                kind = KindOf(act.Kind),
                subject = act.Subject,
                role = act.Role,
                compartment = act.Compartment?.Value,
                definition = act.Definition is { } definition ? JsonSerializer.Serialize(ToJson(act.Scope!.Value, definition)) : null,
                proposedAt = Names.Utc(act.Proposed.At),
                proposedBy = act.Proposed.By.Value,
                expiresAt = Names.Utc(act.ExpiresAt),
            }, transaction(), cancellationToken: cancellationToken));

    public async Task<bool> CloseAsync(Guid id, bool validated, Stamp closed, CancellationToken cancellationToken)
        => await connection.ExecuteAsync(new CommandDefinition(
            validated
                ? "update reg.pending_acts set validated_at = @at, validated_by = @by where act_id = @id and validated_at is null and rejected_at is null"
                : "update reg.pending_acts set rejected_at = @at, rejected_by = @by where act_id = @id and validated_at is null and rejected_at is null",
            new { id, at = Names.Utc(closed.At), by = closed.By.Value }, transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false) == 1;

    private static string KindOf(PendingActKind kind) => kind switch
    {
        PendingActKind.PlatformRole => "platform_role",
        PendingActKind.RoleDefinition => "role_definition",
        PendingActKind.OwnershipRescue => "ownership_rescue",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown act."),
    };

    private static PendingAct ToAct(Row row)
    {
        var kind = row.Kind switch
        {
            "platform_role" => PendingActKind.PlatformRole,
            "role_definition" => PendingActKind.RoleDefinition,
            "ownership_rescue" => PendingActKind.OwnershipRescue,
            _ => throw new InvalidOperationException($"reg.pending_acts holds the kind '{row.Kind}', which this package does not know."),
        };
        var json = row.Definition is null ? null : JsonSerializer.Deserialize<DefinitionJson>(row.Definition);
        return new PendingAct(row.ActId, kind, row.Subject, row.Role, row.Compartment is null ? null : Compartment.Parse(row.Compartment),
            json is null ? null : Names.ScopeOf(json.Scope),
            json is null ? null : new RoleDefinition(Names.CategoryOf(json.Category), json.DisplayName, json.Operations,
                new AssignableRoles(Names.SelectorOf(json.AssignsCompartment), json.AssignsPlatform, json.AssignableRoles)),
            new Stamp(Utc(row.ProposedAt), Actor.Parse(row.ProposedBy)), Utc(row.ExpiresAt),
            row.ValidatedAt is { } validated ? new Stamp(Utc(validated), Actor.Parse(row.ValidatedBy!)) : null,
            row.RejectedAt is { } rejected ? new Stamp(Utc(rejected), Actor.Parse(row.RejectedBy!)) : null);
    }

    private static DefinitionJson ToJson(RoleScope scope, RoleDefinition definition)
    {
        var assignable = definition.Assignable ?? Stellaegis.Registry.AssignableRoles.None;
        return new DefinitionJson(Names.Of(scope), Names.Of(definition.Category), definition.DisplayName, definition.Operations.ToArray(),
            Names.Of(assignable.Compartment), assignable.AllPlatform, assignable.Roles.ToArray());
    }

    private static DateTimeOffset Utc(DateTimeOffset value) => value;
}
