using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
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

    /// <summary>A timestamp as the column holds it: in UTC, the only offset Npgsql writes to a <c>timestamptz</c>.</summary>
    public static DateTimeOffset Utc(DateTimeOffset at) => at.ToUniversalTime();

    /// <summary>A timestamp as the record carries it.</summary>
    public static DateTimeOffset At(DateTimeOffset at) => at;

    /// <summary>The trace of a row, from its four audit columns.</summary>
    public static Audit AuditOf(NpgsqlDataReader row) => new(
        new Stamp(At(row.GetFieldValue<DateTimeOffset>("created_at")), Actor.Parse(row.GetString("created_by"))),
        new Stamp(At(row.GetFieldValue<DateTimeOffset>("modified_at")), Actor.Parse(row.GetString("modified_by"))));
}

/// <summary>The rows of <c>reg.compartments</c>, read and written on the connection of a unit of work.</summary>
internal sealed class CompartmentRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    private const string Columns = "compartment, display_name, status, created_at, created_by, modified_at, modified_by";

    public async Task<CompartmentRecord?> GetAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {Columns}, owner_subject from reg.compartments where compartment = @compartment",
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {Columns}, owner_subject from reg.compartments order by compartment",
            connection, transaction());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var compartments = new List<CompartmentRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            compartments.Add(Read(reader));
        return compartments;
    }

    public async Task UpsertAsync(CompartmentRecord compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into reg.compartments ({Columns})
            values (@compartment, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (compartment) do update set
              display_name = excluded.display_name, status = excluded.status,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
            """,
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment.Compartment.Value;
        command.Parameters.Add("displayName", NpgsqlDbType.Text).Value = compartment.DisplayName;
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = Names.Of(compartment.Status);
        command.Parameters.Add("createdAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(compartment.Audit.Created.At);
        command.Parameters.Add("createdBy", NpgsqlDbType.Text).Value = compartment.Audit.Created.By.Value;
        command.Parameters.Add("modifiedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(compartment.Audit.Modified.At);
        command.Parameters.Add("modifiedBy", NpgsqlDbType.Text).Value = compartment.Audit.Modified.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Seats the owner when there is none; whether it was seated.</summary>
    public async Task<bool> SetOwnerIfNoneAsync(string compartment, string owner, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "update reg.compartments set owner_subject = @owner where compartment = @compartment and owner_subject is null",
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        command.Parameters.Add("owner", NpgsqlDbType.Text).Value = owner;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>Replaces the owner when it is still <paramref name="from"/>; whether it was replaced.</summary>
    public async Task<bool> ReplaceOwnerAsync(string compartment, string from, string to, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "update reg.compartments set owner_subject = @to where compartment = @compartment and owner_subject = @from",
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        command.Parameters.Add("from", NpgsqlDbType.Text).Value = from;
        command.Parameters.Add("to", NpgsqlDbType.Text).Value = to;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    // One row per role kept, or one row without a role: the transfer is read from the first, the roles from each.
    public async Task<OwnershipTransfer?> GetTransferAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select t.compartment, t.from_subject, t.to_subject, t.proposed_at, t.proposed_by, t.expires_at, k.role
            from reg.ownership_transfers t
            left join reg.ownership_transfer_kept_roles k on k.compartment = t.compartment
            where t.compartment = @compartment
            order by k.role
            """,
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        (string Compartment, string From, string To, DateTimeOffset ProposedAt, string ProposedBy, DateTimeOffset ExpiresAt)? transfer = null;
        var kept = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            transfer ??= (reader.GetString("compartment"), reader.GetString("from_subject"), reader.GetString("to_subject"),
                reader.GetFieldValue<DateTimeOffset>("proposed_at"), reader.GetString("proposed_by"), reader.GetFieldValue<DateTimeOffset>("expires_at"));
            if (!reader.IsDBNull("role"))
                kept.Add(reader.GetString("role"));
        }
        return transfer is { } first
            ? new OwnershipTransfer(Compartment.Parse(first.Compartment), first.From, first.To, kept,
                new Stamp(first.ProposedAt, Actor.Parse(first.ProposedBy)), first.ExpiresAt)
            : null;
    }

    public async Task SaveTransferAsync(OwnershipTransfer transfer, CancellationToken cancellationToken)
    {
        await DeleteTransferAsync(transfer.Compartment.Value, cancellationToken).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            """
            insert into reg.ownership_transfers (compartment, from_subject, to_subject, proposed_at, proposed_by, expires_at)
            values (@compartment, @from, @to, @proposedAt, @proposedBy, @expiresAt)
            """,
            connection, transaction()))
        {
            command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = transfer.Compartment.Value;
            command.Parameters.Add("from", NpgsqlDbType.Text).Value = transfer.From;
            command.Parameters.Add("to", NpgsqlDbType.Text).Value = transfer.To;
            command.Parameters.Add("proposedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(transfer.Proposed.At);
            command.Parameters.Add("proposedBy", NpgsqlDbType.Text).Value = transfer.Proposed.By.Value;
            command.Parameters.Add("expiresAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(transfer.ExpiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (transfer.Kept.Count == 0)
            return;
        await using var kept = new NpgsqlCommand(
            "insert into reg.ownership_transfer_kept_roles (compartment, role) select @compartment, unnest(@roles::text[])",
            connection, transaction());
        kept.Parameters.Add("compartment", NpgsqlDbType.Text).Value = transfer.Compartment.Value;
        kept.Parameters.Add("roles", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = transfer.Kept.Distinct(StringComparer.Ordinal).ToArray();
        await kept.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Seats <paramref name="owner"/> whoever owns the compartment now, in the transaction of the caller; the former owner, or <see langword="null"/>.</summary>
    public async Task<string?> ReplaceOwnerAnyAsync(string compartment, string owner, CancellationToken cancellationToken)
    {
        string? previous = null;
        await using (var select = new NpgsqlCommand(
            "select owner_subject from reg.compartments where compartment = @compartment for update",
            connection, transaction()))
        {
            select.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull("owner_subject"))
                previous = reader.GetString("owner_subject");
        }

        await using var update = new NpgsqlCommand(
            "update reg.compartments set owner_subject = @owner where compartment = @compartment",
            connection, transaction());
        update.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        update.Parameters.Add("owner", NpgsqlDbType.Text).Value = owner;
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return previous;
    }

    public async Task<bool> ExistsAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from reg.compartments where compartment = @compartment)",
            connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task DeleteTransferAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.ownership_transfers where compartment = @compartment", connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CompartmentRecord Read(NpgsqlDataReader row)
        => new(Compartment.Parse(row.GetString("compartment")), row.GetString("display_name"), Names.CompartmentStatusOf(row.GetString("status")),
            Names.AuditOf(row), row.IsDBNull("owner_subject") ? null : row.GetString("owner_subject"));
}

/// <summary>The rows of <c>reg.users</c>: the projection of the accounts of Identity.</summary>
internal sealed class UserRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    private const string Columns = "subject, email, display_name, status, created_at, created_by, modified_at, modified_by";

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {Columns} from reg.users where lower(email) = lower(@email) order by subject limit 1",
            connection, transaction());
        command.Parameters.Add("email", NpgsqlDbType.Text).Value = email;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<UserRecord?> GetAsync(string subject, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {Columns} from reg.users where subject = @subject",
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {Columns} from reg.users order by subject",
            connection, transaction());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var users = new List<UserRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            users.Add(Read(reader));
        return users;
    }

    public async Task UpsertAsync(UserRecord user, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into reg.users ({Columns})
            values (@subject, @email, @displayName, @status, @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (subject) do update set
              email = excluded.email, display_name = excluded.display_name, status = excluded.status,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
            """,
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = user.Subject;
        command.Parameters.Add("email", NpgsqlDbType.Text).Value = user.Email;
        command.Parameters.Add("displayName", NpgsqlDbType.Text).Value = user.DisplayName;
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = Names.Of(user.Status);
        command.Parameters.Add("createdAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(user.Audit.Created.At);
        command.Parameters.Add("createdBy", NpgsqlDbType.Text).Value = user.Audit.Created.By.Value;
        command.Parameters.Add("modifiedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(user.Audit.Modified.At);
        command.Parameters.Add("modifiedBy", NpgsqlDbType.Text).Value = user.Audit.Modified.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static UserRecord Read(NpgsqlDataReader row)
        => new(row.GetString("subject"), row.GetString("email"), row.GetString("display_name"), Names.UserStatusOf(row.GetString("status")),
            Names.AuditOf(row));
}

/// <summary>The rows of <c>reg.operations</c>: the catalogue of what can be granted.</summary>
internal sealed class CatalogRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    public async Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select module, operation, kind, declared_second_factor, hardened_second_factor from reg.operations order by module, operation",
            connection, transaction());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<CatalogEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new CatalogEntry(reader.GetString("module"), reader.GetString("operation"), Names.KindOf(reader.GetString("kind")),
                reader.GetBoolean("declared_second_factor"), reader.GetBoolean("hardened_second_factor")));
        }
        return entries;
    }

    /// <summary>Inserts each entry, or replaces its module, kind and declared second factor — one statement, so one transaction.</summary>
    public async Task UpsertAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.operations (operation, module, kind, declared_second_factor, hardened_second_factor)
            select o, m, k, d, false from unnest(@operations::text[], @modules::text[], @kinds::text[], @declared::boolean[]) as t(o, m, k, d)
            on conflict (operation) do update set module = excluded.module, kind = excluded.kind, declared_second_factor = excluded.declared_second_factor
            """,
            connection, transaction());
        command.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = entries.Select(e => e.Operation).ToArray();
        command.Parameters.Add("modules", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = entries.Select(e => e.Module).ToArray();
        command.Parameters.Add("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = entries.Select(e => Names.Of(e.Kind)).ToArray();
        command.Parameters.Add("declared", NpgsqlDbType.Array | NpgsqlDbType.Boolean).Value = entries.Select(e => e.DeclaredSecondFactor).ToArray();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HardenAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "update reg.operations set hardened_second_factor = true where operation = any(@operations)",
            connection, transaction());
        command.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = operations.ToArray();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // The roles that hold any of the operations, before they lose them.
    public async Task<IReadOnlyList<string>> RolesHoldingAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select distinct role from reg.role_operations where operation = any(@operations) order by role",
            connection, transaction());
        command.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = operations.ToArray();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var roles = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            roles.Add(reader.GetString("role"));
        return roles;
    }

    // From the roles first: the catalogue is what their rows reference.
    public async Task RetireAsync(IReadOnlyCollection<string> operations, CancellationToken cancellationToken)
    {
        await using (var roles = new NpgsqlCommand(
            "delete from reg.role_operations where operation = any(@operations)",
            connection, transaction()))
        {
            roles.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = operations.ToArray();
            await roles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var catalogue = new NpgsqlCommand(
            "delete from reg.operations where operation = any(@operations)",
            connection, transaction());
        catalogue.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = operations.ToArray();
        await catalogue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The rows of <c>reg.roles</c> and <c>reg.role_operations</c>.</summary>
internal sealed class RoleRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    private const string Filter = "(@name::text is null or r.role = @name) and (@scope::text is null or r.scope = @scope)";

    /// <summary>The roles with their operations and their assignable roles: one, those of a scope, or all, by name.</summary>
    public async Task<IReadOnlyList<Role>> ListAsync(string? name, RoleScope? scope, CancellationToken cancellationToken)
    {
        // Three reads rather than one join: a role's operations and its assignable roles would multiply each other.
        var rows = new List<(string Name, RoleScope Scope, RoleCategory Category, string DisplayName, bool Declared, CompartmentRoleSelector AssignsCompartment, bool AssignsPlatform, Audit Audit)>();
        await using (var command = new NpgsqlCommand(
            $"""
            select r.role, r.scope, r.category, r.display_name, r.declared, r.assigns_compartment, r.assigns_platform,
                   r.created_at, r.created_by, r.modified_at, r.modified_by
            from reg.roles r
            where {Filter}
            order by r.role
            """,
            connection, transaction()))
        {
            AddFilter(command, name, scope);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((reader.GetString("role"), Names.ScopeOf(reader.GetString("scope")), Names.CategoryOf(reader.GetString("category")),
                    reader.GetString("display_name"), reader.GetBoolean("declared"), Names.SelectorOf(reader.GetString("assigns_compartment")),
                    reader.GetBoolean("assigns_platform"), Names.AuditOf(reader)));
            }
        }

        var operations = await ListValuesAsync(
            $"select o.role, o.operation as value from reg.role_operations o join reg.roles r on r.role = o.role where {Filter} order by o.operation",
            name, scope, cancellationToken).ConfigureAwait(false);
        var assignable = await ListValuesAsync(
            $"select a.role, a.assignable_role as value from reg.role_assignable_roles a join reg.roles r on r.role = a.role where {Filter} order by a.assignable_role",
            name, scope, cancellationToken).ConfigureAwait(false);
        return rows
            .Select(row => new Role(row.Name, row.Scope, row.Category, row.DisplayName, row.Declared,
                operations.GetValueOrDefault(row.Name, []),
                new AssignableRoles(row.AssignsCompartment, row.AssignsPlatform, assignable.GetValueOrDefault(row.Name, [])),
                row.Audit))
            .ToList();
    }

    public async Task UpsertAsync(Role role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.roles (role, scope, category, display_name, declared, assigns_compartment, assigns_platform,
                                   created_at, created_by, modified_at, modified_by)
            values (@role, @scope, @category, @displayName, @declared, @assignsCompartment, @assignsPlatform,
                    @createdAt, @createdBy, @modifiedAt, @modifiedBy)
            on conflict (role) do update set
              scope = excluded.scope, category = excluded.category, display_name = excluded.display_name, declared = excluded.declared,
              assigns_compartment = excluded.assigns_compartment, assigns_platform = excluded.assigns_platform,
              modified_at = excluded.modified_at, modified_by = excluded.modified_by
            """,
            connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role.Name;
        command.Parameters.Add("scope", NpgsqlDbType.Text).Value = Names.Of(role.Scope);
        command.Parameters.Add("category", NpgsqlDbType.Text).Value = Names.Of(role.Category);
        command.Parameters.Add("displayName", NpgsqlDbType.Text).Value = role.DisplayName;
        command.Parameters.Add("declared", NpgsqlDbType.Boolean).Value = role.Declared;
        command.Parameters.Add("assignsCompartment", NpgsqlDbType.Text).Value = Names.Of(role.Assignable.Compartment);
        command.Parameters.Add("assignsPlatform", NpgsqlDbType.Boolean).Value = role.Assignable.AllPlatform;
        command.Parameters.Add("createdAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(role.Audit.Created.At);
        command.Parameters.Add("createdBy", NpgsqlDbType.Text).Value = role.Audit.Created.By.Value;
        command.Parameters.Add("modifiedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(role.Audit.Modified.At);
        command.Parameters.Add("modifiedBy", NpgsqlDbType.Text).Value = role.Audit.Modified.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteOperationsAsync(string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.role_operations where role = @role", connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertOperationsAsync(string role, string[] operations, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "insert into reg.role_operations (role, operation) select @role, unnest(@operations::text[])",
            connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        command.Parameters.Add("operations", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = operations;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAssignableAsync(string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.role_assignable_roles where role = @role", connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertAssignableAsync(string role, string[] roles, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "insert into reg.role_assignable_roles (role, assignable_role) select @role, unnest(@roles::text[])",
            connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        command.Parameters.Add("roles", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = roles;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the role; its operations go with it, and a role still held is refused by the schema.</summary>
    public async Task DeleteAsync(string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.roles where role = @role", connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddFilter(NpgsqlCommand command, string? name, RoleScope? scope)
    {
        command.Parameters.Add("name", NpgsqlDbType.Text).Value = (object?)name ?? DBNull.Value;
        command.Parameters.Add("scope", NpgsqlDbType.Text).Value = scope is { } s ? Names.Of(s) : DBNull.Value;
    }

    // The values of the roles the filter keeps, by role, in the order the statement gives them.
    private async Task<Dictionary<string, List<string>>> ListValuesAsync(string sql, string? name, RoleScope? scope, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction());
        AddFilter(command, name, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var role = reader.GetString("role");
            if (!values.TryGetValue(role, out var list))
                values[role] = list = [];
            list.Add(reader.GetString("value"));
        }
        return values;
    }
}

/// <summary>The rows of <c>reg.memberships</c> and <c>reg.member_roles</c>.</summary>
internal sealed class MembershipRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    public async Task<bool> ExistsAsync(string subject, string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select exists (select 1 from reg.memberships where subject = @subject and compartment = @compartment)",
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// The memberships with their roles: those of a user, those of a compartment, or those holding a role — in
    /// which case every role of the membership comes back, not only the one asked.
    /// </summary>
    public async Task<IReadOnlyList<Membership>> ListAsync(string? subject, string? compartment, string? role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select m.subject, m.compartment, m.granted_at, m.granted_by, r.role
            from reg.memberships m
            left join reg.member_roles r on r.subject = m.subject and r.compartment = m.compartment
            where (@subject::text is null or m.subject = @subject)
              and (@compartment::text is null or m.compartment = @compartment)
              and (@role::text is null or exists (
                    select 1 from reg.member_roles h
                    where h.subject = m.subject and h.compartment = m.compartment and h.role = @role))
            order by m.subject, m.compartment, r.role
            """,
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = (object?)subject ?? DBNull.Value;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = (object?)compartment ?? DBNull.Value;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = (object?)role ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(string Subject, string Compartment, DateTimeOffset GrantedAt, string GrantedBy, string? Role)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add((reader.GetString("subject"), reader.GetString("compartment"), reader.GetFieldValue<DateTimeOffset>("granted_at"),
                reader.GetString("granted_by"), reader.IsDBNull("role") ? null : reader.GetString("role")));
        }
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

    public async Task InsertAsync(string subject, string compartment, Stamp granted, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.memberships (subject, compartment, granted_at, granted_by)
            values (@subject, @compartment, @grantedAt, @grantedBy) on conflict do nothing
            """,
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        command.Parameters.Add("grantedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(granted.At);
        command.Parameters.Add("grantedBy", NpgsqlDbType.Text).Value = granted.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the membership; its roles go with it.</summary>
    public async Task DeleteAsync(string subject, string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.memberships where subject = @subject and compartment = @compartment", connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertRoleAsync(string subject, string compartment, string role, Stamp granted, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.member_roles (subject, compartment, role, granted_at, granted_by)
            values (@subject, @compartment, @role, @grantedAt, @grantedBy) on conflict do nothing
            """,
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        command.Parameters.Add("grantedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(granted.At);
        command.Parameters.Add("grantedBy", NpgsqlDbType.Text).Value = granted.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(string subject, string compartment, string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.member_roles where subject = @subject and compartment = @compartment and role = @role", connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes every membership of the compartment; their roles go with them.</summary>
    public async Task DeleteByCompartmentAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.memberships where compartment = @compartment", connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The rows of <c>reg.operator_roles</c>: the platform roles of the users.</summary>
internal sealed class OperatorRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    public async Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select role from reg.operator_roles where subject = @subject order by role", connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var roles = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            roles.Add(reader.GetString("role"));
        return roles;
    }

    public async Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select subject, role from reg.operator_roles order by subject, role", connection, transaction());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(string Subject, string Role)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            rows.Add((reader.GetString("subject"), reader.GetString("role")));
        return rows
            .GroupBy(row => row.Subject, StringComparer.Ordinal)
            .Select(group => (group.Key, (IReadOnlyCollection<string>)group.Select(row => row.Role).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListByRoleAsync(string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select subject from reg.operator_roles where role = @role order by subject", connection, transaction());
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var subjects = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            subjects.Add(reader.GetString("subject"));
        return subjects;
    }

    public async Task InsertAsync(string subject, string role, Stamp granted, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.operator_roles (subject, role, granted_at, granted_by)
            values (@subject, @role, @grantedAt, @grantedBy) on conflict do nothing
            """,
            connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        command.Parameters.Add("grantedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(granted.At);
        command.Parameters.Add("grantedBy", NpgsqlDbType.Text).Value = granted.By.Value;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string subject, string role, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("delete from reg.operator_roles where subject = @subject and role = @role", connection, transaction());
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = role;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The rows of <c>reg.invitations</c> and <c>reg.invitation_roles</c>.</summary>
internal sealed class InvitationRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    private const string Select = """
        select i.invitation_id, i.token_hash, i.compartment, i.email, i.designates_owner, i.delivery, i.issued_at, i.issued_by,
               i.expires_at, i.accepted_at, i.accepted_by, i.revoked_at, i.revoked_by, r.role
        from reg.invitations i
        left join reg.invitation_roles r on r.invitation_id = i.invitation_id
        """;

    private const string Order = "order by i.issued_at desc, i.invitation_id, r.role";

    public async Task<Invitation?> FindAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{Select} where i.invitation_id = @invitationId {Order}", connection, transaction());
        command.Parameters.Add("invitationId", NpgsqlDbType.Uuid).Value = invitationId;
        return (await ReadAsync(command, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<Invitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{Select} where i.token_hash = @tokenHash {Order}", connection, transaction());
        command.Parameters.Add("tokenHash", NpgsqlDbType.Text).Value = tokenHash;
        return (await ReadAsync(command, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<Invitation>> ListByCompartmentAsync(string compartment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{Select} where i.compartment = @compartment {Order}", connection, transaction());
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = compartment;
        return await ReadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertAsync(Invitation invitation, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
            """
            insert into reg.invitations (invitation_id, token_hash, compartment, email, designates_owner, delivery, issued_at, issued_by, expires_at)
            values (@id, @hash, @compartment, @email, @designatesOwner, @delivery, @issuedAt, @issuedBy, @expiresAt)
            """,
            connection, transaction()))
        {
            command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = invitation.InvitationId;
            command.Parameters.Add("hash", NpgsqlDbType.Text).Value = invitation.TokenHash;
            command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = invitation.Compartment.Value;
            command.Parameters.Add("email", NpgsqlDbType.Text).Value = invitation.Email;
            command.Parameters.Add("designatesOwner", NpgsqlDbType.Boolean).Value = invitation.DesignatesOwner;
            command.Parameters.Add("delivery", NpgsqlDbType.Text).Value = invitation.Delivery == InvitationDelivery.Hand ? "hand" : "email";
            command.Parameters.Add("issuedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(invitation.Issued.At);
            command.Parameters.Add("issuedBy", NpgsqlDbType.Text).Value = invitation.Issued.By.Value;
            command.Parameters.Add("expiresAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(invitation.ExpiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (invitation.Roles.Count == 0)
            return;
        await using var roles = new NpgsqlCommand(
            "insert into reg.invitation_roles (invitation_id, role) select @id, unnest(@roles::text[])",
            connection, transaction());
        roles.Parameters.Add("id", NpgsqlDbType.Uuid).Value = invitation.InvitationId;
        roles.Parameters.Add("roles", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = invitation.Roles.Distinct(StringComparer.Ordinal).ToArray();
        await roles.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RevokeAsync(Guid id, Stamp revoked, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "update reg.invitations set revoked_at = @at, revoked_by = @by where invitation_id = @id and accepted_at is null and revoked_at is null",
            connection, transaction());
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        command.Parameters.Add("at", NpgsqlDbType.TimestampTz).Value = Names.Utc(revoked.At);
        command.Parameters.Add("by", NpgsqlDbType.Text).Value = revoked.By.Value;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    // The token is spent atomically: of two acceptances at once, one updates the row.
    public async Task<bool> TryAcceptAsync(string hash, string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            update reg.invitations set accepted_at = @at, accepted_by = @subject
            where token_hash = @hash and accepted_at is null and revoked_at is null and expires_at > @at
            """,
            connection, transaction());
        command.Parameters.Add("hash", NpgsqlDbType.Text).Value = hash;
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("at", NpgsqlDbType.TimestampTz).Value = Names.Utc(at);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> DeleteDeadBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "delete from reg.invitations where accepted_at is null and (expires_at < @before or revoked_at < @before)",
            connection, transaction());
        command.Parameters.Add("before", NpgsqlDbType.TimestampTz).Value = Names.Utc(before);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // One row per role of an invitation, or one row without a role.
    private static async Task<IReadOnlyList<Invitation>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<(Guid InvitationId, string TokenHash, string Compartment, string Email, bool DesignatesOwner, string Delivery,
            DateTimeOffset IssuedAt, string IssuedBy, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, string? AcceptedBy,
            DateTimeOffset? RevokedAt, string? RevokedBy, string? Role)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add((reader.GetGuid("invitation_id"), reader.GetString("token_hash"), reader.GetString("compartment"), reader.GetString("email"),
                reader.GetBoolean("designates_owner"), reader.GetString("delivery"),
                reader.GetFieldValue<DateTimeOffset>("issued_at"), reader.GetString("issued_by"), reader.GetFieldValue<DateTimeOffset>("expires_at"),
                reader.IsDBNull("accepted_at") ? null : reader.GetFieldValue<DateTimeOffset>("accepted_at"),
                reader.IsDBNull("accepted_by") ? null : reader.GetString("accepted_by"),
                reader.IsDBNull("revoked_at") ? null : reader.GetFieldValue<DateTimeOffset>("revoked_at"),
                reader.IsDBNull("revoked_by") ? null : reader.GetString("revoked_by"),
                reader.IsDBNull("role") ? null : reader.GetString("role")));
        }
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

    private static DateTimeOffset Utc(DateTimeOffset value) => value;
}

/// <summary>The rows of <c>reg.pending_acts</c>.</summary>
internal sealed class PendingActRepository(NpgsqlConnection connection, Func<NpgsqlTransaction?> transaction)
{
    // The definition as JSON, in the names of the routes.
    private sealed record DefinitionJson(string Scope, string Category, string DisplayName, string[] Operations, string AssignsCompartment, bool AssignsPlatform, string[] AssignableRoles);

    public async Task<IReadOnlyList<PendingAct>> ListAsync(Guid? actId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select act_id, kind, subject, role, compartment, definition::text, proposed_at, proposed_by, expires_at, validated_at,
                   validated_by, rejected_at, rejected_by, consumed_at
            from reg.pending_acts
            where (@actId::uuid is null or act_id = @actId) order by proposed_at desc, act_id
            """,
            connection, transaction());
        command.Parameters.Add("actId", NpgsqlDbType.Uuid).Value = (object?)actId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var acts = new List<PendingAct>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            acts.Add(Read(reader));
        return acts;
    }

    public async Task InsertAsync(PendingAct act, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into reg.pending_acts (act_id, kind, subject, role, compartment, definition, proposed_at, proposed_by, expires_at)
            values (@id, @kind, @subject, @role, @compartment, @definition::jsonb, @proposedAt, @proposedBy, @expiresAt)
            """,
            connection, transaction());
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = act.ActId;
        command.Parameters.Add("kind", NpgsqlDbType.Text).Value = KindOf(act.Kind);
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = (object?)act.Subject ?? DBNull.Value;
        command.Parameters.Add("role", NpgsqlDbType.Text).Value = (object?)act.Role ?? DBNull.Value;
        command.Parameters.Add("compartment", NpgsqlDbType.Text).Value = (object?)act.Compartment?.Value ?? DBNull.Value;
        command.Parameters.Add("definition", NpgsqlDbType.Text).Value = act.Definition is { } definition
            ? JsonSerializer.Serialize(ToJson(act.Scope!.Value, definition))
            : DBNull.Value;
        command.Parameters.Add("proposedAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(act.Proposed.At);
        command.Parameters.Add("proposedBy", NpgsqlDbType.Text).Value = act.Proposed.By.Value;
        command.Parameters.Add("expiresAt", NpgsqlDbType.TimestampTz).Value = Names.Utc(act.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CloseAsync(Guid id, bool validated, Stamp closed, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            validated
                ? "update reg.pending_acts set validated_at = @at, validated_by = @by where act_id = @id and validated_at is null and rejected_at is null"
                : "update reg.pending_acts set rejected_at = @at, rejected_by = @by where act_id = @id and validated_at is null and rejected_at is null",
            connection, transaction());
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        command.Parameters.Add("at", NpgsqlDbType.TimestampTz).Value = Names.Utc(closed.At);
        command.Parameters.Add("by", NpgsqlDbType.Text).Value = closed.By.Value;
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    // One statement: the act is validated, for this person and this author, open and not spent — then spent.
    public async Task<bool> ConsumeAsync(Guid id, string subject, string proposedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            update reg.pending_acts set consumed_at = @now
            where act_id = @id and kind = 'hand_delivery' and subject = @subject and proposed_by = @proposedBy
              and validated_at is not null and consumed_at is null and expires_at > @now
            """,
            connection, transaction());
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        command.Parameters.Add("subject", NpgsqlDbType.Text).Value = subject;
        command.Parameters.Add("proposedBy", NpgsqlDbType.Text).Value = proposedBy;
        command.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = Names.Utc(now);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static string KindOf(PendingActKind kind) => kind switch
    {
        PendingActKind.PlatformRole => "platform_role",
        PendingActKind.RoleDefinition => "role_definition",
        PendingActKind.OwnershipRescue => "ownership_rescue",
        PendingActKind.HandDelivery => "hand_delivery",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown act."),
    };

    private static PendingAct Read(NpgsqlDataReader row)
    {
        var name = row.GetString("kind");
        var kind = name switch
        {
            "platform_role" => PendingActKind.PlatformRole,
            "role_definition" => PendingActKind.RoleDefinition,
            "ownership_rescue" => PendingActKind.OwnershipRescue,
            "hand_delivery" => PendingActKind.HandDelivery,
            _ => throw new InvalidOperationException($"reg.pending_acts holds the kind '{name}', which this package does not know."),
        };
        var json = row.IsDBNull("definition") ? null : JsonSerializer.Deserialize<DefinitionJson>(row.GetString("definition"));
        return new PendingAct(row.GetGuid("act_id"), kind,
            row.IsDBNull("subject") ? null : row.GetString("subject"),
            row.IsDBNull("role") ? null : row.GetString("role"),
            row.IsDBNull("compartment") ? null : Compartment.Parse(row.GetString("compartment")),
            json is null ? null : Names.ScopeOf(json.Scope),
            json is null ? null : new RoleDefinition(Names.CategoryOf(json.Category), json.DisplayName, json.Operations,
                new AssignableRoles(Names.SelectorOf(json.AssignsCompartment), json.AssignsPlatform, json.AssignableRoles)),
            new Stamp(Utc(row.GetFieldValue<DateTimeOffset>("proposed_at")), Actor.Parse(row.GetString("proposed_by"))),
            Utc(row.GetFieldValue<DateTimeOffset>("expires_at")),
            row.IsDBNull("validated_at") ? null : new Stamp(Utc(row.GetFieldValue<DateTimeOffset>("validated_at")), Actor.Parse(row.GetString("validated_by"))),
            row.IsDBNull("rejected_at") ? null : new Stamp(Utc(row.GetFieldValue<DateTimeOffset>("rejected_at")), Actor.Parse(row.GetString("rejected_by"))),
            row.IsDBNull("consumed_at") ? null : Utc(row.GetFieldValue<DateTimeOffset>("consumed_at")));
    }

    private static DefinitionJson ToJson(RoleScope scope, RoleDefinition definition)
    {
        var assignable = definition.Assignable ?? Stellaegis.Registry.AssignableRoles.None;
        return new DefinitionJson(Names.Of(scope), Names.Of(definition.Category), definition.DisplayName, definition.Operations.ToArray(),
            Names.Of(assignable.Compartment), assignable.AllPlatform, assignable.Roles.ToArray());
    }

    private static DateTimeOffset Utc(DateTimeOffset value) => value;
}
