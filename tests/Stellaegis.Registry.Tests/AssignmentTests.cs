using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// Who may assign which role (I3, I7): assigning is a right of its own, bounded by the assignable lists of the roles
/// the author holds; nobody changes their own memberships or roles; unassigning and removing follow the same rule.
/// </summary>
public sealed class AssignmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Actor AsJohn = Actor.Parse(John);

    /// <summary>
    /// Acme with John its manager of the accesses — an administration role that assigns the business roles —, and Jane
    /// a member; the installation seats them.
    /// </summary>
    private static async Task<TestHost> AcmeAsync()
    {
        var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        Assert.True((await host.Compartments.CreateAsync(Globex, "Globex", By, Ct)).IsSuccess);
        foreach (var subject in new[] { John, Jane })
            Assert.True((await host.Memberships.AddAsync(subject, Acme, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AddAsync(Jane, Globex, By, Ct)).IsSuccess);
        return host;
    }

    [Fact]
    public async Task A_manager_of_the_accesses_assigns_the_business_roles_of_their_compartment_and_nothing_else()
    {
        using var host = await AcmeAsync();

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", AsJohn, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "accountant", AsJohn, Ct)).IsSuccess);   // without holding it
        var membership = (await host.Memberships.ListByUserAsync(Jane, Ct)).Single(entry => entry.Compartment == Acme);
        Assert.Equal(["accountant", "reader"], membership.Roles.Order());

        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Memberships.AssignRoleAsync(Jane, Acme, "access_manager", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Memberships.AssignRoleAsync(Jane, Globex, "reader", AsJohn, Ct)).Error!.Code);   // not his compartment
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Operators.AssignAsync(Jane, "super_admin", AsJohn, Ct)).Error!.Code);
    }

    [Fact]
    public async Task An_operator_assigns_by_the_lists_of_their_platform_roles_in_every_compartment()
    {
        using var host = await AcmeAsync();
        Assert.True((await host.Operators.AssignAsync(John, "super_admin", By, Ct)).IsSuccess);

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Globex, "reader", AsJohn, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "access_manager", AsJohn, Ct)).IsSuccess);
        Assert.True((await host.Operators.AssignAsync(Jane, "super_admin", AsJohn, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Nobody_changes_their_own_memberships_or_roles_in_either_direction()
    {
        using var host = await AcmeAsync();
        Assert.True((await host.Operators.AssignAsync(John, "super_admin", By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.AssignRoleAsync(John, Acme, "reader", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.UnassignRoleAsync(John, Acme, "access_manager", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.RemoveAsync(John, Acme, AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Memberships.AddAsync(John, Globex, AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.OwnRolesCode, (await host.Operators.UnassignAsync(John, "super_admin", AsJohn, Ct)).Error!.Code);
        Assert.Equal(["super_admin"], await host.Operators.ListRolesAsync(John, Ct));
    }

    [Fact]
    public async Task Unassigning_a_role_or_removing_a_member_takes_the_same_right_as_assigning_what_they_hold()
    {
        using var host = await AcmeAsync();
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "access_manager", By, Ct)).IsSuccess);

        // Jane is an administrator of the accesses too: John may take her business role, not her administration one,
        // and not remove her, which would take both.
        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Acme, "reader", AsJohn, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Memberships.UnassignRoleAsync(Jane, Acme, "access_manager", AsJohn, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Memberships.RemoveAsync(Jane, Acme, AsJohn, Ct)).Error!.Code);
        Assert.Contains(Acme, (await host.Memberships.ListByUserAsync(Jane, Ct)).Select(entry => entry.Compartment));

        // Once she holds only business roles, he may.
        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "accountant", AsJohn, Ct)).IsSuccess);
        Assert.True((await host.Memberships.RemoveAsync(Jane, Acme, AsJohn, Ct)).IsSuccess);
        Assert.DoesNotContain(Acme, (await host.Memberships.ListByUserAsync(Jane, Ct)).Select(entry => entry.Compartment));
    }

    [Fact]
    public async Task A_client_holds_no_role_and_assigns_none_while_a_job_is_bounded_by_nothing()
    {
        using var host = await AcmeAsync();
        var client = Actor.Parse("c_acme_reports");

        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", client, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, (await host.Operators.AssignAsync(Jane, "super_admin", client, Ct)).Error!.Code);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "access_manager", Actor.Job("install"), Ct)).IsSuccess);
        Assert.True((await host.Operators.AssignAsync(John, "super_admin", Actor.Job("install"), Ct)).IsSuccess);
    }
}
