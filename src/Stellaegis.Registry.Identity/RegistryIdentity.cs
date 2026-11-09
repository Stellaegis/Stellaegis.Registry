using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stellaegis.Identity;
using Stellaegis.Primitives;

namespace Stellaegis.Registry.Identity;

/// <summary>
/// Composes <c>Stellaegis.Identity</c> with the registry: the ports the accounts declare, answered by the registry.
/// Neither package depends on the other; this one, a package of composition, is where they meet, so that no host
/// copies the bridge — a few lines that decide who takes an account.
/// </summary>
public static class RegistryIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Answers the ports of the accounts by the registry: <see cref="IHandDeliveryApprovals"/> by the acts that wait for
    /// a validation — a reset link handed over for an active account waits for a second person who reaches the person
    /// too —, and <see cref="IInvitationAcceptance"/> by the invitations — the account accepting is recorded in the
    /// directory first, by its person. Registered after the accounts and the registry; a port the host answers
    /// otherwise is kept.
    /// </summary>
    public static IServiceCollection AddStellaegisRegistryIdentity(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.TryAddScoped<IHandDeliveryApprovals, RegistryHandDeliveryApprovals>();
        services.TryAddScoped<IInvitationAcceptance, RegistryInvitationAcceptance>();
        return services;
    }
}

/// <summary>A reset link handed over, approved by the acts of the registry that wait for a validation.</summary>
internal sealed class RegistryHandDeliveryApprovals(IValidations validations) : IHandDeliveryApprovals
{
    public async Task<Guid> RequestAsync(Subject subject, Actor requestedBy, CancellationToken cancellationToken = default)
    {
        var proposed = await validations.ProposeHandDeliveryAsync(subject.Value, requestedBy, cancellationToken).ConfigureAwait(false);
        if (proposed.IsSuccess)
            return proposed.Value.ActId;
        // Asked again while the first waits: the act waiting is the approval to have granted.
        if (proposed.Error!.Code == RegistryErrors.ActPendingCode
            && (await validations.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(act =>
                act.Kind == PendingActKind.HandDelivery && act.Subject == subject.Value && act.Validated is null && act.Rejected is null) is { } waiting)
            return waiting.ActId;
        // The accounts asked the rights system first, on the account (Subject): a refusal here is a host that opened the
        // administration to every account while asking the registry for the approval — a fault of its composition.
        throw new InvalidOperationException($"The registry refuses to hand a reset link of {subject} over to {requestedBy}: {proposed.Error}.");
    }

    public Task<bool> TryConsumeAsync(Guid approval, Subject subject, Actor requestedBy, CancellationToken cancellationToken = default)
        => validations.TryConsumeHandDeliveryAsync(approval, subject.Value, requestedBy, cancellationToken);
}

/// <summary>An invitation of the registry, opened and accepted by the accounts.</summary>
internal sealed class RegistryInvitationAcceptance(IInvitations invitations, IUserDirectory directory, IAuthUserStore accounts) : IInvitationAcceptance
{
    public async Task<InvitationOffer?> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        var preview = await invitations.PreviewAsync(token, cancellationToken).ConfigureAwait(false);
        return preview.IsSuccess ? new InvitationOffer(preview.Value.Email, preview.Value.Delivery == InvitationDelivery.Email) : null;
    }

    public async Task<Result> AcceptAsync(string token, Subject subject, bool emailVerified, CancellationToken cancellationToken = default)
    {
        // The registry grants to a person of its directory: one who accepts their first invitation is recorded first, by
        // themselves, as a registration is.
        if ((await directory.GetAsync(subject.Value, cancellationToken).ConfigureAwait(false)).IsFailure)
        {
            if (!Guid.TryParse(subject.Value, out var id) || await accounts.GetByUniqueIdAsync(id, cancellationToken).ConfigureAwait(false) is not { } account)
                return Result.Failure(IdentityErrors.AccountNotFound);
            var recorded = await directory.RecordAsync(subject.Value, account.Email, account.DisplayName, account.AsActor(), cancellationToken).ConfigureAwait(false);
            if (recorded.IsFailure)
                return Result.Failure(recorded.Error!);
        }
        return await invitations.AcceptAsync(token, subject.Value, emailVerified, cancellationToken).ConfigureAwait(false);
    }
}
