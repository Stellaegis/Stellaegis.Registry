using Microsoft.Extensions.Logging;
using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>
/// Where the effective rights go: the decision point. The registry computes the rights of a subject in a space
/// after every change that touches them, and hands the snapshot to this port, synchronously, in the same operation —
/// deterministically, rather than through an event whose delivery would have to be made reliable. The host
/// implements it with the projection of <c>Stellaegis.Access</c>, whose methods have the same shape, or with an
/// HTTP client of a decision point apart. Without an implementation, <see cref="NullRightsSink"/> drops the
/// snapshots and warns once.
/// </summary>
public interface IRightsSink
{
    /// <summary>Replaces the compartments the person is admitted in; empty removes them all.</summary>
    Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken cancellationToken = default);

    /// <summary>Replaces the rights of a person in a compartment, by operation identity; empty removes them all.</summary>
    Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default);

    /// <summary>Replaces the rights of a person in the shared space — what makes them an operator of the platform; empty removes them all.</summary>
    Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default);

    /// <summary>Removes every membership and every right granted in the compartment, once it is terminated or suspended.</summary>
    Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Removes every membership and every right of the person, in every space, once they are deactivated.</summary>
    Task RemoveSubjectAsync(string subject, CancellationToken cancellationToken = default);
}

/// <summary>The sink of a host without a decision point: drops every snapshot, and says so once.</summary>
public sealed class NullRightsSink(ILogger<NullRightsSink> logger) : IRightsSink
{
    private int _warned;

    /// <inheritdoc/>
    public Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken cancellationToken = default) => Drop();

    /// <inheritdoc/>
    public Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default) => Drop();

    /// <inheritdoc/>
    public Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default) => Drop();

    /// <inheritdoc/>
    public Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default) => Drop();

    /// <inheritdoc/>
    public Task RemoveSubjectAsync(string subject, CancellationToken cancellationToken = default) => Drop();

    private Task Drop()
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            logger.LogWarning("No rights sink is registered: the effective rights the registry computes go nowhere. Register one with AddStellaegisRightsSink<T>(), on the projection of Stellaegis.Access.");
        return Task.CompletedTask;
    }
}

/// <summary>A sink that keeps every call, for the tests of a host to read what the registry projected.</summary>
public sealed class RecordingRightsSink : IRightsSink
{
    private readonly List<RightsSinkCall> _calls = [];

    /// <summary>Every call, in order.</summary>
    public IReadOnlyList<RightsSinkCall> Calls
    {
        get
        {
            lock (_calls)
                return [.. _calls];
        }
    }

    /// <summary>The last call of a kind, or <see langword="null"/>.</summary>
    public RightsSinkCall? Last(RightsSinkCallKind kind) => Calls.LastOrDefault(call => call.Kind == kind);

    /// <summary>Forgets every call.</summary>
    public void Clear()
    {
        lock (_calls)
            _calls.Clear();
    }

    /// <inheritdoc/>
    public Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken cancellationToken = default)
        => Record(new RightsSinkCall(RightsSinkCallKind.ReplaceMemberships, subject, null, [.. compartments.Select(c => c.Value)]));

    /// <inheritdoc/>
    public Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
        => Record(new RightsSinkCall(RightsSinkCallKind.ReplaceUserGrants, subject, compartment, [.. operations]));

    /// <inheritdoc/>
    public Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<string> operations, CancellationToken cancellationToken = default)
        => Record(new RightsSinkCall(RightsSinkCallKind.ReplaceAdminGrants, subject, null, [.. operations]));

    /// <inheritdoc/>
    public Task RemoveCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => Record(new RightsSinkCall(RightsSinkCallKind.RemoveCompartment, null, compartment, []));

    /// <inheritdoc/>
    public Task RemoveSubjectAsync(string subject, CancellationToken cancellationToken = default)
        => Record(new RightsSinkCall(RightsSinkCallKind.RemoveSubject, subject, null, []));

    private Task Record(RightsSinkCall call)
    {
        lock (_calls)
            _calls.Add(call);
        return Task.CompletedTask;
    }
}

/// <summary>What a <see cref="RecordingRightsSink"/> received.</summary>
/// <param name="Kind">Which method.</param>
/// <param name="Subject">The subject, when the method names one.</param>
/// <param name="Compartment">The compartment, when the method names one.</param>
/// <param name="Values">The compartments or the operations handed, as strings, in the order given.</param>
public sealed record RightsSinkCall(RightsSinkCallKind Kind, string? Subject, Compartment? Compartment, IReadOnlyList<string> Values)
{
    /// <summary>Two calls are equal when their kind, subject, compartment and values, in order, are.</summary>
    public bool Equals(RightsSinkCall? other)
        => other is not null && Kind == other.Kind && Subject == other.Subject && Compartment == other.Compartment && Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Subject, Compartment, Values.Count);

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}({Subject}, {Compartment}, [{string.Join(", ", Values)}])";
}

/// <summary>The five methods of <see cref="IRightsSink"/>.</summary>
public enum RightsSinkCallKind
{
    /// <summary><see cref="IRightsSink.ReplaceMembershipsAsync"/>.</summary>
    ReplaceMemberships,

    /// <summary><see cref="IRightsSink.ReplaceUserGrantsAsync"/>.</summary>
    ReplaceUserGrants,

    /// <summary><see cref="IRightsSink.ReplaceAdminGrantsAsync"/>.</summary>
    ReplaceAdminGrants,

    /// <summary><see cref="IRightsSink.RemoveCompartmentAsync"/>.</summary>
    RemoveCompartment,

    /// <summary><see cref="IRightsSink.RemoveSubjectAsync"/>.</summary>
    RemoveSubject,
}

/// <summary>
/// Computes the effective rights and hands them to the sink. A person holds, in a compartment, the union of the
/// operations of their compartment roles there — if they are a member, the compartment is active and they are; on
/// the platform, the union of the operations of their platform roles — if they are active. Every change calls the
/// one of these that it touches; a role changed recomputes every holder.
/// </summary>
internal sealed class RightsProjector(
    IUserStore users,
    ICompartmentStore compartments,
    IRoleStore roles,
    IMembershipStore memberships,
    IOperatorStore operators,
    IRightsSink sink,
    IEventPublisher publisher,
    TimeProvider time)
{
    /// <summary>The compartments the person is admitted in: those they are a member of and that are active, none when deactivated.</summary>
    public async Task ProjectMembershipsAsync(string subject, CancellationToken ct)
    {
        var admitted = new List<Compartment>();
        if (await IsActiveAsync(subject, ct).ConfigureAwait(false))
        {
            foreach (var membership in await memberships.ListByUserAsync(subject, ct).ConfigureAwait(false))
            {
                var compartment = await compartments.GetAsync(membership.Compartment, ct).ConfigureAwait(false);
                if (compartment is { Status: CompartmentStatus.Active })
                    admitted.Add(membership.Compartment);
            }
        }
        await sink.ReplaceMembershipsAsync(subject, admitted, ct).ConfigureAwait(false);
    }

    /// <summary>The rights of the person in the compartment, and the event that says they changed.</summary>
    public async Task ProjectUserGrantsAsync(string subject, Compartment compartment, CancellationToken ct)
    {
        var operations = new SortedSet<string>(StringComparer.Ordinal);
        var record = await compartments.GetAsync(compartment, ct).ConfigureAwait(false);
        if (record is { Status: CompartmentStatus.Active } && await IsActiveAsync(subject, ct).ConfigureAwait(false))
        {
            var membership = (await memberships.ListByUserAsync(subject, ct).ConfigureAwait(false)).FirstOrDefault(m => m.Compartment == compartment);
            if (membership is not null)
                await UnionAsync(operations, membership.Roles, ct).ConfigureAwait(false);
        }
        await sink.ReplaceUserGrantsAsync(subject, compartment, operations, ct).ConfigureAwait(false);
        await publisher.PublishAsync(new RightsChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = compartment }, ct).ConfigureAwait(false);
    }

    /// <summary>The rights of the person on the platform, and the event that says they changed.</summary>
    public async Task ProjectAdminGrantsAsync(string subject, CancellationToken ct)
    {
        var operations = new SortedSet<string>(StringComparer.Ordinal);
        if (await IsActiveAsync(subject, ct).ConfigureAwait(false))
            await UnionAsync(operations, await operators.ListRolesAsync(subject, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        await sink.ReplaceAdminGrantsAsync(subject, operations, ct).ConfigureAwait(false);
        await publisher.PublishAsync(new RightsChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = null }, ct).ConfigureAwait(false);
    }

    /// <summary>Everything of the person: their memberships, their rights in each compartment, on the platform.</summary>
    public async Task ProjectSubjectAsync(string subject, CancellationToken ct)
    {
        await ProjectMembershipsAsync(subject, ct).ConfigureAwait(false);
        foreach (var membership in await memberships.ListByUserAsync(subject, ct).ConfigureAwait(false))
            await ProjectUserGrantsAsync(subject, membership.Compartment, ct).ConfigureAwait(false);
        await ProjectAdminGrantsAsync(subject, ct).ConfigureAwait(false);
    }

    /// <summary>Every holder of the role, in its scope.</summary>
    public async Task ProjectRoleHoldersAsync(Role role, CancellationToken ct)
    {
        if (role.Scope == RoleScope.Compartment)
        {
            foreach (var membership in await memberships.ListByRoleAsync(role.Name, ct).ConfigureAwait(false))
                await ProjectUserGrantsAsync(membership.Subject, membership.Compartment, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var subject in await operators.ListByRoleAsync(role.Name, ct).ConfigureAwait(false))
                await ProjectAdminGrantsAsync(subject, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Every member of the compartment, once it resumes.</summary>
    public async Task ProjectCompartmentAsync(Compartment compartment, CancellationToken ct)
    {
        foreach (var membership in await memberships.ListByCompartmentAsync(compartment, ct).ConfigureAwait(false))
        {
            await ProjectMembershipsAsync(membership.Subject, ct).ConfigureAwait(false);
            await ProjectUserGrantsAsync(membership.Subject, compartment, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The compartment is suspended or terminated: nothing granted in it any more.</summary>
    public async Task RemoveCompartmentAsync(Compartment compartment, CancellationToken ct)
    {
        await sink.RemoveCompartmentAsync(compartment, ct).ConfigureAwait(false);
        foreach (var membership in await memberships.ListByCompartmentAsync(compartment, ct).ConfigureAwait(false))
            await publisher.PublishAsync(new RightsChangedEvent { OccurredAt = time.GetUtcNow(), Subject = membership.Subject, Compartment = compartment }, ct).ConfigureAwait(false);
    }

    /// <summary>The person is deactivated: nothing granted to them any more, anywhere.</summary>
    public async Task RemoveSubjectAsync(string subject, CancellationToken ct)
    {
        await sink.RemoveSubjectAsync(subject, ct).ConfigureAwait(false);
        await publisher.PublishAsync(new RightsChangedEvent { OccurredAt = time.GetUtcNow(), Subject = subject, Compartment = null }, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsActiveAsync(string subject, CancellationToken ct)
        => await users.GetAsync(subject, ct).ConfigureAwait(false) is { Status: UserStatus.Active };

    private async Task UnionAsync(SortedSet<string> operations, IEnumerable<string> roleNames, CancellationToken ct)
    {
        foreach (var name in roleNames)
        {
            var role = await roles.GetAsync(name, ct).ConfigureAwait(false);
            if (role is not null)
                operations.UnionWith(role.Operations);
        }
    }
}
