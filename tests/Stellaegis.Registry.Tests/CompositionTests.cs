using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Identity;
using Stellaegis.Identity.AspNetCore;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Registry.AspNetCore;
using Stellaegis.Registry.Identity;
using static Stellaegis.Registry.Tests.Fixtures;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// Identity and the registry composed in one host, as an application does: the routes of the accounts decided on the
/// person by the registry, a reset link in hand approved by its acts, an invitation accepted through the accounts.
/// </summary>
public sealed class CompositionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record People(WebApplication App, string Fred, string John, string Jane, CapturingInvitationSender Sender);

    /// <summary>
    /// Acme: Fred an operator of the platform — super_admin —, John its manager of the accesses — the business roles
    /// assignable —, Jane a reader. Each an account of Identity, recorded in the directory under its subject.
    /// </summary>
    private static async Task<People> StartAsync()
    {
        var sender = new CapturingInvitationSender();
        var builder = WebTestHost.Builder().WithHeaderAuthentication();
        builder.Services.AddStellaegisIdentityInMemory(options =>
        {
            options.PasswordHasher.Pepper = "test-pepper";
            options.PasswordHasher.Iterations = 1000;
            options.Mfa.SecretKeys = new KeySlots { Keys = { [1] = "test-totp-key" } };
            options.LoginPolicy.MinResponseTime = TimeSpan.Zero;
            options.Links.HandDelivery = true;
            options.PasswordReset.MinimumInterval = TimeSpan.Zero;
        });
        builder.Services.AddStellaegisIdentityAdministration();
        builder.Services.AddStellaegisLocalBlockedUsersSource();
        builder.Services.AddStellaegisInMemoryIdentityMailer();
        builder.Services.AddStellaegisRegistryIdentity();
        builder.Services.AddSingleton<IInvitationSender>(sender);
        builder.Services.Configure<RegistryOptions>(options => options.Invitations.HandDelivery = true);
        builder.Services.AddAuthorization(options =>
        {
            foreach (var operation in IdentityOperations.All)
                options.AddPolicy(operation.ToString(), policy => policy.RequireClaim(WebTestHost.HeaderAuthentication.GrantClaim, operation.ToString()));
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/admin").MapIdentityAdminEndpoints().RequireAuthorizationPerOperation(operation => operation.ToString());   // decided on the person
        app.MapGroup("/api/admin").MapRegistryAdminEndpoints().RequireAuthorizationPerOperation(operation => operation.ToString());
        await app.StartAsync(Ct);

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<ICatalog>().RecordAsync([Inventory, Billing, RegistryModule.Declaration, IdentityModule.Declaration], Ct);
        Assert.True((await services.GetRequiredService<IRoles>().DeclareAsync(DeclaredRoles, By, Ct)).IsSuccess);
        Assert.True((await services.GetRequiredService<IRoles>().CreateAsync("access_manager", RoleScope.Compartment,
            new RoleDefinition(RoleCategory.Administration, "Access manager", [IdentityOperations.UsersBlock.Identity, RegistryOperations.InvitationsSend.Identity],
                AssignableRoles.Every(CompartmentRoleSelector.Business)), By, Ct)).IsSuccess);
        Assert.True((await services.GetRequiredService<ICompartments>().CreateAsync(Acme, "Acme SA", By, Ct)).IsSuccess);

        async Task<string> PersonAsync(string email, string name)
        {
            var created = (await services.GetRequiredService<AccountAdministrationService>().CreateAsync(email, name, TokenDelivery.Hand, By, Ct)).Value;
            Assert.True((await services.GetRequiredService<ActivationService>().ActivateAsync(created.ActivationToken, "Correct-Horse-9", Ct)).IsSuccess);
            var subject = created.UserUniqueId.ToString("N");
            Assert.True((await services.GetRequiredService<IUserDirectory>().RecordAsync(subject, email, name, Actor.Parse(subject), Ct)).IsSuccess);
            return subject;
        }
        var fred = await PersonAsync("fred@acme.test", "Fred");
        var john = await PersonAsync("john@acme.test", "John");
        var jane = await PersonAsync("jane@acme.test", "Jane");
        var memberships = services.GetRequiredService<IMemberships>();
        Assert.True((await services.GetRequiredService<IOperators>().AssignAsync(fred, "super_admin", By, Ct)).IsSuccess);
        foreach (var subject in new[] { john, jane })
            Assert.True((await memberships.AddAsync(subject, Acme, By, Ct)).IsSuccess);
        Assert.True((await memberships.AssignRoleAsync(john, Acme, "access_manager", By, Ct)).IsSuccess);
        Assert.True((await memberships.AssignRoleAsync(jane, Acme, "reader", By, Ct)).IsSuccess);
        return new People(app, fred, john, jane, sender);
    }

    [Fact]
    public async Task Who_manages_the_colleagues_blocks_a_reader_and_never_the_administrator()
    {
        var people = await StartAsync();
        await using var app = people.App;
        var john = app.ClientAs(people.John, IdentityOperations.UsersBlock.ToString());

        var refused = await john.PostAsync($"/api/admin/identity/users/{people.Fred}/block", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await john.PostAsync($"/api/admin/identity/users/{people.Jane}/block", null, Ct)).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthUserStore>().GetByUniqueIdAsync(Guid.Parse(people.Fred), Ct))!.IsEnabled);
    }

    [Fact]
    public async Task A_reset_link_in_hand_waits_for_a_second_person_of_the_registry()
    {
        var people = await StartAsync();
        await using var app = people.App;
        var fred = app.ClientAs(people.Fred, IdentityOperations.UsersRecover.ToString());

        var asked = await fred.PostAsJsonAsync($"/api/admin/identity/users/{people.Jane}/password-reset", new IssueLinkRequest("hand"), Ct);
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var approval = (await asked.Content.ReadFromJsonAsync<HandDeliveryPending>(Ct))!.Approval;
        Assert.Equal(HttpStatusCode.Forbidden, (await fred.PostAsJsonAsync($"/api/admin/identity/users/{people.Jane}/password-reset", new IssueLinkRequest("hand", approval), Ct)).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var validations = scope.ServiceProvider.GetRequiredService<IValidations>();
            Assert.Equal(PendingActKind.HandDelivery, (await validations.ListAsync(Ct)).Single(act => act.ActId == approval).Kind);
            Assert.True((await validations.ValidateAsync(approval, Actor.Parse(@"system:SATURN-fred"), ValidationChannel.Console, Ct)).IsSuccess);
        }

        var given = await fred.PostAsJsonAsync($"/api/admin/identity/users/{people.Jane}/password-reset", new IssueLinkRequest("hand", approval), Ct);
        Assert.Equal(HttpStatusCode.OK, given.StatusCode);
        Assert.NotNull((await given.Content.ReadFromJsonAsync<IssuedLink>(Ct))!.Link);
        Assert.Equal(HttpStatusCode.Forbidden, (await fred.PostAsJsonAsync($"/api/admin/identity/users/{people.Jane}/password-reset", new IssueLinkRequest("hand", approval), Ct)).StatusCode);   // spent
    }

    [Fact]
    public async Task An_invitation_of_the_registry_creates_and_admits_an_account_of_identity()
    {
        var people = await StartAsync();
        await using var app = people.App;
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.True((await services.GetRequiredService<IInvitations>().IssueAsync(Acme, "nina@acme.test", ["reader"], InvitationDelivery.Email, Actor.Parse(people.John), cancellationToken: Ct)).IsSuccess);
        var token = Assert.Single(people.Sender.Sent).Token;
        var invitations = services.GetRequiredService<InvitationService>();

        Assert.True(invitations.IsOpen);
        Assert.Equal(new InvitationOffer("nina@acme.test", SentByEmail: true), await invitations.FindAsync(token, Ct));
        var nina = (await invitations.CreateAccountAsync(token, "Nina", "Correct-Horse-9", Ct)).Value;
        Assert.True((await invitations.AcceptAsync(token, nina, Ct)).IsSuccess);

        var member = (await services.GetRequiredService<IMemberships>().ListByCompartmentAsync(Acme, Ct)).Single(m => m.Subject == nina.ToString("N"));
        Assert.Equal(["reader"], member.Roles);
        Assert.Equal(nina.ToString("N"), (await services.GetRequiredService<IUserDirectory>().GetAsync(nina.ToString("N"), Ct)).Value.Subject);
    }

    [Fact]
    public async Task An_invitation_handed_over_admits_the_account_created_once_its_address_is_proven()
    {
        var people = await StartAsync();
        await using var app = people.App;
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var issued = await services.GetRequiredService<IInvitations>().IssueAsync(Acme, "omar@acme.test", ["reader"], InvitationDelivery.Hand, Actor.Parse(people.John), cancellationToken: Ct);
        var token = issued.Value.Token!;
        Assert.Empty(people.Sender.Sent);
        var invitations = services.GetRequiredService<InvitationService>();
        Assert.Equal(new InvitationOffer("omar@acme.test", SentByEmail: false), await invitations.FindAsync(token, Ct));

        // The account is created; a link handed over proves nothing, so the acceptance waits — the invitation holds.
        var omar = (await invitations.CreateAccountAsync(token, "Omar", "Correct-Horse-9", Ct)).Value;
        Assert.Equal(IdentityErrors.EmailNotVerified, (await invitations.AcceptAsync(token, omar, Ct)).Error);
        Assert.DoesNotContain((await services.GetRequiredService<IMemberships>().ListByCompartmentAsync(Acme, Ct)), m => m.Subject == omar.ToString("N"));
        Assert.NotNull(await invitations.FindAsync(token, Ct));

        // The address proven from the account, the same link admits it.
        var account = services.GetRequiredService<AccountService>();
        var code = (await account.RequestEmailVerificationAsync(omar, cancellationToken: Ct)).Value;
        Assert.True((await account.ConfirmEmailVerificationAsync(omar, code, Ct)).IsSuccess);
        Assert.True((await invitations.AcceptAsync(token, omar, Ct)).IsSuccess);
        var member = (await services.GetRequiredService<IMemberships>().ListByCompartmentAsync(Acme, Ct)).Single(m => m.Subject == omar.ToString("N"));
        Assert.Equal(["reader"], member.Roles);
    }
}
