using Stellaegis.Primitives;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// The requirement of a second factor belongs to the elementary operation, as its module declares it and the
/// installation hardens it, whichever role grants it; in the shared space every operation requires one.
/// </summary>
public sealed class SecondFactorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class PayrollModule : IStellaegisModule
    {
        public static string Name => "payroll";
    }

    private static readonly Operation PayrollRead = new("payroll", "read", OperationKind.Read);
    private static readonly Operation PayrollPay = new("payroll", "pay", OperationKind.Write, requiresSecondFactor: true);

    private static async Task<TestHost> PayrollAsync()
    {
        var host = await new TestHost().InstalledAsync(Ct);
        await host.Catalog.RecordAsync([ModuleDeclaration.Compartmented<PayrollModule>().WithOperations(PayrollRead, PayrollPay)], Ct);
        Assert.True((await host.Roles.CreateAsync("payroll", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Business, "Payroll", [PayrollRead.Identity, PayrollPay.Identity, ItemsRead.Identity]), By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AddAsync(Jane, Acme, By, Ct)).IsSuccess);
        host.Sink.Clear();
        return host;
    }

    [Fact]
    public async Task A_compartment_grant_requires_a_second_factor_for_the_operations_declared_sensitive_whichever_role_grants_them()
    {
        using var host = await PayrollAsync();

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "payroll", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);   // items.read again, by another role

        var grants = host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!;
        Assert.Equal(["invoices.read", "items.read", "payroll.pay", "payroll.read"], grants.Values);
        Assert.Equal(["payroll.pay"], grants.SecondFactor);
        Assert.True((await host.Catalog.ListAsync(Ct)).Single(entry => entry.Operation == "payroll.pay").RequiresSecondFactor);
    }

    [Fact]
    public async Task The_installation_hardens_an_operation_never_weakens_one_and_nobody_else_touches_the_catalogue()
    {
        using var host = await PayrollAsync();
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "payroll", By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.CatalogInstallationOnlyCode, (await host.Catalog.RequireSecondFactorAsync(["payroll.read"], Actor.Parse(John), Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.UnknownOperationCode, (await host.Catalog.RequireSecondFactorAsync(["payroll.nothing"], By, Ct)).Error!.Code);
        Assert.True((await host.Catalog.RequireSecondFactorAsync(["payroll.read"], By, Ct)).IsSuccess);
        await host.Catalog.RecordAsync([ModuleDeclaration.Compartmented<PayrollModule>().WithOperations(PayrollRead, PayrollPay)], Ct);

        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.Equal(["payroll.pay", "payroll.read"], host.Sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.SecondFactor);
    }

    [Fact]
    public async Task Every_operation_of_the_shared_space_requires_a_second_factor()
    {
        using var host = await PayrollAsync();

        Assert.True((await host.Operators.AssignAsync(Jane, "super_admin", By, Ct)).IsSuccess);

        var grants = host.Sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!;
        Assert.Equal(grants.Values, grants.SecondFactor);
        Assert.NotEmpty(grants.Values);
    }
}
