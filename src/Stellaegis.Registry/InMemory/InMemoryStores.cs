using System.Collections.Concurrent;
using Stellaegis.Primitives;

namespace Stellaegis.Registry.InMemory;

/// <summary>What the in-memory stores hold. One instance per host; the rights are lost with the process.</summary>
public sealed class InMemoryRegistryData
{
    internal ConcurrentDictionary<Compartment, CompartmentRecord> Compartments { get; } = new();

    internal ConcurrentDictionary<string, UserRecord> Users { get; } = new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, CatalogEntry> Catalog { get; } = new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, Role> Roles { get; } = new(StringComparer.Ordinal);

    internal ConcurrentDictionary<(string Subject, Compartment Compartment), MembershipEntry> Memberships { get; } = new();

    internal ConcurrentDictionary<string, Dictionary<string, Stamp>> Operators { get; } = new(StringComparer.Ordinal);

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        Compartments.Clear();
        Users.Clear();
        Catalog.Clear();
        Roles.Clear();
        Memberships.Clear();
        Operators.Clear();
    }

    internal static IReadOnlyCollection<string> Snapshot(Dictionary<string, Stamp> set)
    {
        lock (set)
            return set.Keys.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>A membership: who granted it and when, and the roles with who granted each and when.</summary>
    internal sealed class MembershipEntry(Stamp granted)
    {
        public Stamp Granted { get; } = granted;

        public Dictionary<string, Stamp> Roles { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed class InMemoryCompartmentStore(InMemoryRegistryData data) : ICompartmentStore
{
    public Task<CompartmentRecord?> GetAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => Task.FromResult(data.Compartments.GetValueOrDefault(compartment));

    public Task<IReadOnlyList<CompartmentRecord>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CompartmentRecord>>(data.Compartments.Values.OrderBy(c => c.Compartment.Value, StringComparer.Ordinal).ToList());

    public Task SaveAsync(CompartmentRecord compartment, CancellationToken cancellationToken = default)
    {
        data.Compartments[compartment.Compartment] = compartment;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUserStore(InMemoryRegistryData data) : IUserStore
{
    public Task<UserRecord?> GetAsync(string subject, CancellationToken cancellationToken = default)
        => Task.FromResult(data.Users.GetValueOrDefault(subject));

    public Task<IReadOnlyList<UserRecord>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserRecord>>(data.Users.Values.OrderBy(u => u.Subject, StringComparer.Ordinal).ToList());

    public Task SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        data.Users[user.Subject] = user;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCatalogStore(InMemoryRegistryData data) : ICatalogStore
{
    public Task<IReadOnlyList<CatalogEntry>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CatalogEntry>>(data.Catalog.Values.OrderBy(e => e.Module, StringComparer.Ordinal).ThenBy(e => e.Operation, StringComparer.Ordinal).ToList());

    public Task SaveAsync(IReadOnlyCollection<CatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
            data.Catalog[entry.Operation] = entry;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryRoleStore(InMemoryRegistryData data) : IRoleStore
{
    public Task<Role?> GetAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(data.Roles.GetValueOrDefault(name));

    public Task<IReadOnlyList<Role>> ListAsync(RoleScope? scope = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Role>>(data.Roles.Values.Where(r => scope is null || r.Scope == scope).OrderBy(r => r.Name, StringComparer.Ordinal).ToList());

    public Task SaveAsync(Role role, CancellationToken cancellationToken = default)
    {
        data.Roles[role.Name] = role with { Operations = role.Operations.Order(StringComparer.Ordinal).ToList() };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        data.Roles.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMembershipStore(InMemoryRegistryData data) : IMembershipStore
{
    public Task<bool> IsMemberAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default)
        => Task.FromResult(data.Memberships.ContainsKey((subject, compartment)));

    public Task<IReadOnlyList<Membership>> ListByUserAsync(string subject, CancellationToken cancellationToken = default)
        => Task.FromResult(List(pair => pair.Key.Subject == subject));

    public Task<IReadOnlyList<Membership>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => Task.FromResult(List(pair => pair.Key.Compartment == compartment));

    public Task<IReadOnlyList<Membership>> ListByRoleAsync(string role, CancellationToken cancellationToken = default)
        => Task.FromResult(List(pair => { lock (pair.Value.Roles) return pair.Value.Roles.ContainsKey(role); }));

    public Task AddAsync(string subject, Compartment compartment, Stamp granted, CancellationToken cancellationToken = default)
    {
        data.Memberships.TryAdd((subject, compartment), new InMemoryRegistryData.MembershipEntry(granted));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string subject, Compartment compartment, CancellationToken cancellationToken = default)
    {
        data.Memberships.TryRemove((subject, compartment), out _);
        return Task.CompletedTask;
    }

    public Task AssignRoleAsync(string subject, Compartment compartment, string role, Stamp granted, CancellationToken cancellationToken = default)
    {
        if (data.Memberships.TryGetValue((subject, compartment), out var entry))
            lock (entry.Roles)
                entry.Roles.TryAdd(role, granted);
        return Task.CompletedTask;
    }

    public Task UnassignRoleAsync(string subject, Compartment compartment, string role, CancellationToken cancellationToken = default)
    {
        if (data.Memberships.TryGetValue((subject, compartment), out var entry))
            lock (entry.Roles)
                entry.Roles.Remove(role);
        return Task.CompletedTask;
    }

    public Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
    {
        foreach (var key in data.Memberships.Keys.Where(k => k.Compartment == compartment).ToList())
            data.Memberships.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private IReadOnlyList<Membership> List(Func<KeyValuePair<(string Subject, Compartment Compartment), InMemoryRegistryData.MembershipEntry>, bool> where)
        => data.Memberships
            .Where(where)
            .OrderBy(pair => pair.Key.Subject, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Compartment.Value, StringComparer.Ordinal)
            .Select(pair => new Membership(pair.Key.Subject, pair.Key.Compartment, InMemoryRegistryData.Snapshot(pair.Value.Roles), pair.Value.Granted))
            .ToList();
}

internal sealed class InMemoryOperatorStore(InMemoryRegistryData data) : IOperatorStore
{
    public Task<IReadOnlyList<string>> ListRolesAsync(string subject, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(data.Operators.TryGetValue(subject, out var set) ? [.. InMemoryRegistryData.Snapshot(set)] : []);

    public Task<IReadOnlyList<(string Subject, IReadOnlyCollection<string> Roles)>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<(string, IReadOnlyCollection<string>)>>(data.Operators
            .Where(pair => { lock (pair.Value) return pair.Value.Count > 0; })
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, InMemoryRegistryData.Snapshot(pair.Value)))
            .ToList());

    public Task<IReadOnlyList<string>> ListByRoleAsync(string role, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(data.Operators
            .Where(pair => { lock (pair.Value) return pair.Value.ContainsKey(role); })
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToList());

    public Task AssignAsync(string subject, string role, Stamp granted, CancellationToken cancellationToken = default)
    {
        var set = data.Operators.GetOrAdd(subject, _ => new Dictionary<string, Stamp>(StringComparer.Ordinal));
        lock (set)
            set.TryAdd(role, granted);
        return Task.CompletedTask;
    }

    public Task UnassignAsync(string subject, string role, CancellationToken cancellationToken = default)
    {
        if (data.Operators.TryGetValue(subject, out var set))
            lock (set)
                set.Remove(role);
        return Task.CompletedTask;
    }
}
