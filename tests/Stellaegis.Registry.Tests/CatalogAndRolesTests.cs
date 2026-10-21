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
        Assert.True((await host.Roles.DeclareAsync(DeclaredRoles, Ct)).IsSuccess);
        var custom = await host.Roles.CreateAsync("auditor", RoleScope.Compartment, "Auditor", [ItemsRead.Identity], Ct);
        Assert.True(custom.IsSuccess);

        // Someone edits the declared role by hand; the installation puts it back, and leaves the custom one alone.
        Assert.True((await host.Roles.UpdateAsync("reader", "Reader (edited)", [ItemsRead.Identity], Ct)).IsSuccess);
        Assert.True((await host.Roles.DeclareAsync(DeclaredRoles, Ct)).IsSuccess);

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
        Assert.True((await host.Roles.CreateAsync("reader", RoleScope.Compartment, "Reader", [ItemsRead.Identity], Ct)).IsSuccess);

        var declared = await host.Roles.DeclareAsync([new RoleDeclaration("reader", RoleScope.Compartment, "Reader", [ItemsRead])], Ct);

        Assert.Equal(RegistryErrors.RoleNotDeclaredCode, declared.Error!.Code);
    }

    [Fact]
    public async Task An_operation_outside_the_catalogue_is_refused()
    {
        using var host = new TestHost();
        await host.Catalog.RecordAsync([Inventory], Ct);

        var created = await host.Roles.CreateAsync("accountant", RoleScope.Compartment, "Accountant", [InvoicesIssue.Identity], Ct);
        var declared = await host.Roles.DeclareAsync([Accountant], Ct);

        Assert.Equal(RegistryErrors.UnknownOperationCode, created.Error!.Code);
        Assert.Equal(RegistryErrors.UnknownOperationCode, declared.Error!.Code);
        Assert.Equal(RegistryErrors.RoleNameInvalidCode, (await host.Roles.CreateAsync("Chef-Atelier", RoleScope.Compartment, "x", [], Ct)).Error!.Code);
    }

    [Fact]
    public async Task A_declared_role_and_a_held_role_are_not_deleted()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("auditor", RoleScope.Compartment, "Auditor", [ItemsRead.Identity], Ct)).IsSuccess);
        await host.Memberships.AddAsync(Jane, Acme, Ct);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "auditor", Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.RoleDeclaredCode, (await host.Roles.DeleteAsync("reader", Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.RoleInUseCode, (await host.Roles.DeleteAsync("auditor", Ct)).Error!.Code);

        Assert.True((await host.Memberships.UnassignRoleAsync(Jane, Acme, "auditor", Ct)).IsSuccess);
        Assert.True((await host.Roles.DeleteAsync("auditor", Ct)).IsSuccess);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, (await host.Roles.GetAsync("auditor", Ct)).Error!.Code);
        Assert.Contains(host.Events.Events, e => e is RoleChangedEvent { Role: "auditor", Exists: false });
    }
}
