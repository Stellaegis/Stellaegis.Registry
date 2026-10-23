using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Registry;

/// <summary>How the link of an invitation reaches the person invited.</summary>
public enum InvitationDelivery
{
    /// <summary>Sent by the system to the address invited, through <see cref="IInvitationSender"/>: accepting it proves the address.</summary>
    Email,

    /// <summary>Handed over by its author, by the means of their choice, when the host declares it: accepting it proves nothing about the address.</summary>
    Hand,
}

/// <summary>
/// An invitation: an assignment deferred. Its author gives roles of their assignable list, in a compartment, to an
/// address, which take effect when the person accepts — everything checked again then. Only the hash of its token is
/// kept. Accepted, it stays as long as the account, part of its life; lapsed or revoked, it is purged.
/// </summary>
/// <param name="InvitationId">Its public identity, to list and revoke it by.</param>
/// <param name="TokenHash">The SHA-256 of its token, in lowercase hexadecimal; the token is in the link only.</param>
/// <param name="Compartment">The compartment it admits to.</param>
/// <param name="Email">The address invited, lowercase: the account that accepts it carries it, verified.</param>
/// <param name="Roles">The compartment roles it gives.</param>
/// <param name="DesignatesOwner">Whether it seats the owner of a compartment that has none.</param>
/// <param name="Delivery">How its link reached the person.</param>
/// <param name="Issued">When and by whom it was issued: its author, who answers for the roles it gives.</param>
/// <param name="ExpiresAt">When it lapses, unanswered.</param>
/// <param name="AcceptedAt">When it was accepted, or <see langword="null"/>.</param>
/// <param name="AcceptedBy">The subject of the account that accepted it, or <see langword="null"/>.</param>
/// <param name="Revoked">When and by whom it was revoked, or <see langword="null"/>.</param>
public sealed record Invitation(
    Guid InvitationId, string TokenHash, Compartment Compartment, string Email, IReadOnlyCollection<string> Roles,
    bool DesignatesOwner, InvitationDelivery Delivery, Stamp Issued, DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt = null, string? AcceptedBy = null, Stamp? Revoked = null)
{
    /// <summary>Whether it can still be accepted at <paramref name="now"/>: neither accepted, nor revoked, nor lapsed.</summary>
    public bool IsPendingAt(DateTimeOffset now) => AcceptedAt is null && Revoked is null && now < ExpiresAt;
}

/// <summary>An invitation just issued, with its token — shown this once, to hand over, when its delivery is by hand.</summary>
/// <param name="Invitation">The invitation.</param>
/// <param name="Token">The token, when the author hands the link over; <see langword="null"/> when it was sent by e-mail.</param>
public sealed record IssuedInvitation(Invitation Invitation, string? Token);

/// <summary>What a token opens, read without spending it: enough for a page to ask for a password, nothing it would name.</summary>
/// <param name="InvitationId">The invitation.</param>
/// <param name="Email">The address invited, which the account that accepts it carries.</param>
/// <param name="Delivery">How its link reached the person: by e-mail from the system, which proves the address, or by hand.</param>
/// <param name="ExpiresAt">When it lapses.</param>
public sealed record InvitationPreview(Guid InvitationId, string Email, InvitationDelivery Delivery, DateTimeOffset ExpiresAt);

/// <summary>What the sender of an invitation receives: the address, the token its link carries, and what the message may name.</summary>
/// <param name="Email">The address invited.</param>
/// <param name="Token">The token of the link; the host writes the link to its acceptance page.</param>
/// <param name="ExpiresAt">When the invitation lapses.</param>
/// <param name="Compartment">The compartment it admits to.</param>
/// <param name="CompartmentName">What people call the compartment.</param>
public sealed record InvitationMessage(string Email, string Token, DateTimeOffset ExpiresAt, Compartment Compartment, string CompartmentName);

/// <summary>
/// Sends the link of an invitation, the host's way — <c>IIdentityMailer</c>, a messaging module, or otherwise —, in
/// process, when it is issued. The registry declares it and never sends anything itself; no event carries the token.
/// </summary>
public interface IInvitationSender
{
    /// <summary>Sends the invitation to its address.</summary>
    Task SendAsync(InvitationMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The options of the registry, bound from <see cref="SectionName"/> or configured in code, and validated when the
/// host starts.
/// <code>
/// Stellaegis:Registry:Invitations:Lifetime     = 7.00:00:00
/// Stellaegis:Registry:Invitations:HandDelivery = false
/// Stellaegis:Registry:Invitations:Retention    = 30.00:00:00
/// </code>
/// </summary>
public sealed class RegistryOptions : IValidatableOptions
{
    /// <summary>The configuration section of the registry, <c>Stellaegis:Registry</c>.</summary>
    public const string SectionName = "Stellaegis:Registry";

    /// <summary>The invitations.</summary>
    public InvitationOptions Invitations { get; set; } = new();

    /// <inheritdoc/>
    public string? FindError() => Invitations.FindError() is { } error ? $"{nameof(Invitations)}: {error}" : null;
}

/// <summary>How long an invitation waits, how its link may reach the person, and how long a dead one is kept.</summary>
public sealed class InvitationOptions : IValidatableOptions
{
    /// <summary>The least an invitation lapsed or revoked is kept before its purge: 30 days.</summary>
    public static readonly TimeSpan MinimumRetention = TimeSpan.FromDays(30);

    /// <summary>How long an invitation waits for its acceptance. Seven days unless set.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether its author may receive the token of an invitation to hand the link over by the means of their choice,
    /// rather than have it sent by e-mail. No unless set: handing links over is an opening the host declares.
    /// </summary>
    public bool HandDelivery { get; set; }

    /// <summary>How long an invitation lapsed or revoked is kept, for the trace, before its purge. <see cref="MinimumRetention"/> unless set, and never less.</summary>
    public TimeSpan Retention { get; set; } = MinimumRetention;

    /// <inheritdoc/>
    public string? FindError()
    {
        if (Lifetime <= TimeSpan.Zero)
            return $"{nameof(Lifetime)} must be positive.";
        if (Retention < MinimumRetention)
            return $"{nameof(Retention)} must be at least {MinimumRetention.TotalDays} days.";
        return null;
    }
}

/// <summary>The tokens of the invitations: 32 random bytes, written base64url in the link; only their SHA-256 is kept.</summary>
internal static class InvitationTokens
{
    public static (string Token, string Hash) New()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guard.NotWhiteSpace(token))));
}

/// <summary>The invitations. An adapter implements it; <c>Stellaegis.Registry.PostgreSQL</c> on <c>reg.invitations</c>.</summary>
public interface IInvitationStore
{
    /// <summary>Inserts the invitation.</summary>
    Task InsertAsync(Invitation invitation, CancellationToken cancellationToken = default);

    /// <summary>The invitation, or <see langword="null"/>.</summary>
    Task<Invitation?> GetAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>The invitation whose token has this hash, or <see langword="null"/>.</summary>
    Task<Invitation?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The invitations to the compartment, the newest first.</summary>
    Task<IReadOnlyList<Invitation>> ListByCompartmentAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Revokes the invitation when it is neither accepted nor revoked; whether it was.</summary>
    Task<bool> RevokeAsync(Guid invitationId, Stamp revoked, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends the token, atomically: marks the invitation accepted by <paramref name="subject"/> when it is neither
    /// accepted nor revoked nor lapsed at <paramref name="at"/>; whether it was — of two acceptances at once, one passes.
    /// </summary>
    Task<bool> TryAcceptAsync(string tokenHash, string subject, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Deletes the invitations never accepted that lapsed or were revoked before <paramref name="before"/>; how many.</summary>
    Task<int> DeleteDeadBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}

/// <summary>
/// The invitations: an assignment deferred (I3). Issuing is controlled as an assignment is — every role in the
/// assignable lists of the author's roles, a member of the compartment never invited, nobody inviting their own
/// address —; accepting checks everything again, all or nothing (Q3): the address of the account is the address
/// invited, verified (Q2); the author is still active, still holds <c>invitations.write</c>, and may still assign
/// every role; the compartment is still active. The membership and the roles then carry the author as their grantor.
/// </summary>
public interface IInvitations
{
    /// <summary>
    /// Issues an invitation to <paramref name="email"/>, in the compartment, with <paramref name="roles"/>; a newer one
    /// to the same address and compartment revokes those waiting. Sent by e-mail through <see cref="IInvitationSender"/>,
    /// or handed over — the token is then in the answer, this once —, when the host declares it.
    /// <paramref name="designatesOwner"/> makes it seat the owner of a compartment that has none: an act of an operator
    /// whose platform roles cover every compartment role, or of the installation.
    /// </summary>
    Task<Result<IssuedInvitation>> IssueAsync(
        Compartment compartment, string email, IReadOnlyCollection<string> roles, InvitationDelivery delivery, Actor actor,
        bool designatesOwner = false, CancellationToken cancellationToken = default);

    /// <summary>Revokes an invitation waiting: its author, the owner of its compartment, or anyone who could issue it.</summary>
    Task<Result> RevokeAsync(Guid invitationId, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>What the token opens, when it can still be accepted; the token is not spent. A page shows nothing of it before the person submits.</summary>
    Task<Result<InvitationPreview>> PreviewAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the invitation for the account <paramref name="subject"/>, recorded in the directory, whose address is
    /// the one invited and verified — which the host tells, from the accounts. Everything is checked again; the token is
    /// spent once, atomically; then the membership, the roles and the ownership designated are applied, granted by the
    /// author of the invitation.
    /// </summary>
    Task<Result> AcceptAsync(string token, string subject, bool emailVerified, CancellationToken cancellationToken = default);

    /// <summary>The invitations to the compartment, the newest first.</summary>
    Task<IReadOnlyList<Invitation>> ListAsync(Compartment compartment, CancellationToken cancellationToken = default);

    /// <summary>Deletes the invitations never accepted that lapsed or were revoked longer than the retention ago; how many. The host runs it by its scheduler under <see cref="RegistryJobs.Purge"/>.</summary>
    Task<int> PurgeAsync(Actor actor, CancellationToken cancellationToken = default);
}

/// <summary>The jobs of the registry, the actors of what it does without a person.</summary>
public static class RegistryJobs
{
    /// <summary>The purge of the invitations past their retention, <c>job:registry_purge</c>.</summary>
    public static Actor Purge { get; } = Actor.Job("registry_purge");
}

internal sealed class Invitations(
    IInvitationStore store,
    ICompartmentStore compartments,
    IUserStore users,
    IMembershipStore memberships,
    IRoleStore roles,
    AssignmentRules rules,
    IMemberships membershipService,
    IOwnership ownership,
    IOptions<RegistryOptions> options,
    IEventPublisher publisher,
    TimeProvider time,
    IInvitationSender? sender = null) : IInvitations
{
    public async Task<Result<IssuedInvitation>> IssueAsync(
        Compartment compartment, string email, IReadOnlyCollection<string> roleNames, InvitationDelivery delivery, Actor actor,
        bool designatesOwner = false, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(compartment);
        Guard.NotNull(roleNames);
        Guard.NotNull(actor);
        email = Normalize(email);
        if (!IsEmail(email))
            return Result.Failure<IssuedInvitation>(RegistryErrors.InvitationEmailInvalid(email));
        var settings = options.Value.Invitations;
        if (delivery == InvitationDelivery.Hand && !settings.HandDelivery)
            return Result.Failure<IssuedInvitation>(RegistryErrors.HandDeliveryClosed());
        if (delivery == InvitationDelivery.Email && sender is null)
            return Result.Failure<IssuedInvitation>(RegistryErrors.NoInvitationSender());

        var record = await compartments.GetAsync(compartment, cancellationToken).ConfigureAwait(false);
        if (record is null)
            return Result.Failure<IssuedInvitation>(RegistryErrors.CompartmentNotFound(compartment));
        if (record.Status != CompartmentStatus.Active)
            return Result.Failure<IssuedInvitation>(RegistryErrors.CompartmentNotActive(compartment));

        var named = roleNames.Distinct(StringComparer.Ordinal).ToList();
        if (named.Contains(RegistryRoles.Owner, StringComparer.Ordinal))
            return Result.Failure<IssuedInvitation>(RegistryErrors.RoleReserved(RegistryRoles.Owner));
        foreach (var name in named)
        {
            var role = await roles.GetAsync(name, cancellationToken).ConfigureAwait(false);
            if (role is null)
                return Result.Failure<IssuedInvitation>(RegistryErrors.RoleNotFound(name));
            if (role.Scope != RoleScope.Compartment)
                return Result.Failure<IssuedInvitation>(RegistryErrors.RoleScopeMismatch(name, RoleScope.Compartment));
        }
        if (await rules.CoversAsync(actor, compartment, named, cancellationToken).ConfigureAwait(false) is { } refused)
            return Result.Failure<IssuedInvitation>(refused);
        if (designatesOwner)
        {
            if (record.Owner is not null)
                return Result.Failure<IssuedInvitation>(RegistryErrors.OwnerSeated(compartment));
            if (!await rules.DesignatesOwnersAsync(actor, cancellationToken).ConfigureAwait(false))
                return Result.Failure<IssuedInvitation>(RegistryErrors.RoleNotAssignable(RegistryRoles.Owner));
        }

        // Nobody invites their own address — an assignment deferred to themselves —, nor a member of the compartment,
        // whose roles change by a direct assignment.
        var invited = await users.FindByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        if (invited is not null && RegistryRules.IsSubject(actor.Value) && RegistryRules.NormalizeSubject(actor.Value) == invited.Subject)
            return Result.Failure<IssuedInvitation>(RegistryErrors.OwnRoles(invited.Subject));
        if (invited is not null && await memberships.IsMemberAsync(invited.Subject, compartment, cancellationToken).ConfigureAwait(false))
            return Result.Failure<IssuedInvitation>(RegistryErrors.AlreadyMember(email, compartment));

        var now = time.GetUtcNow();
        var issued = new Stamp(now, actor);
        foreach (var waiting in (await store.ListByCompartmentAsync(compartment, cancellationToken).ConfigureAwait(false))
                     .Where(invitation => invitation.Email == email && invitation.IsPendingAt(now)))
            await store.RevokeAsync(waiting.InvitationId, issued, cancellationToken).ConfigureAwait(false);

        var (token, hash) = InvitationTokens.New();
        var invitation = new Invitation(Guid.NewGuid(), hash, compartment, email, named, designatesOwner, delivery, issued, now + settings.Lifetime);
        await store.InsertAsync(invitation, cancellationToken).ConfigureAwait(false);
        if (delivery == InvitationDelivery.Hand)
            return Result.Success(new IssuedInvitation(invitation, token));

        try
        {
            await sender!.SendAsync(new InvitationMessage(email, token, invitation.ExpiresAt, compartment, record.DisplayName), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A link that never left must not stay usable.
            await store.RevokeAsync(invitation.InvitationId, issued, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return Result.Success(new IssuedInvitation(invitation, null));
    }

    public async Task<Result> RevokeAsync(Guid invitationId, Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        var invitation = await store.GetAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
            return Result.Failure(RegistryErrors.InvitationNotFound());
        if (!await MayRevokeAsync(invitation, actor, cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.RoleNotAssignable(invitation.Roles.FirstOrDefault() ?? RegistryRoles.Owner));
        return await store.RevokeAsync(invitationId, new Stamp(time.GetUtcNow(), actor), cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : Result.Failure(RegistryErrors.InvitationNotPending());
    }

    public async Task<Result<InvitationPreview>> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindAsync(token, cancellationToken).ConfigureAwait(false);
        return invitation is null || !invitation.IsPendingAt(time.GetUtcNow())
            ? Result.Failure<InvitationPreview>(RegistryErrors.InvitationInvalid())
            : Result.Success(new InvitationPreview(invitation.InvitationId, invitation.Email, invitation.Delivery, invitation.ExpiresAt));
    }

    public async Task<Result> AcceptAsync(string token, string subject, bool emailVerified, CancellationToken cancellationToken = default)
    {
        subject = RegistryRules.NormalizeSubject(subject);
        var invitation = await FindAsync(token, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (invitation is null || !invitation.IsPendingAt(now))
            return Result.Failure(RegistryErrors.InvitationInvalid());

        // Everything again, all or nothing: what the invitation assumed may have changed since it was issued.
        var why = await WhyNotAsync(invitation, subject, emailVerified, cancellationToken).ConfigureAwait(false);
        if (why is not null)
            return Result.Failure(RegistryErrors.InvitationStale(why));
        if (!await store.TryAcceptAsync(invitation.TokenHash, subject, now, cancellationToken).ConfigureAwait(false))
            return Result.Failure(RegistryErrors.InvitationInvalid());

        // Granted by the author, who answers for them (A8); dated by the acceptance, when they take effect.
        var author = invitation.Issued.By;
        if (!await memberships.IsMemberAsync(subject, invitation.Compartment, cancellationToken).ConfigureAwait(false))
        {
            var added = await membershipService.AddAsync(subject, invitation.Compartment, author, cancellationToken).ConfigureAwait(false);
            if (added.IsFailure)
                return added;
        }
        foreach (var role in invitation.Roles)
        {
            var assigned = await membershipService.AssignRoleAsync(subject, invitation.Compartment, role, author, cancellationToken).ConfigureAwait(false);
            if (assigned.IsFailure)
                return assigned;
        }
        if (invitation.DesignatesOwner)
        {
            var seated = await ownership.SeatAsync(invitation.Compartment, subject, author, cancellationToken).ConfigureAwait(false);
            if (seated.IsFailure)
                return seated;
        }
        await publisher.PublishAsync(new InvitationAcceptedEvent { OccurredAt = now, InvitationId = invitation.InvitationId, Compartment = invitation.Compartment, Subject = subject, By = author }, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<Invitation>> ListAsync(Compartment compartment, CancellationToken cancellationToken = default)
        => store.ListByCompartmentAsync(Guard.NotNull(compartment), cancellationToken);

    public Task<int> PurgeAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(actor);
        return store.DeleteDeadBeforeAsync(time.GetUtcNow() - options.Value.Invitations.Retention, cancellationToken);
    }

    private async Task<Invitation?> FindAsync(string token, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(token) ? null : await store.GetByHashAsync(InvitationTokens.Hash(token), cancellationToken).ConfigureAwait(false);

    // Why the invitation cannot be accepted by the account now, or null.
    private async Task<string?> WhyNotAsync(Invitation invitation, string subject, bool emailVerified, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(subject, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatus.Active)
            return "the account is not an active user of the directory";
        if (!string.Equals(Normalize(user.Email), invitation.Email, StringComparison.Ordinal) || !emailVerified)
            return "the account does not carry the address invited, verified";
        var record = await compartments.GetAsync(invitation.Compartment, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status != CompartmentStatus.Active)
            return "the compartment is not active";
        if (invitation.DesignatesOwner && record.Owner is not null)
            return "the compartment has an owner already";

        var author = invitation.Issued.By;
        if (AssignmentRules.IsJob(author))
            return null;
        if (!RegistryRules.IsSubject(author.Value))
            return "its author holds no role";
        var authorRecord = await users.GetAsync(RegistryRules.NormalizeSubject(author.Value), cancellationToken).ConfigureAwait(false);
        if (authorRecord is null || authorRecord.Status != UserStatus.Active)
            return "its author is no longer an active user";
        if (!await rules.HoldsAsync(author, invitation.Compartment, RegistryOperations.InvitationsWrite.Identity, cancellationToken).ConfigureAwait(false))
            return "its author no longer holds invitations.write";
        foreach (var role in invitation.Roles)
            if (await roles.GetAsync(role, cancellationToken).ConfigureAwait(false) is null)
                return $"role '{role}' no longer exists";
        if (await rules.CoversAsync(author, invitation.Compartment, invitation.Roles, cancellationToken).ConfigureAwait(false) is not null)
            return "its author may no longer assign every role it gives";
        if (invitation.DesignatesOwner && !await rules.DesignatesOwnersAsync(author, cancellationToken).ConfigureAwait(false))
            return "its author may no longer designate an owner";
        return null;
    }

    // The author, the owner of the compartment, or anyone who could issue it.
    private async Task<bool> MayRevokeAsync(Invitation invitation, Actor actor, CancellationToken cancellationToken)
    {
        if (AssignmentRules.IsJob(actor) || actor == invitation.Issued.By)
            return true;
        var record = await compartments.GetAsync(invitation.Compartment, cancellationToken).ConfigureAwait(false);
        if (record?.Owner is { } owner && RegistryRules.IsSubject(actor.Value) && RegistryRules.NormalizeSubject(actor.Value) == owner)
            return true;
        return await rules.CoversAsync(actor, invitation.Compartment, invitation.Roles, cancellationToken).ConfigureAwait(false) is null
               && (!invitation.DesignatesOwner || await rules.DesignatesOwnersAsync(actor, cancellationToken).ConfigureAwait(false));
    }

    private static string Normalize(string email) => Guard.NotNull(email).Trim().ToLowerInvariant();

    private static bool IsEmail(string email)
        => email.Length is >= 3 and <= 320 && email.IndexOf('@') is > 0 and var at && at < email.Length - 1 && !email.Any(char.IsWhiteSpace);
}
