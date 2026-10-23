using System.Data.Common;
using Dapper;
using Stellaegis.Primitives;

namespace Stellaegis.Registry.PostgreSQL;

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

    /// <summary>A timestamp as the column holds it: UTC, which <c>timestamptz</c> requires of a parameter.</summary>
    public static DateTime Utc(DateTimeOffset at) => at.UtcDateTime;

    /// <summary>A timestamp as the record carries it, from the UTC the column gives back.</summary>
    public static DateTimeOffset At(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    /// <summary>The trace of a row, from its four audit columns.</summary>
    public static Audit AuditOf(DateTime createdAt, string createdBy, DateTime modifiedAt, string modifiedBy)
        => new(new Stamp(At(createdAt), Actor.Parse(createdBy)), new Stamp(At(modifiedAt), Actor.Parse(modifiedBy)));
}

/// <summary>The rows of <c>reg.compartments</c>, read and written on the connection of a unit of work.</summary>
internal sealed class CompartmentRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Compartment, string DisplayName, string Status, DateTime CreatedAt, string CreatedBy, DateTime ModifiedAt, string ModifiedBy);

    private const string Columns = "compartment, display_name, status, created_at, created_by, modified_at, modified_by";

    // Dapper matches a positional record by the names of its parameters: the columns are read under those names.
    private const string Selected = "compartment as Compartment, display_name as DisplayName, status as Status, created_at as CreatedAt, created_by as CreatedBy, modified_at as ModifiedAt, modified_by as ModifiedBy";

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
            insert into reg.compartments ({Columns})
            values (@compartment, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (compartment) do update set
              display_name = excluded.display_name, status = excluded.status,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
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

    private static CompartmentRecord ToRecord(Row row)
        => new(Compartment.Parse(row.Compartment), row.DisplayName, Names.CompartmentStatusOf(row.Status),
            Names.AuditOf(row.CreatedAt, row.CreatedBy, row.ModifiedAt, row.ModifiedBy));
}

/// <summary>The rows of <c>reg.users</c>: the projection of the accounts of Identity.</summary>
internal sealed class UserRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Subject, string Email, string DisplayName, string Status, DateTime CreatedAt, string CreatedBy, DateTime ModifiedAt, string ModifiedBy);

    private const string Columns = "subject, email, display_name, status, created_at, created_by, modified_at, modified_by";

    private const string Selected = "subject as Subject, email as Email, display_name as DisplayName, status as Status, created_at as CreatedAt, created_by as CreatedBy, modified_at as ModifiedAt, modified_by as ModifiedBy";

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
            insert into reg.users ({Columns})
            values (@subject, @email, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (subject) do update set
              email = excluded.email, display_name = excluded.display_name, status = excluded.status,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
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
        var rows = await connection.QueryAsync<(string Module, string Operation, string Kind)>(new CommandDefinition(
            "select module, operation, kind from reg.operations order by module, operation",
            transaction: transaction(), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(row => new CatalogEntry(row.Module, row.Operation, Names.KindOf(row.Kind))).ToList();
    }

    /// <summary>Inserts each entry, or replaces its module and kind — one statement, so one transaction.</summary>
    public Task UpsertAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.operations (operation, module, kind)
            select * from unnest(@operations::text[], @modules::text[], @kinds::text[])
            on conflict (operation) do update set module = excluded.module, kind = excluded.kind
            """,
            new
            {
                operations = entries.Select(e => e.Operation).ToArray(),
                modules = entries.Select(e => e.Module).ToArray(),
                kinds = entries.Select(e => Names.Of(e.Kind)).ToArray(),
            }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.roles</c> and <c>reg.role_operations</c>.</summary>
internal sealed class RoleRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(
        string Role, string Scope, string Category, string DisplayName, bool Declared, string AssignsCompartment, bool AssignsPlatform,
        bool RequiresSecondFactor, DateTime CreatedAt, string CreatedBy, DateTime ModifiedAt, string ModifiedBy);

    private sealed record Pair(string Role, string Value);

    /// <summary>The roles with their operations and their assignable roles: one, those of a scope, or all, by name.</summary>
    public async Task<IReadOnlyList<Role>> ListAsync(string? name, RoleScope? scope, CancellationToken cancellationToken)
    {
        // Three reads rather than one join: a role's operations and its assignable roles would multiply each other.
        const string Filter = "(@name::text is null or r.role = @name) and (@scope::text is null or r.scope = @scope)";
        var parameters = new { name, scope = scope is { } s ? Names.Of(s) : null };
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            select r.role as Role, r.scope as Scope, r.category as Category, r.display_name as DisplayName, r.declared as Declared,
                   r.assigns_compartment as AssignsCompartment, r.assigns_platform as AssignsPlatform, r.requires_second_factor as RequiresSecondFactor,
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
                row.RequiresSecondFactor,
                Names.AuditOf(row.CreatedAt, row.CreatedBy, row.ModifiedAt, row.ModifiedBy)))
            .ToList();
    }

    public Task UpsertAsync(Role role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.roles (role, scope, category, display_name, declared, assigns_compartment, assigns_platform, requires_second_factor,
                                   created_at, created_by, modified_at, modified_by)
            values (@role, @scope, @category, @displayName, @declared, @assignsCompartment, @assignsPlatform, @requiresSecondFactor,
                    @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (role) do update set
              scope = excluded.scope, category = excluded.category, display_name = excluded.display_name, declared = excluded.declared,
              assigns_compartment = excluded.assigns_compartment, assigns_platform = excluded.assigns_platform,
              requires_second_factor = excluded.requires_second_factor,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
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
                requiresSecondFactor = role.RequiresSecondFactor,
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
            insert into reg.role_operations (role, operation)
            select @role, unnest(@operations::text[])
            """,
            new { role, operations }, transaction(), cancellationToken: cancellationToken));

    public Task DeleteAssignableAsync(string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.role_assignable_roles where role = @role",
            new { role }, transaction(), cancellationToken: cancellationToken));

    public Task InsertAssignableAsync(string role, string[] roles, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            insert into reg.role_assignable_roles (role, assignable_role)
            select @role, unnest(@roles::text[])
            """,
            new { role, roles }, transaction(), cancellationToken: cancellationToken));

    /// <summary>Deletes the role; its operations go with it, and a role still held is refused by the schema.</summary>
    public Task DeleteAsync(string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.roles where role = @role",
            new { role }, transaction(), cancellationToken: cancellationToken));
}

/// <summary>The rows of <c>reg.memberships</c> and <c>reg.member_roles</c>.</summary>
internal sealed class MembershipRepository(DbConnection connection, Func<DbTransaction?> transaction)
{
    private sealed record Row(string Subject, string Compartment, DateTime GrantedAt, string GrantedBy, string? Role);

    public Task<bool> ExistsAsync(string subject, string compartment, CancellationToken cancellationToken)
        => connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists (select 1 from reg.memberships where subject = @subject and compartment = @compartment)",
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
            where (@subject::text is null or m.subject = @subject)
              and (@compartment::text is null or m.compartment = @compartment)
              and (@role::text is null or exists (
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
            insert into reg.memberships (subject, compartment, granted_at, granted_by)
            values (@subject, @compartment, @grantedAt, @grantedBy) on conflict do nothing
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
            insert into reg.member_roles (subject, compartment, role, granted_at, granted_by)
            values (@subject, @compartment, @role, @grantedAt, @grantedBy) on conflict do nothing
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
            insert into reg.operator_roles (subject, role, granted_at, granted_by)
            values (@subject, @role, @grantedAt, @grantedBy) on conflict do nothing
            """,
            new { subject, role, grantedAt = Names.Utc(granted.At), grantedBy = granted.By.Value }, transaction(), cancellationToken: cancellationToken));

    public Task DeleteAsync(string subject, string role, CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            "delete from reg.operator_roles where subject = @subject and role = @role",
            new { subject, role }, transaction(), cancellationToken: cancellationToken));
}
