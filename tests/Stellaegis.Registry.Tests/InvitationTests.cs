using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The invitation, an assignment deferred: issued under the assignable lists of its author, accepted by the account that
/// carries the address invited, verified, everything checked again then, all or nothing.
/// </summary>
public sealed class InvitationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Actor AsJane = Actor.Parse(Jane);
    private static readonly Actor AsJohn = Actor.Parse(John);
    private static readonly string Nina = Guid.Parse("5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293").ToString("N");

    /// <summary>
    /// Acme, John its manager of the accesses — invitations.write, the business roles assignable —, Jane a member, and
    /// a sender of e-mails; the hand delivery open when asked.
    /// </summary>
    private static async Task<(TestHost Host, CapturingInvitationSender Sender)> AcmeAsync(bool handDelivery = false)
    {
        var sender = new CapturingInvitationSender();
        var host = await new TestHost(configure: services =>
        {
            services.AddSingleton<IInvitationSender>(sender);
            services.Configure<RegistryOptions>(options => options.Invitations.HandDelivery = handDelivery);
        }).InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [RegistryOperations.InvitationsWrite.Identity], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        foreach (var subject in new[] { John, Jane })
            Assert.True((await host.Memberships.AddAsync(subject, Acme, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        return (host, sender);
    }

    private static IInvitations Invitations(TestHost host) => host.Services.GetRequiredService<IInvitations>();

    [Fact]
    public async Task An_invitation_sent_by_email_is_previewed_without_being_spent_then_accepted_once_granted_by_its_author()
    {
        var (host, sender) = await AcmeAsync();
        using var _ = host;

        var issued = await Invitations(host).IssueAsync(Acme, "Nina@Acme.test ", ["reader"], InvitationDelivery.Email, AsJohn, cancellationToken: Ct);
        Assert.True(issued.IsSuccess);
        Assert.Null(issued.Value.Token);                                // sent, never in the answer
        var message = Assert.Single(sender.Sent);
        Assert.Equal(("nina@acme.test", "Acme SA"), (message.Email, message.CompartmentName));
        Assert.Equal(64, issued.Value.Invitation.TokenHash.Length);
        Assert.DoesNotContain(message.Token, issued.Value.Invitation.TokenHash);

        var preview = await Invitations(host).PreviewAsync(message.Token, Ct);
        Assert.Equal(("nina@acme.test", InvitationDelivery.Email), (preview.Value.Email, preview.Value.Delivery));
        Assert.True((await Invitations(host).PreviewAsync(message.Token, Ct)).IsSuccess);   // opening it spends nothing

        Assert.True((await host.Users.RecordAsync(Nina, "nina@acme.test", "Nina", Actor.Parse(Nina), Ct)).IsSuccess);
        Assert.True((await Invitations(host).AcceptAsync(message.Token, Nina, emailVerified: true, Ct)).IsSuccess);
        var membership = (await host.Memberships.ListByUserAsync(Nina, Ct)).Single();
        Assert.Equal((Acme, AsJohn), (membership.Compartment, membership.Granted.By));
        Assert.Equal(["reader"], membership.Roles);
        var accepted = (await Invitations(host).ListAsync(Acme, Ct)).Single();
        Assert.Equal(Nina, accepted.AcceptedBy);

        Assert.Equal(RegistryErrors.InvitationInvalidCode, (await Invitations(host).AcceptAsync(message.Token, Nina, true, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.InvitationInvalidCode, (await Invitations(host).PreviewAsync(message.Token, Ct)).Error!.Code);
    }

    [Fact]
    public async Task An_invitation_is_issued_as_an_assignment_is_and_never_to_ones_own_address_nor_to_a_member()
    {
        var (host, _) = await AcmeAsync();
        using var __ = host;
        var invitations = Invitations(host);
        Task<Result<IssuedInvitation>> Issue(string email, string[] roles, Actor by, InvitationDelivery delivery = InvitationDelivery.Email)
            => invitations.IssueAsync(Acme, email, roles, delivery, by, cancellationToken: Ct);

        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await Issue("nina@acme.test", ["access_manager"], AsJohn)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleReservedCode, (await Issue("nina@acme.test", [RegistryRoles.Owner], AsJohn)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await Issue("nina@acme.test", ["reader"], AsJane)).Error!.Code);   // she assigns nothing
        Assert.Equal(RegistryErrors.OwnRolesCode, (await Issue("john@acme.test", ["reader"], AsJohn)).Error!.Code);
        Assert.Equal(RegistryErrors.AlreadyMemberCode, (await Issue("JANE@acme.test", ["reader"], AsJohn)).Error!.Code);
        Assert.Equal(RegistryErrors.InvitationEmailInvalidCode, (await Issue("nina", ["reader"], AsJohn)).Error!.Code);
        Assert.Equal(RegistryErrors.HandDeliveryClosedCode, (await Issue("nina@acme.test", ["reader"], AsJohn, InvitationDelivery.Hand)).Error!.Code);
        Assert.True((await host.Compartments.SuspendAsync(Acme, By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.CompartmentNotActiveCode, (await Issue("nina@acme.test", ["reader"], AsJohn)).Error!.Code);
        Assert.Empty(await invitations.ListAsync(Acme, Ct));
    }

    [Fact]
    public async Task Everything_is_checked_again_at_the_acceptance_all_or_nothing()
    {
        var (host, sender) = await AcmeAsync();
        using var _ = host;
        var invitations = Invitations(host);
        Assert.True((await invitations.IssueAsync(Acme, "nina@acme.test", ["reader"], InvitationDelivery.Email, AsJohn, cancellationToken: Ct)).IsSuccess);
        var token = sender.Sent.Single().Token;
        Assert.True((await host.Users.RecordAsync(Nina, "nina@acme.test", "Nina", Actor.Parse(Nina), Ct)).IsSuccess);
        Assert.True((await host.Users.RecordAsync(Guid.Parse("6c7d8e9f-0a1b-4c2d-9e3f-405162738495").ToString("N"), "other@acme.test", "Other", By, Ct)).IsSuccess);

        async Task Refused(string subject, bool verified = true)
        {
            Assert.Equal(RegistryErrors.InvitationStaleCode, (await invitations.AcceptAsync(token, subject, verified, Ct)).Error!.Code);
            Assert.DoesNotContain(Acme, (await host.Memberships.ListByUserAsync(Nina, Ct)).Select(m => m.Compartment));
        }

        await Refused(Guid.Parse("6c7d8e9f-0a1b-4c2d-9e3f-405162738495").ToString("N"));   // another address
        await Refused(Nina, verified: false);                                               // the address not proven
        Assert.True((await host.Memberships.UnassignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        await Refused(Nina);                                                                // the author lost the right
        Assert.True((await host.Memberships.AssignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await host.Users.DeactivateAsync(John, By, Ct)).IsSuccess);
        await Refused(Nina);                                                                // the author is gone
        Assert.True((await host.Users.ReactivateAsync(John, By, Ct)).IsSuccess);
        Assert.True((await host.Compartments.SuspendAsync(Acme, By, Ct)).IsSuccess);
        await Refused(Nina);                                                                // the compartment is suspended
        Assert.True((await host.Compartments.ResumeAsync(Acme, By, Ct)).IsSuccess);

        // None of those spent it; past its lifetime, it opens nothing.
        host.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        Assert.Equal(RegistryErrors.InvitationInvalidCode, (await invitations.AcceptAsync(token, Nina, true, Ct)).Error!.Code);
    }

    [Fact]
    public async Task A_newer_invitation_revokes_the_waiting_one_and_revoking_takes_the_author_the_owner_or_anyone_who_could_issue_it()
    {
        var (host, sender) = await AcmeAsync(handDelivery: true);
        using var _ = host;
        var invitations = Invitations(host);

        var first = await invitations.IssueAsync(Acme, "nina@acme.test", ["reader"], InvitationDelivery.Hand, AsJohn, cancellationToken: Ct);
        Assert.NotNull(first.Value.Token);                              // handed over: in the answer, this once
        Assert.Empty(sender.Sent);
        var second = await invitations.IssueAsync(Acme, "nina@acme.test", ["reader"], InvitationDelivery.Hand, AsJohn, cancellationToken: Ct);
        Assert.Equal(RegistryErrors.InvitationInvalidCode, (await invitations.PreviewAsync(first.Value.Token!, Ct)).Error!.Code);
        Assert.Equal(InvitationDelivery.Hand, (await invitations.PreviewAsync(second.Value.Token!, Ct)).Value.Delivery);

        var id = second.Value.Invitation.InvitationId;
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await invitations.RevokeAsync(id, AsJane, Ct)).Error!.Code);
        Assert.True((await host.Roles.CreateAsync("second_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Second manager", [], AssignableRoles.Of("reader")), By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "second_manager", By, Ct)).IsSuccess);
        Assert.True((await invitations.RevokeAsync(id, AsJane, Ct)).IsSuccess);           // she could issue it
        Assert.Equal(RegistryErrors.InvitationNotPendingCode, (await invitations.RevokeAsync(id, AsJohn, Ct)).Error!.Code);
    }

    [Fact]
    public async Task An_operator_designates_the_owner_of_a_compartment_that_has_none_by_an_invitation()
    {
        var (host, sender) = await AcmeAsync();
        using var _ = host;
        var invitations = Invitations(host);
        var initech = Compartment.Parse("initech");
        Assert.True((await host.Compartments.CreateAsync(initech, "Initech", By, Ct)).IsSuccess);
        // An operator who invites, and whose list covers every compartment role: the owner's.
        Assert.True((await host.Roles.CreateAsync("platform_inviter", RoleScope.Platform,
            new RoleDefinition(RoleCategory.Administration, "Inviter", [RegistryOperations.InvitationsWrite.Identity], AssignableRoles.Every(CompartmentRoleSelector.All)), By, Ct)).IsSuccess);
        Assert.True((await host.Operators.AssignAsync(Jane, "platform_inviter", By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await invitations.IssueAsync(initech, "boss@initech.test", [], InvitationDelivery.Email, AsJohn, designatesOwner: true, Ct)).Error!.Code);
        Assert.True((await invitations.IssueAsync(initech, "boss@initech.test", ["reader"], InvitationDelivery.Email, AsJane, designatesOwner: true, Ct)).IsSuccess);
        Assert.True((await host.Users.RecordAsync(Nina, "boss@initech.test", "Boss", Actor.Parse(Nina), Ct)).IsSuccess);
        Assert.True((await invitations.AcceptAsync(sender.Sent.Single().Token, Nina, emailVerified: true, Ct)).IsSuccess);

        Assert.Equal(Nina, (await host.Compartments.GetAsync(initech, Ct)).Value.Owner);
        Assert.Equal([RegistryRoles.Owner, "reader"], (await host.Memberships.ListByUserAsync(Nina, Ct)).Single().Roles.Order(StringComparer.Ordinal));
        Assert.Equal(RegistryErrors.OwnerSeatedCode, (await invitations.IssueAsync(initech, "x@initech.test", [], InvitationDelivery.Email, AsJane, designatesOwner: true, Ct)).Error!.Code);
    }

    [Fact]
    public async Task The_purge_removes_the_invitations_dead_past_their_retention_and_keeps_the_accepted_ones()
    {
        var (host, sender) = await AcmeAsync();
        using var _ = host;
        var invitations = Invitations(host);
        Assert.True((await invitations.IssueAsync(Acme, "nina@acme.test", ["reader"], InvitationDelivery.Email, AsJohn, cancellationToken: Ct)).IsSuccess);
        Assert.True((await host.Users.RecordAsync(Nina, "nina@acme.test", "Nina", Actor.Parse(Nina), Ct)).IsSuccess);
        Assert.True((await invitations.AcceptAsync(sender.Sent[0].Token, Nina, true, Ct)).IsSuccess);
        var lapsing = await invitations.IssueAsync(Acme, "ola@acme.test", ["reader"], InvitationDelivery.Email, AsJohn, cancellationToken: Ct);
        var revoked = await invitations.IssueAsync(Acme, "pia@acme.test", ["reader"], InvitationDelivery.Email, AsJohn, cancellationToken: Ct);
        Assert.True((await invitations.RevokeAsync(revoked.Value.Invitation.InvitationId, AsJohn, Ct)).IsSuccess);

        Assert.Equal(0, await invitations.PurgeAsync(RegistryJobs.Purge, Ct));
        host.Clock.Advance(TimeSpan.FromDays(40));
        Assert.Equal(2, await invitations.PurgeAsync(RegistryJobs.Purge, Ct));
        Assert.Equal(Nina, Assert.Single(await invitations.ListAsync(Acme, Ct)).AcceptedBy);
        Assert.Equal("job:registry_purge", RegistryJobs.Purge.Value);
    }

    [Fact]
    public void The_options_hold_a_positive_lifetime_and_a_retention_never_lowered()
    {
        Assert.Null(new RegistryOptions().FindError());
        Assert.Equal("Invitations: Retention must be at least 30 days.", new RegistryOptions { Invitations = { Retention = TimeSpan.FromDays(7) } }.FindError());
        Assert.Equal("Invitations: Lifetime must be positive.", new RegistryOptions { Invitations = { Lifetime = TimeSpan.Zero } }.FindError());
    }
}
