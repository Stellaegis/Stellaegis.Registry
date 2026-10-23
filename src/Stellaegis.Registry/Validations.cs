using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>The acts that take effect only once a second channel validated them.</summary>
public enum PendingActKind
{
    /// <summary>The assignment of a platform role: whoever holds the platform holds every compartment.</summary>
    PlatformRole,

    /// <summary>The creation of a role, or a change of its operations, its assignable list or its category.</summary>
    RoleDefinition,

    /// <summary>The rescue of a compartment whose owner is unreachable, gone or dead: another owner seated by the platform.</summary>
    OwnershipRescue,
}

/// <summary>Where a validation comes from.</summary>
public enum ValidationChannel
{
    /// <summary>The administration routes: a person other than the author, holding <c>validations.write</c>.</summary>
    Screen,

    /// <summary>
    /// The <c>validate</c> verb of the host: the same executable, a process apart, under the administration connection of
    /// the database, the actor the system account that ran it. The frontier is the system access that guards the
    /// connection: whoever stole a web access asks, and the act waits. It may validate the act of the only operator.
    /// </summary>
    Console,
}

/// <summary>
/// An act waiting for its validation: until then it exists nowhere else — a platform role waiting is in no snapshot of
/// rights, a role definition waiting changes no role. The row stays once closed, the trace of who proposed and who
/// validated or rejected.
/// </summary>
/// <param name="ActId">Its identity.</param>
/// <param name="Kind">What it does.</param>
/// <param name="Subject">The user it concerns: given the platform role, or seated as owner; <see langword="null"/> for a role definition.</param>
/// <param name="Role">The platform role assigned, or the role defined; <see langword="null"/> for a rescue.</param>
/// <param name="Compartment">The compartment rescued; <see langword="null"/> otherwise.</param>
/// <param name="Scope">The scope of the role defined, for a creation or a change; <see langword="null"/> otherwise.</param>
/// <param name="Definition">The definition proposed; <see langword="null"/> but for a role definition.</param>
/// <param name="Proposed">When and by whom it was proposed: its author, who answers for it.</param>
/// <param name="ExpiresAt">When it lapses, unvalidated.</param>
/// <param name="Validated">When and by whom it was validated, or <see langword="null"/>.</param>
/// <param name="Rejected">When and by whom it was rejected or withdrawn, or <see langword="null"/>.</param>
public sealed record PendingAct(
    Guid ActId, PendingActKind Kind, string? Subject, string? Role, Compartment? Compartment, RoleScope? Scope, RoleDefinition? Definition,
    Stamp Proposed, DateTimeOffset ExpiresAt, Stamp? Validated = null, Stamp? Rejected = null)
{
    /// <summary>How long an act waits for its validation: seven days.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Whether it can still be validated at <paramref name="now"/>: neither closed nor lapsed.</summary>
    public bool IsPendingAt(DateTimeOffset now) => Validated is null && Rejected is null && now < ExpiresAt;
}

/// <summary>What a sensitive act became: applied at once — by the installation —, or waiting for its validation.</summary>
/// <typeparam name="T">What it produces once applied.</typeparam>
/// <param name="Value">What it produced, when applied; <see langword="null"/> while it waits.</param>
/// <param name="Pending">The act waiting, or <see langword="null"/> when it was applied.</param>
public sealed record ActOutcome<T>(T? Value, PendingAct? Pending) where T : class
{
    /// <summary>Whether it waits for its validation.</summary>
    public bool IsPending => Pending is not null;
}

/// <summary>The acts waiting for a validation. An adapter implements it; <c>Stellaegis.Registry.PostgreSQL</c> on <c>reg.pending_acts</c>.</summary>
public interface IPendingActStore
{
    /// <summary>Inserts the act.</summary>
    Task InsertAsync(PendingAct act, CancellationToken cancellationToken = default);

    /// <summary>The act, or <see langword="null"/>.</summary>
    Task<PendingAct?> GetAsync(Guid actId, CancellationToken cancellationToken = default);

    /// <summary>Every act, closed ones included, the newest first.</summary>
    Task<IReadOnlyList<PendingAct>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the act as validated when it is still open; whether it was — of two validations at once, one passes.</summary>
    Task<bool> MarkValidatedAsync(Guid actId, Stamp validated, CancellationToken cancellationToken = default);

    /// <summary>Closes the act as rejected when it is still open; whether it was.</summary>
    Task<bool> MarkRejectedAsync(Guid actId, Stamp rejected, CancellationToken cancellationToken = default);
}

/// <summary>
/// The validation of the three sensitive acts — the assignment of a platform role, the definition of a role, the rescue
/// of an ownership —, by a second channel. On screen, a person other than the author, holding <c>validations.write</c>,
/// whose lists cover the platform role validated, who does not hold the role redefined; never one's own act. At the
/// console, the <c>validate</c> verb of the host, which may validate any act, the only operator's included. Validating
/// applies the act, checked again; the act keeps its author and its validator.
/// </summary>
public interface IValidations
{
    /// <summary>The acts, closed ones included, the newest first.</summary>
    Task<IReadOnlyList<PendingAct>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the act and applies it.</summary>
    Task<Result> ValidateAsync(Guid actId, Actor actor, ValidationChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Rejects the act: its author withdraws it, or whoever could validate it refuses it.</summary>
    Task<Result> RejectAsync(Guid actId, Actor actor, ValidationChannel channel, CancellationToken cancellationToken = default);
}

internal sealed class Validations(
    IPendingActStore store,
    IUserStore users,
    IRoleStore roles,
    IOperatorStore operators,
    AssignmentRules rules,
    Roles roleService,
    Operators operatorService,
    Ownership ownership,
    IEventPublisher publisher,
    TimeProvider time) : IValidations
{
    public Task<IReadOnlyList<PendingAct>> ListAsync(CancellationToken cancellationToken = default) => store.ListAsync(cancellationToken);

    public async Task<Result> ValidateAsync(Guid actId, Actor actor, ValidationChannel channel, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var act = await store.GetAsync(actId, cancellationToken).ConfigureAwait(false);
        var open = Open(act);
        if (open.IsFailure)
            return open;
        if (channel == ValidationChannel.Screen && await WhyNotValidatorAsync(act!, actor, cancellationToken).ConfigureAwait(false) is { } refused)
            return Result.Failure(refused);

        // Checked again, as an acceptance is: what the act assumed may have changed since it was proposed.
        var applied = act!.Kind switch
        {
            PendingActKind.PlatformRole => await operatorService.ApplyAsync(act.Subject!, act.Role!, act.Proposed.By, cancellationToken).ConfigureAwait(false),
            PendingActKind.RoleDefinition => await roleService.ApplyAsync(act.Role!, act.Scope!.Value, act.Definition!, act.Proposed.By, cancellationToken).ConfigureAwait(false),
            _ => await ownership.ApplyRescueAsync(act.Compartment!, act.Subject!, act.Proposed.By, cancellationToken).ConfigureAwait(false),
        };
        if (applied.IsFailure)
            return applied;
        var now = time.GetUtcNow();
        if (!await store.MarkValidatedAsync(actId, new Stamp(now, actor), cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.ActClosed());
        await publisher.PublishAsync(new PendingActClosedEvent { OccurredAt = now, ActId = actId, Kind = act.Kind, Validated = true, ProposedBy = act.Proposed.By, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> RejectAsync(Guid actId, Actor actor, ValidationChannel channel, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var act = await store.GetAsync(actId, cancellationToken).ConfigureAwait(false);
        var open = Open(act);
        if (open.IsFailure)
            return open;
        // Its author withdraws it; anyone who could validate it refuses it.
        if (channel == ValidationChannel.Screen && actor != act!.Proposed.By
            && await WhyNotValidatorAsync(act, actor, cancellationToken).ConfigureAwait(false) is { } refused)
            return Result.Failure(refused);
        var now = time.GetUtcNow();
        if (!await store.MarkRejectedAsync(actId, new Stamp(now, actor), cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.ActClosed());
        await publisher.PublishAsync(new PendingActClosedEvent { OccurredAt = now, ActId = actId, Kind = act!.Kind, Validated = false, ProposedBy = act.Proposed.By, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Result Open(PendingAct? act)
    {
        if (act is null)
            return Result.Failure(RegistryErrors.ActNotFound());
        if (act.Validated is not null || act.Rejected is not null)
            return Result.Failure(RegistryErrors.ActClosed());
        return time.GetUtcNow() >= act.ExpiresAt ? Result.Failure(RegistryErrors.ActExpired()) : Result.Success();
    }

    // On screen: a person other than the author, holding validations.write by a platform role, and, for a platform role,
    // whose lists cover it; for a role redefined, who does not hold it.
    private async Task<Error?> WhyNotValidatorAsync(PendingAct act, Actor actor, CancellationToken cancellationToken)
    {
        if (!RegistryRules.IsSubject(actor.Value))
            return RegistryErrors.NotValidator();
        if (actor == act.Proposed.By)
            return RegistryErrors.OwnAct();
        var validator = RegistryRules.NormalizeSubject(actor.Value);
        if ((await users.GetAsync(validator, cancellationToken).ConfigureAwait(false))?.Status != UserStatus.Active)
            return RegistryErrors.NotValidator();
        var all = (await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToDictionary(role => role.Name, StringComparer.Ordinal);
        var held = await operators.ListRolesAsync(validator, cancellationToken).ConfigureAwait(false);
        if (!held.Any(name => all.TryGetValue(name, out var role) && role.Operations.Contains(RegistryOperations.ValidationsWrite.Identity, StringComparer.Ordinal)))
            return RegistryErrors.NotValidator();
        switch (act.Kind)
        {
            case PendingActKind.PlatformRole:
                return await rules.CheckAsync(actor, act.Subject!, compartment: null, [act.Role!], cancellationToken).ConfigureAwait(false);
            case PendingActKind.RoleDefinition when all.TryGetValue(act.Role!, out var defined):
                return await roleService.HeldByAsync(defined, actor, cancellationToken).ConfigureAwait(false) ? RegistryErrors.RoleHeldByAuthor(defined.Name) : null;
            default:
                return null;
        }
    }
}

/// <summary>Proposes the sensitive acts: one open act per target, waiting <see cref="PendingAct.Lifetime"/>.</summary>
internal sealed class ActProposals(IPendingActStore store, IEventPublisher publisher, TimeProvider time)
{
    public async Task<Result<PendingAct>> ProposeAsync(
        PendingActKind kind, string? subject, string? role, Compartment? compartment, RoleScope? scope, RoleDefinition? definition,
        Actor actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // One open act per target: a second proposal waits for the first to be validated, rejected or lapsed.
        var open = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(act =>
            act.IsPendingAt(now) && act.Kind == kind && act.Subject == subject && act.Role == role && act.Compartment == compartment);
        if (open is not null)
            return Result.Failure<PendingAct>(RegistryErrors.ActPending(open.ActId));
        var act = new PendingAct(Guid.NewGuid(), kind, subject, role, compartment, scope, definition, new Stamp(now, actor), now + PendingAct.Lifetime);
        await store.InsertAsync(act, cancellationToken).ConfigureAwait(false);
        await publisher.PublishAsync(new ActProposedEvent { OccurredAt = now, ActId = act.ActId, Kind = kind, By = actor }, cancellationToken).ConfigureAwait(false);
        return Result.Success(act);
    }
}
