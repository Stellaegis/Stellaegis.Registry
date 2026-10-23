using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The three sensitive acts take effect only once a second channel validated them: on screen a person other than the
/// author, holding validations.write; at the console, the validate verb of the host. The installation applies at once.
/// </summary>
public sealed class ValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Actor AsJane = Actor.Parse(Jane);
    private static readonly Actor AsJohn = Actor.Parse(John);
    private static readonly string Nina = Guid.Parse("5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293").ToString("N");
    private static readonly Actor AsNina = Actor.Parse(Nina);
    private static readonly Actor Console = Actor.Parse(@"system:SATURN-fred");

    /// <summary>
    /// John an operator — super_admin, every role assignable —, Nina a validator — validations.write, every platform
    /// role assignable —, Jane a user; the platform role ops to give. All seated by the installation.
    /// </summary>
    private static async Task<TestHost> PlatformAsync()
    {
        var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Users.RecordAsync(Nina, "nina@acme.test", "Nina", By, Ct)).IsSuccess);
        Assert.True((await host.Roles.CreateAsync("validator", RoleScope.Platform, new RoleDefinition(RoleCategory.Administration, "Validator",
            [RegistryOperations.ValidationsWrite.Identity], AssignableRoles.Every(CompartmentRoleSelector.None, allPlatform: true)), By, Ct)).IsSuccess);
        Assert.True((await host.Roles.CreateAsync("ops", RoleScope.Platform, new RoleDefinition(RoleCategory.Administration, "Operations", [ItemsPurge.Identity]), By, Ct)).IsSuccess);
        Assert.Null((await host.Operators.AssignAsync(John, "super_admin", By, Ct)).Value);   // the installation: at once
        Assert.Null((await host.Operators.AssignAsync(Nina, "validator", By, Ct)).Value);
        host.Sink.Clear();
        return host;
    }

    private static IValidations Validations(TestHost host) => host.Services.GetRequiredService<IValidations>();

    [Fact]
    public async Task A_platform_role_waits_for_a_validation_by_someone_else_holding_validations_write_and_then_takes_effect()
    {
        using var host = await PlatformAsync();

        var proposed = await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct);
        var act = proposed.Value!;
        Assert.Equal((PendingActKind.PlatformRole, Jane, "ops"), (act.Kind, act.Subject, act.Role));
        Assert.Empty(await host.Operators.ListRolesAsync(Jane, Ct));                // nowhere until validated
        Assert.Empty(host.Sink.Calls);
        Assert.Equal(RegistryErrors.ActPendingCode, (await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct)).Error!.Code);

        Assert.Equal(RegistryErrors.OwnActCode, (await Validations(host).ValidateAsync(act.ActId, AsJohn, ValidationChannel.Screen, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.NotValidatorCode, (await Validations(host).ValidateAsync(act.ActId, AsJane, ValidationChannel.Screen, Ct)).Error!.Code);
        Assert.True((await Validations(host).ValidateAsync(act.ActId, AsNina, ValidationChannel.Screen, Ct)).IsSuccess);

        Assert.Equal(["ops"], await host.Operators.ListRolesAsync(Jane, Ct));
        Assert.Equal(["items.purge"], host.Sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!.Values);
        var closed = (await Validations(host).ListAsync(Ct)).Single(a => a.ActId == act.ActId);
        Assert.Equal((AsJohn, AsNina), (closed.Proposed.By, closed.Validated!.By));
        Assert.Contains(host.Events.Events, e => e is PendingActClosedEvent { Validated: true });
        Assert.Equal(RegistryErrors.ActClosedCode, (await Validations(host).ValidateAsync(act.ActId, Console, ValidationChannel.Console, Ct)).Error!.Code);
    }

    [Fact]
    public async Task The_definition_of_a_role_waits_too_but_its_display_name_changes_at_once()
    {
        using var host = await PlatformAsync();

        var created = await host.Roles.CreateAsync("clerk", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Clerk", [ItemsRead.Identity]), AsJohn, Ct);
        Assert.True(created.Value.IsPending);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, (await host.Roles.GetAsync("clerk", Ct)).Error!.Code);
        Assert.True((await Validations(host).ValidateAsync(created.Value.Pending!.ActId, Console, ValidationChannel.Console, Ct)).IsSuccess);   // the only operator, at the console
        var clerk = (await host.Roles.GetAsync("clerk", Ct)).Value;
        Assert.Equal(AsJohn, clerk.Audit.Created.By);
        Assert.Equal(["items.read"], clerk.Operations);

        var renamed = await host.Roles.UpdateAsync("clerk", new RoleDefinition(RoleCategory.Business, "Desk clerk", [ItemsRead.Identity]), AsJohn, Ct);
        Assert.False(renamed.Value.IsPending);
        Assert.Equal("Desk clerk", renamed.Value.Value!.DisplayName);
        var widened = await host.Roles.UpdateAsync("clerk", new RoleDefinition(RoleCategory.Business, "Desk clerk", [ItemsRead.Identity, ItemsWrite.Identity]), AsJohn, Ct);
        Assert.True(widened.Value.IsPending);
        Assert.Equal(["items.read"], (await host.Roles.GetAsync("clerk", Ct)).Value.Operations);

        // Who holds the role does not validate its new definition.
        Assert.True((await host.Memberships.AddAsync(Nina, Acme, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Nina, Acme, "clerk", By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.RoleHeldByAuthorCode, (await Validations(host).ValidateAsync(widened.Value.Pending!.ActId, AsNina, ValidationChannel.Screen, Ct)).Error!.Code);
    }

    [Fact]
    public async Task The_platform_rescues_an_ownership_once_validated_the_former_owner_keeping_their_other_roles()
    {
        using var host = await PlatformAsync();
        var globex = Compartment.Parse("globex");
        Assert.True((await host.Compartments.CreateAsync(globex, "Globex", Jane, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, globex, "reader", AsJane, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AddAsync(John, globex, By, Ct)).IsSuccess);
        Assert.True((await host.Services.GetRequiredService<IOwnership>().ProposeAsync(globex, John, null, AsJane, Ct)).IsSuccess);
        var ownership = host.Services.GetRequiredService<IOwnership>();

        Assert.Equal(RegistryErrors.OwnRolesCode, (await ownership.RescueAsync(globex, John, AsJohn, Ct)).Error!.Code);   // never for oneself
        var proposed = await ownership.RescueAsync(globex, Nina, AsJohn, Ct);
        Assert.Equal(Jane, (await host.Compartments.GetAsync(globex, Ct)).Value.Owner);
        Assert.True((await Validations(host).ValidateAsync(proposed.Value!.ActId, Console, ValidationChannel.Console, Ct)).IsSuccess);

        Assert.Equal(Nina, (await host.Compartments.GetAsync(globex, Ct)).Value.Owner);
        var roles = (await host.Memberships.ListByCompartmentAsync(globex, Ct)).ToDictionary(m => m.Subject, m => m.Roles);
        Assert.Equal([RegistryRoles.Owner], roles[Nina]);
        Assert.Equal(["reader"], roles[Jane]);
        Assert.Equal(RegistryErrors.TransferNotFoundCode, (await ownership.GetTransferAsync(globex, Ct)).Error!.Code);
    }

    [Fact]
    public async Task An_act_is_withdrawn_by_its_author_rejected_by_a_validator_and_lapses_after_seven_days()
    {
        using var host = await PlatformAsync();
        var first = (await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct)).Value!;
        Assert.True((await Validations(host).RejectAsync(first.ActId, AsJohn, ValidationChannel.Screen, Ct)).IsSuccess);   // withdrawn
        var second = (await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct)).Value!;
        Assert.Equal(RegistryErrors.NotValidatorCode, (await Validations(host).RejectAsync(second.ActId, AsJane, ValidationChannel.Screen, Ct)).Error!.Code);
        Assert.True((await Validations(host).RejectAsync(second.ActId, AsNina, ValidationChannel.Screen, Ct)).IsSuccess);

        var third = (await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct)).Value!;
        host.Clock.Advance(PendingAct.Lifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(RegistryErrors.ActExpiredCode, (await Validations(host).ValidateAsync(third.ActId, Console, ValidationChannel.Console, Ct)).Error!.Code);
        Assert.Empty(await host.Operators.ListRolesAsync(Jane, Ct));
        Assert.NotNull((await host.Operators.AssignAsync(Jane, "ops", AsJohn, Ct)).Value);   // lapsed: proposed again
    }
}
