using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>The catalogue from the declarations, and the roles over it.</summary>
public sealed class CatalogAndRolesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalogue_is_the_operations_the_modules_declare()
    {
        using var host = new TestHost();
        var recorded = await host.Catalog.RecordAsync([Inventory, Billing], Ct);

        Assert.Equal(5, recorded.Count);
        var listed = await host.Catalog.ListAsync(Ct);
        Assert.Equal(["billing", "billing", "inventory", "inventory", "inventory"], listed.Select(e => e.Module));
        Assert.Contains(new CatalogEntry("inventory", "items.purge", OperationKind.Delete), listed);
    }

    [Fact]
    public async Task Recording_again_updates_the_kind_and_removes_nothing()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory, Billing], Ct);
        var inventoryWithoutPurge = ModuleDeclaration.Compartmented<Fixtures.InventoryModuleAgain>()
            .WithOperations(ItemsRead, new Operation("items", "write", OperationKind.Delete));

        await host.Catalog.RecordAsync([inventoryWithoutPurge], Ct);

        var listed = await host.Catalog.ListAsync(Ct);
        Assert.Equal(5, listed.Count);
        Assert.Equal(OperationKind.Delete, listed.Single(e => e.Operation == "items.write").Kind);
        Assert.Contains(listed, e => e.Operation == "items.purge");
    }

    [Fact]
    public async Task Declared_roles_are_created_then_reapplied_without_touching_a_role_made_by_hand()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory, Billing, RegistryModule.Declaration], Ct);
        Assert.True((await host.Roles.DeclareAsync(DeclaredRoles, By, Ct)).IsSuccess);
        var custom = await host.Roles.CreateAsync("auditor", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Auditor", [ItemsRead.Identity]), By, Ct);
        Assert.True(custom.IsSuccess);

        // Someone edits the declared role by hand; the installation puts it back, and leaves the custom one alone.
        Assert.True((await host.Roles.UpdateAsync("reader", new RoleDefinition(RoleCategory.Business, "Reader (edited)", [ItemsRead.Identity]), By, Ct)).IsSuccess);
        Assert.True((await host.Roles.DeclareAsync(DeclaredRoles, By, Ct)).IsSuccess);

        var reader = (await host.Roles.GetAsync("reader", Ct)).Value;
        Assert.True(reader.Declared);
        Assert.Equal("Reader", reader.DisplayName);
        Assert.Equal(["invoices.read", "items.read"], reader.Operations);
        var auditor = (await host.Roles.GetAsync("auditor", Ct)).Value;
        Assert.False(auditor.Declared);
        Assert.Equal(["items.read"], auditor.Operations);
        Assert.Equal(4, (await host.Roles.ListAsync(cancellationToken: Ct)).Count);
        Assert.Single(await host.Roles.ListAsync(RoleScope.Platform, Ct));
    }

    [Fact]
    public async Task A_declaration_cannot_take_over_a_role_made_by_hand()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory], Ct);
        Assert.True((await host.Roles.CreateAsync("reader", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Reader", [ItemsRead.Identity]), By, Ct)).IsSuccess);

        var declared = await host.Roles.DeclareAsync([new RoleDeclaration("reader", RoleScope.Compartment, RoleCategory.Business, "Reader", [ItemsRead])], By, Ct);

        Assert.Equal(RegistryErrors.RoleNotDeclaredCode, declared.Error!.Code);
    }

    [Fact]
    public async Task An_operation_outside_the_catalogue_is_refused()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory], Ct);

        var created = await host.Roles.CreateAsync("accountant", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Accountant", [InvoicesIssue.Identity]), By, Ct);
        var declared = await host.Roles.DeclareAsync([Accountant], By, Ct);

        Assert.Equal(RegistryErrors.UnknownOperationCode, created.Error!.Code);
        Assert.Equal(RegistryErrors.UnknownOperationCode, declared.Error!.Code);
        Assert.Equal(RegistryErrors.RoleNameInvalidCode, (await host.Roles.CreateAsync("Chef-Atelier", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "x", []), By, Ct)).Error!.Code);
    }

    [Fact]
    public async Task A_declared_role_and_a_held_role_are_not_deleted()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("auditor", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Auditor", [ItemsRead.Identity]), By, Ct)).IsSuccess);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "auditor", By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.RoleDeclaredCode, (await host.Roles.DeleteAsync("reader", By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleInUseCode, (await host.Roles.DeleteAsync("auditor", By, Ct)).Error!.Code);

        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Acme, "auditor", By, Ct)).IsSuccess);
        Assert.True((await host.Roles.DeleteAsync("auditor", By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, (await host.Roles.GetAsync("auditor", Ct)).Error!.Code);
        Assert.Contains(host.Events.Events, e => e is RoleChangedEvent { Role: "auditor", Exists: false });
    }

    [Fact]
    public async Task A_role_carries_its_category_and_the_roles_it_lets_its_holder_assign()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        var manager = await host.Roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct);
        var clerk = await host.Roles.CreateAsync("clerk", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Business, "Clerk", [ItemsRead.Identity]), By, Ct);
        var ops = await host.Roles.CreateAsync("ops", RoleScope.Platform,
            new RoleDefinition(RoleCategory.Administration, "Operations", [ItemsPurge.Identity]), By, Ct);
        Assert.True(manager.IsSuccess && clerk.IsSuccess && ops.IsSuccess);

        var roles = (await host.Roles.ListAsync(cancellationToken: Ct)).ToDictionary(role => role.Name);
        Assert.Equal(RoleCategory.Administration, roles["access_manager"].Category);
        Assert.Equal(RoleCategory.Business, roles["clerk"].Category);

        // The selectors cover the roles to come; a business selector, never an administration role.
        var business = roles["access_manager"].Assignable;
        Assert.True(business.Covers(roles["reader"]) && business.Covers(roles["clerk"]));
        Assert.False(business.Covers(roles["access_manager"]) || business.Covers(roles["ops"]));
        var everything = roles["super_admin"].Assignable;
        Assert.True(roles.Values.All(everything.Covers));
        var named = AssignableRoles.Of("clerk");
        Assert.True(named.Covers(roles["clerk"]));
        Assert.False(named.Covers(roles["reader"]));
    }

    [Fact]
    public async Task A_compartment_role_never_lets_its_holder_assign_a_platform_role_and_names_only_roles_that_exist()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        RoleDefinition Assigning(AssignableRoles assignable) => new(RoleCategory.Administration, "Manager", [], assignable);

        Assert.Equal(RegistryErrors.RoleAssignsPlatformCode, (await host.Roles.CreateAsync("manager", RoleScope.Compartment,
            Assigning(AssignableRoles.Every(CompartmentRoleSelector.None, allPlatform: true)), By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleAssignsPlatformCode, (await host.Roles.CreateAsync("manager", RoleScope.Compartment,
            Assigning(AssignableRoles.Of("super_admin")), By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, (await host.Roles.CreateAsync("manager", RoleScope.Compartment,
            Assigning(AssignableRoles.Of("nobody")), By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleAssignsPlatformCode, (await host.Roles.DeclareAsync(
            [new RoleDeclaration("manager", RoleScope.Compartment, RoleCategory.Administration, "Manager", [], AssignableRoles.Of("super_admin"))], By, Ct)).Error!.Code);

        // A platform role may let its holder assign compartment roles: an operator acts on the compartments.
        Assert.True((await host.Roles.CreateAsync("support", RoleScope.Platform,
            Assigning(new AssignableRoles(CompartmentRoleSelector.All, false, ["super_admin"])), By, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Nobody_changes_the_definition_of_a_role_they_hold()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        await host.Memberships.AddAsync(Jane, Acme, By, Ct);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.True((await host.Operators.AssignAsync(John, "super_admin", By, Ct)).IsSuccess);
        var wider = new RoleDefinition(RoleCategory.Business, "Reader", [ItemsRead.Identity, ItemsWrite.Identity]);
        var platform = new RoleDefinition(RoleCategory.Administration, "Platform administrator", [ItemsPurge.Identity], AssignableRoles.Every(CompartmentRoleSelector.All, allPlatform: true));

        Assert.Equal(RegistryErrors.RoleHeldByAuthorCode, (await host.Roles.UpdateAsync("reader", wider, Actor.Parse(Jane), Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleHeldByAuthorCode, (await host.Roles.UpdateAsync("super_admin", platform, Actor.Parse(John), Ct)).Error!.Code);
        Assert.Equal(["invoices.read", "items.read"], (await host.Roles.GetAsync("reader", Ct)).Value.Operations.Order());

        // Someone who does not hold it may; the installation, which holds nothing, re-applies a declared role.
        Assert.True((await host.Roles.UpdateAsync("reader", wider, Actor.Parse(John), Ct)).IsSuccess);
        Assert.True((await host.Roles.DeclareAsync(DeclaredRoles, By, Ct)).IsSuccess);
        Assert.Equal(["invoices.read", "items.read"], (await host.Roles.GetAsync("reader", Ct)).Value.Operations.Order());
    }

    [Fact]
    public async Task Declarations_name_one_another_in_any_order_and_a_role_named_in_a_list_is_not_deleted()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory], Ct);

        // The manager names the clerk, declared after it; the clerk names itself.
        Assert.True((await host.Roles.DeclareAsync(
        [
            new RoleDeclaration("manager", RoleScope.Compartment, RoleCategory.Administration, "Manager", [ItemsRead], AssignableRoles.Of("clerk", "manager")),
            new RoleDeclaration("clerk", RoleScope.Compartment, RoleCategory.Business, "Clerk", [ItemsRead], AssignableRoles.Of("clerk")),
        ], By, Ct)).IsSuccess);
        Assert.Equal(["clerk", "manager"], (await host.Roles.GetAsync("manager", Ct)).Value.Assignable.Roles.Order());

        Assert.True((await host.Roles.CreateAsync("temp", RoleScope.Compartment, new RoleDefinition(RoleCategory.Business, "Temporary", [ItemsRead.Identity]), By, Ct)).IsSuccess);
        Assert.True((await host.Roles.CreateAsync("lead", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Lead", [], AssignableRoles.Of("temp")), By, Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.RoleNamedAssignableCode, (await host.Roles.DeleteAsync("temp", By, Ct)).Error!.Code);
        Assert.True((await host.Roles.DeleteAsync("lead", By, Ct)).IsSuccess);
        Assert.True((await host.Roles.DeleteAsync("temp", By, Ct)).IsSuccess);
    }
}
