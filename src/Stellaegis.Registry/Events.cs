using Stellaegis.Primitives;

namespace Stellaegis.Registry;

/// <summary>Published when a compartment was created, renamed, suspended, resumed or terminated.</summary>
public sealed record CompartmentChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The compartment.</summary>
    public required Compartment Compartment { get; init; }

    /// <summary>Its state after the change.</summary>
    public required CompartmentStatus Status { get; init; }

    /// <summary>Who made the change.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when a compartment received its first owner, or its ownership was transferred.</summary>
public sealed record OwnershipChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The compartment.</summary>
    public required Compartment Compartment { get; init; }

    /// <summary>Its owner from now on.</summary>
    public required string Owner { get; init; }

    /// <summary>Its previous owner, or <see langword="null"/> for the first one.</summary>
    public string? Previous { get; init; }

    /// <summary>Who made the change: the installation or an operator for the first owner, the recipient for a transfer.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when an invitation was accepted: its roles took effect. It never carries the token.</summary>
public sealed record InvitationAcceptedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The invitation.</summary>
    public required Guid InvitationId { get; init; }

    /// <summary>The compartment it admitted to.</summary>
    public required Compartment Compartment { get; init; }

    /// <summary>The account that accepted it.</summary>
    public required string Subject { get; init; }

    /// <summary>Its author, who granted what it gave.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when a sensitive act was proposed for a validation.</summary>
public sealed record ActProposedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The act.</summary>
    public required Guid ActId { get; init; }

    /// <summary>What it does.</summary>
    public required PendingActKind Kind { get; init; }

    /// <summary>Its author.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when an act waiting was validated — and applied — or rejected.</summary>
public sealed record PendingActClosedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The act.</summary>
    public required Guid ActId { get; init; }

    /// <summary>What it does.</summary>
    public required PendingActKind Kind { get; init; }

    /// <summary>Whether it was validated; rejected otherwise.</summary>
    public required bool Validated { get; init; }

    /// <summary>Its author.</summary>
    public required Actor ProposedBy { get; init; }

    /// <summary>Who validated or rejected it.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when a user was recorded, deactivated or reactivated.</summary>
public sealed record UserChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The user, by their subject.</summary>
    public required string Subject { get; init; }

    /// <summary>Their state after the change.</summary>
    public required UserStatus Status { get; init; }

    /// <summary>Who made the change.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when a role was created, changed or deleted.</summary>
public sealed record RoleChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The role, by its name.</summary>
    public required string Role { get; init; }

    /// <summary>Whether the role still exists.</summary>
    public required bool Exists { get; init; }

    /// <summary>Who made the change.</summary>
    public required Actor By { get; init; }
}

/// <summary>Published when a user joined or left a compartment, or when their roles there changed.</summary>
public sealed record MembershipChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The user.</summary>
    public required string Subject { get; init; }

    /// <summary>The compartment.</summary>
    public required Compartment Compartment { get; init; }

    /// <summary>Whether the user is a member after the change.</summary>
    public required bool IsMember { get; init; }

    /// <summary>Who made the change.</summary>
    public required Actor By { get; init; }
}

/// <summary>
/// Published after the effective rights of a subject in a space were recomputed and handed to the sink. It carries
/// no snapshot: the rights went through the port, deterministically; the event only says that they changed.
/// </summary>
public sealed record RightsChangedEvent : IEvent
{
    /// <inheritdoc/>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The subject whose rights changed.</summary>
    public required string Subject { get; init; }

    /// <summary>The compartment the rights are in, or <see langword="null"/> for the shared space.</summary>
    public required Compartment? Compartment { get; init; }
}
