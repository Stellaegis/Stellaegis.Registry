using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Primitives.Hosting;
using Stellaegis.Registry.AspNetCore;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>The administration routes, under the operations a host grants, each change read at the sink.</summary>
public sealed class AdminEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Everything = RegistryOperations.All.Select(operation => operation.ToString()).ToArray();

    private static readonly string[] Reads = RegistryOperations.All.Where(operation => operation.Kind == OperationKind.Read).Select(operation => operation.ToString()).ToArray();

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map, ApplicationFrame? frame = null, Action<WebApplicationBuilder>? configure = null, bool install = true)
    {
        var builder = WebTestHost.Builder(frame).WithHeaderAuthentication();
        configure?.Invoke(builder);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);
        await app.StartAsync(Ct);
        if (install)
            await app.InstallAsync(Ct);
        return app;
    }

    private static void MapPerOperation(WebApplication app)
        => app.MapGroup("/api/admin").MapRegistryAdminEndpoints().RequireAuthorizationPerOperation(operation => operation.ToString());

    [Fact]
    public async Task An_operator_holding_the_operations_runs_the_life_of_a_compartment()
    {
        await using var app = await StartAsync(MapPerOperation);
        var client = app.ClientAs("operator", Everything);

        var created = await client.PostAsJsonAsync("/api/admin/registry/compartments", new CreateCompartmentRequest("globex", "Globex Corp"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var globex = (await created.Content.ReadFromJsonAsync<CompartmentSummary>(Ct))!;
        Assert.Equal("globex", globex.Compartment);
        Assert.Equal("active", globex.Status);
        Assert.Equal("operator", globex.CreatedBy);   // the actor of the request, kept on the row

        var duplicate = await client.PostAsJsonAsync("/api/admin/registry/compartments", new CreateCompartmentRequest("globex", "Again"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(RegistryErrors.CompartmentExistsCode, await duplicate.CodeOfAsync(Ct));

        var malformed = await client.PostAsJsonAsync("/api/admin/registry/compartments", new CreateCompartmentRequest("Globex-", "Bad"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Registry.Invalid", await malformed.CodeOfAsync(Ct));

        var listed = await client.GetFromJsonAsync<CompartmentSummary[]>("/api/admin/registry/compartments", Ct);
        Assert.Equal(["acme", "globex"], listed!.Select(c => c.Compartment).Order());

        var renamed = await client.PutAsJsonAsync("/api/admin/registry/compartments/globex", new RenameCompartmentRequest("Globex Corporation"), Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Globex Corporation", (await client.GetFromJsonAsync<CompartmentSummary>("/api/admin/registry/compartments/globex", Ct))!.DisplayName);

        var suspended = await client.PostAsync("/api/admin/registry/compartments/globex/suspend", null, Ct);
        Assert.Equal("suspended", (await suspended.Content.ReadFromJsonAsync<CompartmentSummary>(Ct))!.Status);
        var resumed = await client.PostAsync("/api/admin/registry/compartments/globex/resume", null, Ct);
        Assert.Equal("active", (await resumed.Content.ReadFromJsonAsync<CompartmentSummary>(Ct))!.Status);
        var terminated = await client.PostAsync("/api/admin/registry/compartments/globex/terminate", null, Ct);
        Assert.Equal("terminated", (await terminated.Content.ReadFromJsonAsync<CompartmentSummary>(Ct))!.Status);
        Assert.Equal(RightsSinkCallKind.RemoveCompartment, app.Sink().Last(RightsSinkCallKind.RemoveCompartment)!.Kind);

        var again = await client.PostAsync("/api/admin/registry/compartments/globex/resume", null, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, again.StatusCode);
        Assert.Equal(RegistryErrors.CompartmentTerminatedCode, await again.CodeOfAsync(Ct));

        var unknown = await client.GetAsync("/api/admin/registry/compartments/nowhere", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(RegistryErrors.CompartmentNotFoundCode, await unknown.CodeOfAsync(Ct));
    }

    [Fact]
    public async Task The_users_are_read_and_deactivated_never_created_here()
    {
        await using var app = await StartAsync(MapPerOperation);
        var client = app.ClientAs("operator", Everything);

        var listed = await client.GetFromJsonAsync<UserSummary[]>("/api/admin/registry/users", Ct);
        Assert.Equal(2, listed!.Length);
        var jane = await client.GetFromJsonAsync<UserSummary>($"/api/admin/registry/users/{Jane}", Ct);
        Assert.Equal("jane@acme.test", jane!.Email);
        Assert.Equal("active", jane.Status);

        var deactivated = await client.PostAsync($"/api/admin/registry/users/{Jane}/deactivate", null, Ct);
        Assert.Equal("deactivated", (await deactivated.Content.ReadFromJsonAsync<UserSummary>(Ct))!.Status);
        Assert.Equal(Jane, app.Sink().Last(RightsSinkCallKind.RemoveSubject)!.Subject);
        var reactivated = await client.PostAsync($"/api/admin/registry/users/{Jane}/reactivate", null, Ct);
        Assert.Equal("active", (await reactivated.Content.ReadFromJsonAsync<UserSummary>(Ct))!.Status);

        var unknown = await client.GetAsync($"/api/admin/registry/users/{Guid.NewGuid():N}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(RegistryErrors.UserNotFoundCode, await unknown.CodeOfAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/registry/users/not-a-guid", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/admin/registry/users", new { }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_catalogue_and_the_roles_are_administered_a_declared_role_never_deleted()
    {
        await using var app = await StartAsync(MapPerOperation);
        var client = app.ClientAs("operator", Everything);

        var catalog = await client.GetFromJsonAsync<CatalogEntrySummary[]>("/api/admin/registry/catalog", Ct);
        Assert.Contains(catalog!, entry => entry.Operation == ItemsRead.Identity && entry.Kind == "read" && entry.Module == "inventory");
        var inventory = await client.GetFromJsonAsync<CatalogEntrySummary[]>("/api/admin/registry/catalog/inventory", Ct);
        Assert.Equal(3, inventory!.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/registry/catalog/Not-A-Module", Ct)).StatusCode);

        var roles = await client.GetFromJsonAsync<RoleSummary[]>("/api/admin/registry/roles?scope=platform", Ct);
        var superAdmin = Assert.Single(roles!);
        Assert.Equal("super_admin", superAdmin.Name);
        Assert.True(superAdmin.Declared);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/registry/roles?scope=nowhere", Ct)).StatusCode);

        var created = await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("picker", "compartment", "business", "Picker", [ItemsRead.Identity, ItemsWrite.Identity]), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var picker = (await created.Content.ReadFromJsonAsync<RoleSummary>(Ct))!;
        Assert.False(picker.Declared);
        Assert.Equal("compartment", picker.Scope);

        var unknownOperation = await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("ghost", "compartment", "business", "Ghost", ["ghosts.haunt"]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknownOperation.StatusCode);
        Assert.Equal(RegistryErrors.UnknownOperationCode, await unknownOperation.CodeOfAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("Bad-Name", "compartment", "business", "Bad", []), Ct)).StatusCode);

        var updated = await client.PutAsJsonAsync("/api/admin/registry/roles/picker", new UpdateRoleRequest("business", "Order picker", [ItemsRead.Identity]), Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal([ItemsRead.Identity], (await client.GetFromJsonAsync<RoleSummary>("/api/admin/registry/roles/picker", Ct))!.Operations);

        // The category, the assignable list and the second factor go through the routes; a platform role always requires one.
        var manager = await client.PostAsJsonAsync("/api/admin/registry/roles",
            new CreateRoleRequest("pick_manager", "compartment", "administration", "Picking manager", [], new AssignableRolesBody("business", false, ["picker"]), RequiresSecondFactor: true), Ct);
        Assert.Equal(HttpStatusCode.Created, manager.StatusCode);
        var managed = (await manager.Content.ReadFromJsonAsync<RoleSummary>(Ct))!;
        Assert.Equal(("administration", "business", false, true), (managed.Category, managed.Assignable.Compartment, managed.Assignable.Platform, managed.RequiresSecondFactor));
        Assert.Equal(["picker"], managed.Assignable.Roles);
        var ops = (await (await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("ops", "platform", "administration", "Operations", []), Ct)).Content.ReadFromJsonAsync<RoleSummary>(Ct))!;
        Assert.True(ops.RequiresSecondFactor);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("odd", "compartment", "boss", "Odd", []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("odd", "compartment", "business", "Odd", [], new AssignableRolesBody("some", false, [])), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("odd", "compartment", "1", "Odd", []), Ct)).StatusCode);
        var crossing = await client.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("odd", "compartment", "administration", "Odd", [], new AssignableRolesBody("none", true, [])), Ct);
        Assert.Equal(RegistryErrors.RoleAssignsPlatformCode, await crossing.CodeOfAsync(Ct));
        var named = await client.DeleteAsync("/api/admin/registry/roles/picker", Ct);
        Assert.Equal(RegistryErrors.RoleNamedAssignableCode, await named.CodeOfAsync(Ct));   // named in the list of pick_manager
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/admin/registry/roles/pick_manager", Ct)).StatusCode);

        var declared = await client.DeleteAsync("/api/admin/registry/roles/super_admin", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, declared.StatusCode);
        Assert.Equal(RegistryErrors.RoleDeclaredCode, await declared.CodeOfAsync(Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/admin/registry/roles/picker", Ct)).StatusCode);
        var gone = await client.GetAsync("/api/admin/registry/roles/picker", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(RegistryErrors.RoleNotFoundCode, await gone.CodeOfAsync(Ct));
    }

    [Fact]
    public async Task A_role_assigned_by_the_route_is_projected_to_the_sink()
    {
        await using var app = await StartAsync(MapPerOperation);
        var client = app.ClientAs("operator", Everything);
        var sink = app.Sink();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/admin/registry/memberships/acme/{Jane}", null, Ct)).StatusCode);
        Assert.Equal(new RightsSinkCall(RightsSinkCallKind.ReplaceMemberships, Jane, null, ["acme"]), sink.Last(RightsSinkCallKind.ReplaceMemberships));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/admin/registry/memberships/acme/{Jane}/roles/accountant", null, Ct)).StatusCode);
        var grants = sink.Last(RightsSinkCallKind.ReplaceUserGrants)!;
        Assert.Equal(Jane, grants.Subject);
        Assert.Equal(Acme, grants.Compartment);
        Assert.Equal([InvoicesIssue.Identity, InvoicesRead.Identity], grants.Values.Order());

        var members = await client.GetFromJsonAsync<MemberSummary[]>("/api/admin/registry/memberships/compartments/acme", Ct);
        var jane = Assert.Single(members!);
        Assert.Equal(["accountant"], jane.Roles);
        Assert.Equal("operator", jane.GrantedBy);
        var mine = await client.GetFromJsonAsync<MemberSummary[]>($"/api/admin/registry/memberships/users/{Jane}", Ct);
        Assert.Equal("acme", Assert.Single(mine!).Compartment);

        var notMember = await client.PutAsync($"/api/admin/registry/memberships/acme/{John}/roles/accountant", null, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, notMember.StatusCode);
        Assert.Equal(RegistryErrors.NotMemberCode, await notMember.CodeOfAsync(Ct));
        var wrongScope = await client.PutAsync($"/api/admin/registry/memberships/acme/{Jane}/roles/super_admin", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongScope.StatusCode);
        Assert.Equal(RegistryErrors.RoleScopeMismatchCode, await wrongScope.CodeOfAsync(Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/registry/memberships/acme/{Jane}/roles/accountant", Ct)).StatusCode);
        Assert.Empty(sink.Last(RightsSinkCallKind.ReplaceUserGrants)!.Values);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/registry/memberships/acme/{Jane}", Ct)).StatusCode);
        Assert.Empty(sink.Last(RightsSinkCallKind.ReplaceMemberships)!.Values);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/admin/registry/operators/{Jane}/roles/super_admin", null, Ct)).StatusCode);
        Assert.Equal([RegistryOperations.CompartmentsWrite.Identity, ItemsPurge.Identity], sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!.Values.Order());
        var operators = await client.GetFromJsonAsync<OperatorSummary[]>("/api/admin/registry/operators", Ct);
        Assert.Equal(["super_admin"], Assert.Single(operators!).Roles);
        Assert.Equal(["super_admin"], (await client.GetFromJsonAsync<OperatorSummary>($"/api/admin/registry/operators/{Jane}", Ct))!.Roles);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/registry/operators/{Jane}/roles/super_admin", Ct)).StatusCode);
        Assert.Empty(sink.Last(RightsSinkCallKind.ReplaceAdminGrants)!.Values);
    }

    [Fact]
    public async Task A_caller_with_the_reads_only_is_refused_the_writes_and_an_unauthenticated_one_challenged()
    {
        await using var app = await StartAsync(MapPerOperation);
        var reader = app.ClientAs("reader", Reads);

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/compartments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/catalog", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/roles", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/memberships/compartments/acme", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/registry/operators", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/admin/registry/compartments", new CreateCompartmentRequest("globex", "Globex"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsync($"/api/admin/registry/users/{Jane}/deactivate", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/admin/registry/roles", new CreateRoleRequest("picker", "compartment", "business", "Picker", []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsync($"/api/admin/registry/memberships/acme/{Jane}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsync($"/api/admin/registry/operators/{Jane}/roles/super_admin", null, Ct)).StatusCode);
        Assert.Empty(app.Sink().Calls);

        var nobody = app.ClientAs(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await nobody.GetAsync("/api/admin/registry/compartments", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_routes_map_in_an_application_with_compartments_without_reading_its_request_context()
    {
        await using var app = await StartAsync(MapPerOperation,
            frame: ApplicationFrame.SingleCompartment("acme"),
            configure: builder => builder.Services.AddStellaegisRequestContext());

        var listed = await app.ClientAs("operator", Everything).GetFromJsonAsync<CompartmentSummary[]>("/api/admin/registry/compartments", Ct);

        Assert.Equal("acme", Assert.Single(listed!).Compartment);
    }

    [Fact]
    public async Task Routes_left_without_authorization_fail_the_startup()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartAsync(app => app.MapGroup("/api/admin").MapRegistryAdminEndpoints(), install: false));

        Assert.Contains("compartments.read", exception.Message);
        Assert.Contains("declare no authorization", exception.Message);
    }

    [Fact]
    public async Task The_routes_need_the_startup_check_their_registration_brings()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddStellaegisFrame(ApplicationFrame.WithoutCompartments);
        builder.Services.AddStellaegisRegistryInMemory();
        var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.MapGroup("/api/admin").MapRegistryAdminEndpoints());

        Assert.Contains("AddStellaegisStartupCheck", exception.Message);
    }
}
