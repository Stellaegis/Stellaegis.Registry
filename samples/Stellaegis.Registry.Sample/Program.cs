// The example host of Stellaegis.Registry — and the first assembly of the whole base of the family, in one process:
// an enterprise application, one compartment, the company itself. Its people have accounts (Stellaegis.Identity)
// and sign in on its pages; its issuer turns them into tokens for a native client (Stellaegis.Issuer); its rights
// are decided per operation (Stellaegis.Access); and what gives those rights their meaning — the catalogue, the
// roles, the memberships — is the registry, this repository. The host composes the four, with a module of its own.
//
//   dotnet run -- install --admin-email admin@acme.test --admin-password 'Correct-Horse-9'
//       creates the database and the application's role if missing, installs the four schemas under the
//       administration connection, records the catalogue of the operations the modules declare, re-applies the
//       declared roles, creates the compartment, creates the first administrator in Identity and records them in
//       the registry as the administrator of the platform and of the compartment — which the registry projects
//       into Access —, declares the public client of the desktop and provisions the credentials of the inventory.
//       Run again, it changes nothing.
//   dotnet run
//       verifies the four schemas under the application's role, then serves.
//
// Declare, install, grant, project, decide: the whole chain, in one Program.cs.

using Microsoft.AspNetCore.Authentication;
using Stellaegis.Access.AspNetCore;
using Stellaegis.Access.PostgreSQL;
using Stellaegis.Identity;
using Stellaegis.Identity.AspNetCore;
using Stellaegis.Identity.PostgreSQL;
using Stellaegis.Identity.Server;
using Stellaegis.Issuer;
using Stellaegis.Issuer.Client;
using Stellaegis.Issuer.PostgreSQL;
using Stellaegis.Issuer.Server;
using Stellaegis.Primitives;
using Stellaegis.Primitives.AspNetCore;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;
using Stellaegis.Registry;
using Stellaegis.Registry.AspNetCore;
using Stellaegis.Registry.PostgreSQL;
using Stellaegis.Registry.Sample;

if (args.Length > 0 && args[0] == "install")
    return await Install.RunAsync(args[1..]);

var builder = WebApplication.CreateBuilder(args);

// The frame: one compartment, the company, and the shared space the platform is administered from. The request
// context of the host's own module reads the actor from the token — a person by sub, a client by client_id — and
// the client acting on the actor's behalf from the act claim.
builder.Services.AddStellaegisFrame(SampleFrame.Value);
builder.Services.AddStellaegisRequestContext(context => context.ActorFrom(Callers.PersonOrClient).ViaFromClaim("act"));
builder.Services.AddStellaegisModule(InventoryModule.Declaration);
builder.Services.AddSingleton<Inventory>();

// 1. The accounts, on PostgreSQL (Stellaegis:Identity:PostgreSQL, Stellaegis:Identity): the cookie a person signs
//    in with, the pages (Stellaegis:Identity:Pages), the administration routes, and the admission of the blocked
//    accounts read from the local store (Stellaegis:Identity:Admission).
builder.Services.AddStellaegisIdentityPostgreSQL(builder.Configuration);
builder.Services.AddStellaegisIdentityCookie();
builder.Services.AddStellaegisIdentityPages(builder.Configuration);
builder.Services.AddStellaegisIdentityAdministration();
builder.Services.AddStellaegisLocalBlockedUsersSource();
builder.Services.AddStellaegisIdentityAdmission(builder.Configuration);

// 2. The issuer, on the same PostgreSQL (Stellaegis:Issuer:PostgreSQL, Stellaegis:Issuer): the flows, the clients
//    and the scopes, the revocation; its administration routes; the admission of the revoked tokens
//    (Stellaegis:Issuer:Admission); the sessions page (Stellaegis:Issuer:Pages). And the bearer this host validates
//    its own tokens with (Stellaegis:Issuer:Bearer), for the native client and the services calling its API.
builder.Services.AddStellaegisIssuerPostgreSQL(builder.Configuration);
builder.Services.AddStellaegisIssuerAdministration();
builder.Services.AddStellaegisIssuerAdmission(builder.Configuration);
builder.Services.AddStellaegisIssuerPages(builder.Configuration);
builder.Services.AddStellaegisBearer(builder.Configuration);

// 3. The decision point, on the same PostgreSQL (Stellaegis:Access:PostgreSQL), as the rights system of this host:
//    every policy named after an operation — of the inventory, of the accounts, of the issuer, of the registry, of
//    Access itself — is answered by a decision on the actor, the space and the operation of the request.
builder.Services.AddStellaegisAccessPostgreSQL(builder.Configuration);
builder.Services.AddStellaegisAccessPolicies();

// 4. The registry, on the same PostgreSQL (Stellaegis:Registry:PostgreSQL): the catalogue, the roles, the
//    memberships, the operators; its administration routes; and where the effective rights it computes go — the
//    projection of Access, in process, through one adapter of five lines.
builder.Services.AddStellaegisRegistryPostgreSQL(builder.Configuration);
builder.Services.AddStellaegisRegistryAdministration();
builder.Services.AddStellaegisRightsSink<AccessRightsSink>();

// A policy scheme chooses who authenticates a request: a bearer token names the bearer, anything else the cookie —
// a person on the pages, the browser of the native flow on /connect/authorize.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = SampleAuthentication.Scheme;
        options.DefaultAuthenticateScheme = SampleAuthentication.Scheme;
        options.DefaultChallengeScheme = SampleAuthentication.Scheme;
    })
    .AddPolicyScheme(SampleAuthentication.Scheme, SampleAuthentication.Scheme, scheme => scheme.ForwardDefaultSelector = SampleSchemes.Select);

// What the host brings: the mailer the pages send with — here, the log — and the bus the four modules publish on —
// here, one that records a registered account in the registry, and applies a block at once.
builder.Services.AddSingleton<IIdentityMailer, LoggingMailer>();
builder.Services.AddSingleton<IEventPublisher, SampleEventPublisher>();

// The pages follow the culture of the request: French and English, French when the browser says nothing.
builder.Services.AddRequestLocalization(options => options
    .SetDefaultCulture("fr")
    .AddSupportedCultures("fr", "en")
    .AddSupportedUICultures("fr", "en"));

var app = builder.Build();

// The installation ran before, under another role: prove it, under this one, before serving anything.
await app.Services.VerifyStellaegisDatabasesAsync();

app.UseRequestLocalization();
app.MapStaticAssets();   // the stylesheets, scripts and logos of the Razor class libraries
app.UseAuthentication();

// The door of Primitives: every admission registered — the blocked accounts, the revoked tokens — on every route
// but the issuer's protocol routes, which answer a refused person themselves, to the client, with access_denied.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/connect"), door => door.UseStellaegisActorAdmission());
app.UseAuthorization();

app.MapStellaegisIdentityPages();   // /login, /register, /account/… — and /account/sessions, the issuer's page
app.MapStellaegisIssuer();          // /connect/authorize, /connect/logout; /connect/token is OpenIddict's own

// Every group under one convention: a policy per operation, which Access answers.
app.MapGroup("/api").MapInventoryEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapInventoryAdminEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapIdentityAdminEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapIssuerAdminEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapAccessDecisionEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapAccessProjectionEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());
app.MapGroup("/api/admin").MapRegistryAdminEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());

app.Run();
return 0;
