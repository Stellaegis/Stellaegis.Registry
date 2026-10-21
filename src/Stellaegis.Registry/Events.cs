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
