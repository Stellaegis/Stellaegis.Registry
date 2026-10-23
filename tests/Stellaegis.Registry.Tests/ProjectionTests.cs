using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// Every change that touches rights hands a snapshot to the sink, synchronously, in the same operation. The
/// scenarios are replayed on each store: <see cref="InMemoryProjectionTests"/>, <see cref="PostgreSqlProjectionTests"/>.
/// </summary>
public abstract class ProjectionTestsBase
{
    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A host on the stores under test.</summary>
    private protected abstract TestHost CreateHost(bool withSink = true);

    [Fact]
    public async Task A_membership_added_projects_the_memberships_of_the_person()
    {
        using var host = await CreateHost().InstalledAsync(Ct);

        Assert.True((await host.Memberships.AddAsync(Jane, Acme, By, Ct)).IsSuccess);

        var call = Assert.Single(host.Sink.Calls);
        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.ReplaceMemberships, Jane, null, ["acme"]), call);
        Assert.Contains(host.Events.Events, e => e is MembershipChangedEvent { Subject: var s, IsMember: true } && s == Jane);
    }

    [Fact]
    public async Task A_role_assigned_projects_the_union_of_the_roles_in_the_compartment()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.Equal(["invoices.read", "items.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "accountant", By, Ct)).IsSuccess);
        var last = host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!;
        Assert.Equal(Acme, last.Compartment);
        Assert.Equal(["invoices.issue", "invoices.read", "items.read"], last.Values);
        Assert.Contains(host.Events.Events, e => e is RightsChangedEvent { Subject: var s, Compartment: var c } && s == Jane && c == Acme);

        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.Equal(["invoices.issue", "invoices.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
    }

    [Fact]
    public async Task A_role_needs_a_member_and_the_right_scope()
    {
        using var host = await CreateHost().InstalledAsync(Ct);

        Assert.Equal(RegistryErrors.NotMemberCode, (await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).Error!.Code);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        Assert.Equal(RegistryErrors.RoleScopeMismatchCode, (await host.Memberships.AssignRoleAsync(Jane, Acme, "super_admin", By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleScopeMismatchCode, (await host.Operators.AssignAsync(Jane, "reader", By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, (await host.Operators.AssignAsync(Jane, "nobody", By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.CompartmentNotFoundCode, (await host.Memberships.AddAsync(Jane, Globex, By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.UserNotFoundCode, (await host.Memberships.AddAsync(Guid.NewGuid().ToString("N"), Acme, By, Ct)).Error!.Code);
    }

    [Fact]
    public async Task A_platform_role_projects_the_admin_grants()
    {
        using var host = await CreateHost().InstalledAsync(Ct);

        Assert.True((await host.Operators.AssignAsync(Jane, "super_admin", By, Ct)).IsSuccess);

        var call = Assert.Single(host.Sink.Calls);
        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.ReplaceAdminGrants, Jane, null, ["compartments.write", "items.purge"]), call);
        Assert.Equal(["super_admin"], await host.Operators.ListRolesAsync(Jane, Ct));

        Assert.True((await host.Operators.UnassignAsync(Jane, "super_admin", By, Ct)).IsSuccess);
        Assert.Empty(host.Sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!.Values);
        Assert.Empty(await host.Operators.ListAsync(Ct));
    }

    [Fact]
    public async Task A_role_changed_recomputes_every_holder()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        Assert.True((await host.Compartments.CreateAsync(Globex, "Globex", By, Ct)).IsSuccess);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        await host.Memberships.AddAsync(John, Globex, By, Ct);
        await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct);
        await host.Memberships.AssignRoleAsync(John, Globex, "reader", By, Ct);
        await host.Memberships.AssignRoleAsync(John, Globex, "accountant", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Roles.UpdateAsync("reader", new RoleDefinition(RoleCategory.Business, "Reader", [ItemsRead.Identity]), By, Ct)).IsSuccess);

        var calls = host.Sink.Calls.Where(c => c.Kind == RightsSinkCallKind.ReplaceUserGrants).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(["items.read"], calls.Single(c => c.Subject == Jane).Values);
        Assert.Equal(["invoices.issue", "invoices.read", "items.read"], calls.Single(c => c.Subject == John).Values);

        // Re-declaring the role as it was declared puts invoices.read back, for both.
        host.Sink.Clear();
        Assert.True((await host.Roles.DeclareAsync([Reader], By, Ct)).IsSuccess);
        Assert.Equal(["invoices.read", "items.read"], host.Sink.Calls.Single(c => c.Subject == Jane).Values);

        // Declaring it again, unchanged, projects nothing.
        host.Sink.Clear();
        Assert.True((await host.Roles.DeclareAsync([Reader], By, Ct)).IsSuccess);
        Assert.Empty(host.Sink.Calls);
    }

    [Fact]
    public async Task A_platform_role_changed_recomputes_its_operators()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Operators.AssignAsync(Jane, "super_admin", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Roles.UpdateAsync("super_admin", new RoleDefinition(RoleCategory.Administration, "Platform administrator", [ItemsPurge.Identity], AssignableRoles.Every(CompartmentRoleSelector.All, allPlatform: true)), By, Ct)).IsSuccess);

        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.ReplaceAdminGrants, Jane, null, ["items.purge"]), Assert.Single(host.Sink.Calls));
    }

    [Fact]
    public async Task A_user_deactivated_loses_everything_and_a_reactivated_one_gets_it_back()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct);
        await host.Operators.AssignAsync(Jane, "super_admin", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Users.DeactivateAsync(Jane, By, Ct)).IsSuccess);
        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.RemoveSubject, Jane, null, []), Assert.Single(host.Sink.Calls));
        Assert.Equal(RegistryErrors.UserDeactivatedCode, (await host.Memberships.AssignRoleAsync(Jane, Acme, "accountant", By, Ct)).Error!.Code);
        Assert.Single(await host.Memberships.ListByUserAsync(Jane, Ct));   // kept in the registry

        host.Sink.Clear();
        Assert.True((await host.Users.ReactivateAsync(Jane, By, Ct)).IsSuccess);
        Assert.Equal(["acme"], host.Sink.Last(RightsSinkCallKind.ReplaceMemberships)!.Values);
        Assert.Equal(["invoices.read", "items.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
        Assert.Equal(["compartments.write", "items.purge"], host.Sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!.Values);
        Assert.Contains(host.Events.Events, e => e is UserChangedEvent { Status: UserStatus.Active, Subject: var s } && s == Jane);
    }

    [Fact]
    public async Task A_compartment_terminated_is_removed_from_the_sink_and_from_the_registry()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Compartments.TerminateAsync(Acme, By, Ct)).IsSuccess);

        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.RemoveCompartment, null, Acme, []), Assert.Single(host.Sink.Calls));
        Assert.Empty(await host.Memberships.ListByCompartmentAsync(Acme, Ct));
        Assert.Equal(CompartmentStatus.Terminated, (await host.Compartments.GetAsync(Acme, Ct)).Value.Status);
        Assert.Equal(RegistryErrors.CompartmentTerminatedCode, (await host.Memberships.AddAsync(Jane, Acme, By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.CompartmentExistsCode, (await host.Compartments.CreateAsync(Acme, "Acme again", By, Ct)).Error!.Code);
        Assert.Contains(host.Events.Events, e => e is CompartmentChangedEvent { Status: CompartmentStatus.Terminated });
    }

    [Fact]
    public async Task A_compartment_suspended_holds_no_right_until_it_resumes()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Compartments.SuspendAsync(Acme, By, Ct)).IsSuccess);
        Assert.Equal(RightsSinkCallKind.RemoveCompartment, Assert.Single(host.Sink.Calls).Kind);
        Assert.Single(await host.Memberships.ListByCompartmentAsync(Acme, Ct));   // kept in the registry

        // A role assigned while suspended is recorded, and projects nothing but an empty snapshot.
        host.Sink.Clear();
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "accountant", By, Ct)).IsSuccess);
        Assert.Empty(host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);

        host.Sink.Clear();
        Assert.True((await host.Compartments.ResumeAsync(Acme, By, Ct)).IsSuccess);
        Assert.Equal(["acme"], host.Sink.Last(RightsSinkCallKind.ReplaceMemberships)!.Values);
        Assert.Equal(["invoices.issue", "invoices.read", "items.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
    }

    [Fact]
    public async Task A_membership_removed_projects_empty_rights_in_the_compartment()
    {
        using var host = await CreateHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct);
        host.Sink.Clear();

        Assert.True((await host.Memberships.RemoveAsync(Jane, Acme, By, Ct)).IsSuccess);

        Assert.Empty(host.Sink.Last(RightsSinkCallKind.ReplaceMemberships)!.Values);
        Assert.Empty(host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
        Assert.Contains(host.Events.Events, e => e is MembershipChangedEvent { IsMember: false });
    }

    [Fact]
    public async Task Without_a_sink_the_registry_still_works_and_warns_once()
    {
        using var host = await CreateHost(withSink: false).InstalledAsync(Ct);

        Assert.True((await host.Memberships.AddAsync(Jane, Acme, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);

        Assert.Equal(["reader"], Assert.Single(await host.Memberships.ListByUserAsync(Jane, Ct)).Roles);
    }
}

/// <summary>The scenarios on the in-memory stores.</summary>
public sealed class InMemoryProjectionTests : ProjectionTestsBase
{
    private protected override TestHost CreateHost(bool withSink = true) => new(withSink);
}
