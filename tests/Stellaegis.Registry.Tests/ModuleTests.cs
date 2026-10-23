using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>What the module declares, how it registers, and what the directory of users holds.</summary>
public sealed class ModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_module_has_no_compartment_and_thirteen_operations()
    {
        Assert.Equal("registry", RegistryModule.Name);
        Assert.Equal(ModuleCompartments.None, RegistryModule.Declaration.Compartments);
        Assert.Equal(13, RegistryModule.Declaration.Operations.Count);
        Assert.Contains(RegistryOperations.InvitationsWrite, RegistryModule.Declaration.Operations);
        Assert.Contains(RegistryOperations.MembershipsWrite, RegistryModule.Declaration.Operations);
        Assert.Contains(RegistryOperations.OwnershipWrite, RegistryModule.Declaration.Operations);
        Assert.All(RegistryModule.Declaration.Operations, o => Assert.True(NameRules.IsIdentifier(o.Target) && NameRules.IsIdentifier(o.Name)));
    }

    [Fact]
    public void Registering_twice_declares_the_module_once()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStellaegisRegistryInMemory().AddStellaegisRegistry().AddStellaegisRightsSink<RecordingRightsSink>();
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetStellaegisModules(), m => m.Name == "registry");
        using var scope = provider.CreateScope();
        Assert.IsType<RecordingRightsSink>(scope.ServiceProvider.GetRequiredService<IRightsSink>());
    }

    [Fact]
    public async Task A_user_is_recorded_from_any_guid_format_and_updated_in_place()
    {
        using var host = new TestHost();
        var hyphenated = Guid.Parse(Jane).ToString("D");

        var first = await host.Users.RecordAsync(hyphenated, "jane@acme.test", "Jane", By, Ct);
        var again = await host.Users.RecordAsync(Jane, "jane.admin@acme.test", "Jane Admin", By, Ct);

        Assert.Equal(Jane, first.Value.Subject);
        Assert.Equal(first.Value.Audit.Created, again.Value.Audit.Created);
        Assert.Equal(By, again.Value.Audit.Modified.By);
        Assert.Equal("Jane Admin", (await host.Users.GetAsync(hyphenated, Ct)).Value.DisplayName);
        Assert.Single(await host.Users.ListAsync(Ct));
        Assert.Throws<ArgumentException>(() => RegistryRules.NormalizeSubject("c_acme_robot"));
        Assert.Equal(RegistryErrors.UserNotFoundCode, (await host.Users.DeactivateAsync(John, By, Ct)).Error!.Code);
    }

    [Fact]
    public async Task A_compartment_is_renamed_and_listed_with_the_terminated_ones()
    {
        using var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Compartments.CreateAsync(Globex, "Globex", By, Ct)).IsSuccess);

        Assert.Equal("Globex Corp.", (await host.Compartments.RenameAsync(Globex, "Globex Corp.", By, Ct)).Value.DisplayName);
        Assert.True((await host.Compartments.TerminateAsync(Globex, By, Ct)).IsSuccess);

        Assert.Equal([Acme, Globex], (await host.Compartments.ListAsync(Ct)).Select(c => c.Compartment));
        Assert.Equal(RegistryErrors.CompartmentTerminatedCode, (await host.Compartments.RenameAsync(Globex, "x", By, Ct)).Error!.Code);
        Assert.Equal(RegistryErrors.CompartmentNotFoundCode, (await host.Compartments.GetAsync(Compartment.Parse("nobody"), Ct)).Error!.Code);
    }
}
