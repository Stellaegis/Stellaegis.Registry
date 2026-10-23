using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The owner of a compartment: seated by the installation, every administration right of it, untouchable by anyone
/// else, the ownership transferred in two steps and checked again at its acceptance.
/// </summary>
public sealed class OwnershipTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Actor AsJane = Actor.Parse(Jane);
    private static readonly Actor AsJohn = Actor.Parse(John);

    /// <summary>Globex created by the installation with Jane its owner, holding reader and accountant too, and John a member.</summary>
    private static async Task<TestHost> GlobexAsync()
    {
        var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        Assert.True((await host.Compartments.CreateAsync(Globex, "Globex", Jane, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Globex, "reader", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Globex, "accountant", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AddAsync(John, Globex, By, Ct)).IsSuccess);
        return host;
    }

    private static async Task<IReadOnlyCollection<string>> RolesOfAsync(TestHost host, string subject, Compartment compartment)
        => (await host.Memberships.ListByUserAsync(subject, Ct)).SingleOrDefault(membership => membership.Compartment == compartment)?.Roles ?? [];

    [Fact]
    public async Task The_installation_seats_the_owner_with_the_compartment_its_first_member_with_every_administration_right()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.DeclareAsync([new RoleDeclaration(RegistryRoles.Owner, RoleScope.Compartment, RoleCategory.Business, "Propriétaire", [ItemsRead])], By, Ct)).IsSuccess);

        var created = await host.Compartments.CreateAsync(Globex, "Globex", Jane, By, Ct);

        Assert.Equal(Jane, created.Value.Owner);
        Assert.Equal([RegistryRoles.Owner], await RolesOfAsync(host, Jane, Globex));
        var owner = (await host.Roles.GetAsync(RegistryRoles.Owner, Ct)).Value;
        Assert.Equal((RoleCategory.Administration, CompartmentRoleSelector.All, true), (owner.Category, owner.Assignable.Compartment, owner.Declared));
        Assert.Equal(["items.read"], owner.Operations);   // the declaration gives its operations, never its nature
        Assert.Equal(["items.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
        Assert.Contains(host.Events.Events, e => e is OwnershipChangedEvent { Owner: var o, Previous: null } && o == Jane);

        // Seated once; nobody seats themselves; an owner who cannot be seated leaves no compartment behind.
        Assert.Equal(RegistryErrors.OwnerSeatedCode, (await host.Services.GetRequiredService<IOwnership>().SeatAsync(Globex, John, By, Ct)).Error!.Code);
        Assert.True((await host.Compartments.CreateAsync(Compartment.Parse("initech"), "Initech", By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Services.GetRequiredService<IOwnership>().SeatAsync(Compartment.Parse("initech"), John, AsJohn, Ct)).Error!.Code);
        Assert.True((await host.Services.GetRequiredService<IOwnership>().SeatAsync(Compartment.Parse("initech"), John, AsJane, Ct)).IsSuccess);
        var unknown = await host.Compartments.CreateAsync(Compartment.Parse("hooli"), "Hooli", Guid.NewGuid().ToString("N"), By, Ct);
        Assert.Equal(RegistryErrors.UserNotFoundCode, unknown.Error!.Code);
        Assert.Equal(RegistryErrors.CompartmentNotFoundCode, (await host.Compartments.GetAsync(Compartment.Parse("hooli"), Ct)).Error!.Code);
    }

    [Fact]
    public async Task Nobody_else_touches_the_owner_not_even_an_operator_and_the_role_of_the_owner_goes_with_the_ownership_only()
    {
        using var host = await GlobexAsync();
        Assert.True((await host.Operators.AssignAsync(John, "super_admin", By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.OwnerUntouchableCode, (await host.Memberships.UnassignRoleAsync(Jane, Globex, "reader", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnerUntouchableCode, (await host.Memberships.AssignRoleAsync(Jane, Globex, "access_manager", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnerUntouchableCode, (await host.Memberships.RemoveAsync(Jane, Globex, AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleReservedCode, (await host.Memberships.AssignRoleAsync(John, Globex, RegistryRoles.Owner, By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleReservedCode, (await host.Memberships.RemoveAsync(Jane, Globex, By, Ct)).Error!.Code);   // not even the installation
        Assert.Equal(RegistryErrors.RoleReservedCode, (await host.Roles.CreateAsync(RegistryRoles.Owner, RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Owner", []), By, Ct)).Error!.Code);
        Assert.Equal(["accountant", RegistryRoles.Owner, "reader"], (await RolesOfAsync(host, Jane, Globex)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_owner_gives_themselves_business_roles_and_takes_them_back_and_nothing_more()
    {
        using var host = await GlobexAsync();

        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Globex, "reader", AsJane, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Globex, "reader", AsJane, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.AssignRoleAsync(Jane, Globex, "access_manager", AsJane, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleReservedCode, (await host.Memberships.UnassignRoleAsync(Jane, Globex, RegistryRoles.Owner, AsJane, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.RemoveAsync(Jane, Globex, AsJane, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleHeldByAuthorCode, (await host.Roles.UpdateAsync(RegistryRoles.Owner,
            new RoleDefinition(RoleCategory.Administration, "Owner", [ItemsPurge.Identity]), AsJane, Ct)).Error!.Code);

        // And as the owner, she assigns every role of her compartment to others, administration included.
        Assert.True((await host.Memberships.AssignRoleAsync(John, Globex, "access_manager", AsJane, Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_ownership_is_proposed_then_accepted_and_the_outgoing_owner_keeps_what_they_chose()
    {
        using var host = await GlobexAsync();
        var ownership = host.Services.GetRequiredService<IOwnership>();

        var proposed = await ownership.ProposeAsync(Globex, John, ["reader"], AsJane, Ct);
        Assert.True(proposed.IsSuccess);
        Assert.Equal(proposed.Value.Proposed.At + RegistryRoles.TransferLifetime, proposed.Value.ExpiresAt);
        Assert.True((await ownership.AcceptAsync(Globex, AsJohn, Ct)).IsSuccess);

        Assert.Equal(John, (await host.Compartments.GetAsync(Globex, Ct)).Value.Owner);
        Assert.Equal([RegistryRoles.Owner], await RolesOfAsync(host, John, Globex));
        Assert.Equal(["reader"], await RolesOfAsync(host, Jane, Globex));        // accountant went with the ownership
        Assert.Equal(RegistryErrors.TransferNotFoundCode, (await ownership.GetTransferAsync(Globex, Ct)).Error!.Code);
        Assert.Contains(host.Events.Events, e => e is OwnershipChangedEvent { Previous: var p } && p == Jane);

        // Without a choice, the outgoing owner keeps every role they hold.
        Assert.True((await host.Memberships.AssignRoleAsync(John, Globex, "accountant", AsJohn, Ct)).IsSuccess);
        Assert.True((await ownership.ProposeAsync(Globex, Jane, null, AsJohn, Ct)).IsSuccess);
        Assert.True((await ownership.AcceptAsync(Globex, AsJane, Ct)).IsSuccess);
        Assert.Equal(["accountant"], await RolesOfAsync(host, John, Globex));
    }

    [Fact]
    public async Task A_transfer_goes_from_the_owner_to_an_active_member_and_is_answered_by_its_recipient_only()
    {
        using var host = await GlobexAsync();
        var ownership = host.Services.GetRequiredService<IOwnership>();

        Assert.Equal(RegistryErrors.NotOwnerCode, (await ownership.ProposeAsync(Globex, Jane, null, AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.NotOwnerCode, (await ownership.ProposeAsync(Globex, John, null, By, Ct)).Error!.Code);   // a prerogative: no job
        Assert.Equal(RegistryErrors.TransferToSelfCode, (await ownership.ProposeAsync(Globex, Jane, null, AsJane, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotHeldCode, (await ownership.ProposeAsync(Globex, John, ["access_manager"], AsJane, Ct)).Error!.Code);
        Assert.True((await host.Memberships.RemoveAsync(John, Globex, By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.TransferStaleCode, (await ownership.ProposeAsync(Globex, John, null, AsJane, Ct)).Error!.Code);
        Assert.True((await host.Memberships.AddAsync(John, Globex, By, Ct)).IsSuccess);

        Assert.True((await ownership.ProposeAsync(Globex, John, null, AsJane, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.NotRecipientCode, (await ownership.AcceptAsync(Globex, AsJane, Ct)).Error!.Code);
        Assert.True((await ownership.CancelAsync(Globex, AsJohn, Ct)).IsSuccess);           // the recipient declines
        Assert.Equal(RegistryErrors.TransferNotFoundCode, (await ownership.AcceptAsync(Globex, AsJohn, Ct)).Error!.Code);
        Assert.Equal(Jane, (await host.Compartments.GetAsync(Globex, Ct)).Value.Owner);
    }

    [Fact]
    public async Task A_transfer_is_checked_again_at_its_acceptance_and_lapses_after_seven_days()
    {
        using var host = await GlobexAsync();
        var ownership = host.Services.GetRequiredService<IOwnership>();
        Assert.True((await ownership.ProposeAsync(Globex, John, null, AsJane, Ct)).IsSuccess);

        Assert.True((await host.Users.DeactivateAsync(John, By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.TransferStaleCode, (await ownership.AcceptAsync(Globex, AsJohn, Ct)).Error!.Code);
        Assert.True((await host.Users.ReactivateAsync(John, By, Ct)).IsSuccess);
        Assert.True((await host.Compartments.SuspendAsync(Globex, By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.TransferStaleCode, (await ownership.AcceptAsync(Globex, AsJohn, Ct)).Error!.Code);
        Assert.True((await host.Compartments.ResumeAsync(Globex, By, Ct)).IsSuccess);

        host.Clock.Advance(RegistryRoles.TransferLifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(RegistryErrors.TransferExpiredCode, (await ownership.AcceptAsync(Globex, AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.TransferNotFoundCode, (await ownership.GetTransferAsync(Globex, Ct)).Error!.Code);
        Assert.Equal(Jane, (await host.Compartments.GetAsync(Globex, Ct)).Value.Owner);
    }
}
