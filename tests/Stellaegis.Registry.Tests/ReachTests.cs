using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Registry.AspNetCore;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>A gesture on a person — block, reset, deactivate — reaches whom the actor could have assigned every role.</summary>
public sealed class ReachTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Nina = Guid.Parse("5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293").ToString("N");
    private static readonly string Paul = Guid.Parse("6c7d8e9f-0a1b-4c2d-8e3f-5a6b7c8d9e0f").ToString("N");

    /// <summary>
    /// Acme: John its manager of the accesses — the business roles assignable —, Jane a reader. Nina an operator of the
    /// platform, super_admin. Paul holds no role, and owns Globex.
    /// </summary>
    private static async Task<TestHost> AcmeAsync()
    {
        var host = await new TestHost().InstalledAsync(Ct);
        Assert.True((await host.Roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [RegistryOperations.MembershipsGrant.Identity], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        foreach (var subject in new[] { John, Jane })
            Assert.True((await host.Memberships.AddAsync(subject, Acme, By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await host.Memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.True((await host.Users.RecordAsync(Nina, "nina@acme.test", "Nina", By, Ct)).IsSuccess);
        Assert.True((await host.Operators.AssignAsync(Nina, "super_admin", By, Ct)).IsSuccess);
        Assert.True((await host.Users.RecordAsync(Paul, "paul@globex.test", "Paul", By, Ct)).IsSuccess);
        return host;
    }

    private static async Task<string?> RefusalAsync(TestHost host, string subject, string actor)
        => (await host.Users.ReachesAsync(subject, Actor.Parse(actor), Ct)).Error?.Code;

    [Fact]
    public async Task A_person_is_reached_by_who_could_have_assigned_them_every_role_they_hold()
    {
        using var host = await AcmeAsync();

        Assert.Null(await RefusalAsync(host, Jane, John));   // a reader: within the manager's list
        Assert.Equal(RegistryErrors.RoleNotAssignableCode, await RefusalAsync(host, Nina, John));   // the administrator: beyond it
        Assert.Null(await RefusalAsync(host, John, Nina));   // the administrator's lists cover every role
        Assert.Null(await RefusalAsync(host, Jane, Nina));
    }

    [Fact]
    public async Task Nobody_reaches_themselves_nor_an_owner_and_a_person_without_roles_has_nothing_to_protect()
    {
        using var host = await AcmeAsync();
        Assert.Null(await RefusalAsync(host, Paul, John));   // no role: reached

        Assert.True((await host.Compartments.CreateAsync(Globex, "Globex", Paul, By, Ct)).IsSuccess);

        Assert.Equal(RegistryErrors.OwnerUntouchableCode, await RefusalAsync(host, Paul, Nina));   // an owner: even by the platform
        Assert.Equal(RegistryErrors.OwnRolesCode, await RefusalAsync(host, John, John));
        Assert.Equal(RegistryErrors.OwnRolesCode, await RefusalAsync(host, Nina, Nina));
        Assert.True((await host.Users.ReachesAsync(Paul, Actor.Job("install"), Ct)).IsSuccess);   // the installation reaches everyone
    }

    // ── The routes ─────────────────────────────────────────────────────────────

    private static async Task<WebApplication> StartAsync(Action<IServiceCollection>? services = null)
    {
        var builder = WebTestHost.Builder().WithHeaderAuthentication();
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/admin").MapRegistryAdminEndpoints().RequireAuthorizationPerOperation(operation => operation.ToString());
        await app.StartAsync(Ct);
        await app.InstallAsync(Ct);
        using var scope = app.Services.CreateScope();
        var operators = scope.ServiceProvider.GetRequiredService<IOperators>();
        var memberships = scope.ServiceProvider.GetRequiredService<IMemberships>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoles>();
        Assert.True((await roles.CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [RegistryOperations.DirectoryDeactivate.Identity], AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        foreach (var subject in new[] { John, Jane })
            Assert.True((await memberships.AddAsync(subject, Acme, By, Ct)).IsSuccess);
        Assert.True((await memberships.AssignRoleAsync(John, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await memberships.AssignRoleAsync(Jane, Acme, "reader", By, Ct)).IsSuccess);
        Assert.True((await scope.ServiceProvider.GetRequiredService<IUserDirectory>().RecordAsync(Nina, "nina@acme.test", "Nina", By, Ct)).IsSuccess);
        Assert.True((await operators.AssignAsync(Nina, "super_admin", By, Ct)).IsSuccess);
        return app;
    }

    [Fact]
    public async Task Deactivating_someone_is_decided_on_that_person()
    {
        await using var app = await StartAsync();
        var john = app.ClientAs(John, RegistryOperations.DirectoryDeactivate.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await john.PostAsync($"/api/admin/registry/users/{Nina}/deactivate", null, Ct)).StatusCode);   // the administrator
        Assert.Equal(HttpStatusCode.NotFound, (await john.PostAsync($"/api/admin/registry/users/{John}/deactivate", null, Ct)).StatusCode);   // himself
        Assert.Equal(HttpStatusCode.OK, (await john.PostAsync($"/api/admin/registry/users/{Jane}/deactivate", null, Ct)).StatusCode);         // a reader
    }

    /// <summary>A host's own rule that would let every person through.</summary>
    private sealed class Everyone : AuthorizationHandler<OperationRequirement, Subject>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationRequirement requirement, Subject resource)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_more_lenient_rule_of_the_host_does_not_outweigh_the_registrys_refusal()
    {
        await using var app = await StartAsync(services => services.AddSingleton<IAuthorizationHandler, Everyone>());
        var john = app.ClientAs(John, RegistryOperations.DirectoryDeactivate.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await john.PostAsync($"/api/admin/registry/users/{Nina}/deactivate", null, Ct)).StatusCode);
    }
}
