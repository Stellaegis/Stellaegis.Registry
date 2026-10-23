using System.Data.Common;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Npgsql;
using Stellaegis.Access.PostgreSQL;
using Stellaegis.Identity;
using Stellaegis.Identity.AspNetCore;
using Stellaegis.Identity.PostgreSQL;
using Stellaegis.Issuer;
using Stellaegis.Issuer.PostgreSQL;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;
using Stellaegis.Registry.PostgreSQL;

namespace Stellaegis.Registry.Sample;

/// <summary>
/// The installation of the example, apart from the application and under another role — the chain from
/// <em>declare</em> to <em>project</em>, before anything is served:
/// <list type="number">
///   <item>the database and the application's role, created if missing;</item>
///   <item>the four schemas — <c>ident</c>, <c>oidc</c>, <c>authz</c>, <c>reg</c> — installed by the life cycle of
///   Primitives under the administration connection: one database, four schemas, one role, the example sharing
///   the instance, a deployment splitting them by configuration alone;</item>
///   <item>the catalogue of the registry recorded from what the modules declare, and the roles the application
///   declares re-applied;</item>
///   <item>the first administrator, created in Identity without a password — whose identifier comes back —, and
///   recorded in the registry;</item>
///   <item>the compartment of the company, created with the administrator seated as its owner, in one gesture — the
///   gesture of a company installing its own system, which knows who holds it; a host of several compartments
///   installs its operators only, and seats the owner of each compartment later, by a designation;</item>
///   <item>the administrator made administrator of the platform: the registry projects their rights into Access;</item>
///   <item>the public client of the desktop declared, and the credentials of the inventory module provisioned, in
///   the issuer.</item>
/// </list>
/// Every write names its actor, <c>job:install</c>. The administrator has no password: the installation prints its
/// activation link, which the installer opens once the application runs, and chooses the password there — no
/// password goes through the command line, its history or the list of processes. Run again, it changes nothing; an
/// administrator still waiting for its activation gets a new link.
/// <code>
/// dotnet run -- install --admin-email admin@acme.test [--display-name Admin]
/// </code>
/// </summary>
public static class Install
{
    /// <summary>The actor of the installation, which every row it writes names.</summary>
    public static readonly Actor Installer = Actor.Job("install");

    /// <summary>The native client of the desktop application: public, authorization code with PKCE, back on the loopback whatever its port.</summary>
    public const string DesktopClientId = "desktop";

    public static async Task<int> RunAsync(string[] args)
    {
        var email = Argument(args, "--admin-email");
        var displayName = Argument(args, "--display-name") ?? "Administrator";
        if (email is null)
        {
            Console.Error.WriteLine("usage: install --admin-email <email> [--display-name <name>]");
            return 2;
        }

        var (environment, configuration) = SampleConfiguration.Load();

        // The application's connection string names the database and the role the application runs under; the
        // installer's connection string reaches the server under a role that may create both.
        var application = new NpgsqlConnectionStringBuilder(configuration["Stellaegis:Registry:PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("Stellaegis:Registry:PostgreSQL:ConnectionString is not configured."));
        var installer = new NpgsqlConnectionStringBuilder(configuration["Sample:Install:ConnectionString"]
            ?? throw new InvalidOperationException("Sample:Install:ConnectionString is not configured."));
        var role = application.Username ?? throw new InvalidOperationException("The application's connection string names no role.");
        var database = application.Database ?? throw new InvalidOperationException("The application's connection string names no database.");

        // 1. The database and the role, if missing.
        await using (var server = new NpgsqlConnection(installer.ConnectionString))
        {
            await server.OpenAsync();
            await using (var exists = new NpgsqlCommand("select 1 from pg_database where datname = @name", server))
            {
                exists.Parameters.AddWithValue("name", database);
                if (await exists.ExecuteScalarAsync() is null)
                {
                    await using var create = new NpgsqlCommand($"create database \"{database}\"", server);
                    await create.ExecuteNonQueryAsync();
                    Console.WriteLine($"Database {database} created.");
                }
            }
            await using (var createRole = new NpgsqlCommand(
                $"""
                do $$ begin
                  create role "{role}" login password '{application.Password?.Replace("'", "''")}';
                exception when duplicate_object or unique_violation then null;
                end $$;
                """, server))
            {
                await createRole.ExecuteNonQueryAsync();
            }
        }

        // 2. The four schemas, under the installer, which owns them; the application's role granted its rights.
        var adminOnDatabase = SampleConfiguration.AdministrationConnectionString(configuration);
        var report = await DatabaseLifecycle.InstallAsync(
            [IdentityDatabase.Value, IssuerDatabase.Value, AccessDatabase.Value, RegistryDatabase.Value], SampleFrame.Value,
            async (_, ct) => { DbConnection c = new NpgsqlConnection(adminOnDatabase); await c.OpenAsync(ct); return c; },
            role, Installer);
        foreach (var applied in report.Applied)
            Console.WriteLine($"Applied: {applied}");
        foreach (var skipped in report.Skipped)
            Console.WriteLine($"Already applied: {skipped}");

        // The data of the installation, by the services of the modules, under the application's role, as the
        // administration routes would: the installation is the one actor that creates them by itself. The four
        // modules and the host's own are registered as the application registers them, so that the catalogue
        // reads the same declarations, and the registry projects into Access through the same adapter.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment, ApplicationName = "install", ContentRootPath = AppContext.BaseDirectory });
        services.AddStellaegisFrame(SampleFrame.Value);
        services.AddStellaegisModule(InventoryModule.Declaration);
        services.AddStellaegisIdentityPostgreSQL(configuration);
        services.AddStellaegisIssuerPostgreSQL(configuration);
        services.AddStellaegisAccessPostgreSQL(configuration);
        services.AddStellaegisRegistryPostgreSQL(configuration);
        services.AddStellaegisRightsSink<AccessRightsSink>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var modules = provider.GetStellaegisModules();

        // 3. The catalogue: every operation every module declares — the registry's own, the accounts', the issuer's,
        //    Access's, the inventory's — and the roles the application declares over it.
        var catalogue = await scope.ServiceProvider.GetRequiredService<ICatalog>().RecordAsync(modules);
        Console.WriteLine($"Catalogue: {catalogue.Count} operations from {modules.Count} modules ({string.Join(", ", modules.Select(m => m.Name))}).");
        var declared = await scope.ServiceProvider.GetRequiredService<IRoles>().DeclareAsync(SampleRoles.Declarations(modules), Installer);
        if (declared.IsFailure)
        {
            Console.Error.WriteLine($"The roles were not declared: {declared.Error}");
            return 1;
        }
        Console.WriteLine($"Roles declared: {RegistryRoles.Owner}, {SampleRoles.TenantAdministrator}, {SampleRoles.ShopManager}, {SampleRoles.Operator} in the compartments; {SampleRoles.SuperAdmin} on the platform.");

        // 4. The first administrator, without a password: created in Identity, whose identifier comes back, and
        //    recorded in the registry. Its activation link is handed to the installer, at the end.
        var administration = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        string? token = null;
        string subject;
        var account = await administration.CreateAsync(email, displayName, TokenDelivery.Hand, Installer);
        if (account.IsSuccess)
        {
            subject = account.Value.UserUniqueId.ToString("N");
            token = account.Value.ActivationToken;
            Console.WriteLine($"Administrator {email} created in Identity, {subject}.");
        }
        else if (account.Error?.Code == IdentityErrors.EmailTakenCode)
        {
            var existing = await scope.ServiceProvider.GetRequiredService<IAuthUserStore>().GetByEmailAsync(Emails.Normalize(email));
            if (existing is null)
            {
                Console.Error.WriteLine($"Administrator {email} exists in Identity and cannot be read back.");
                return 1;
            }
            subject = existing.UserUniqueId.ToString("N");
            if (!existing.IsActivated)
                token = (await administration.IssueActivationAsync(existing.UserUniqueId, TokenDelivery.Hand, Installer)).Value?.Token;
            Console.WriteLine($"Administrator {email} exists already in Identity, {subject}{(existing.IsActivated ? ", and is active" : "")}.");
        }
        else
        {
            Console.Error.WriteLine($"The administrator was not created: {account.Error}");
            return 1;
        }

        var recorded = await scope.ServiceProvider.GetRequiredService<IUserDirectory>().RecordAsync(subject, email, displayName, Installer);
        if (recorded.IsFailure)
        {
            Console.Error.WriteLine($"The administrator was not recorded in the registry: {recorded.Error}");
            return 1;
        }

        // 5. The compartment of the company, its owner seated in the same gesture: the owner becomes its first member
        //    and holds the role of the owner. A compartment found without one — an installation interrupted — gets it.
        var compartments = scope.ServiceProvider.GetRequiredService<ICompartments>();
        var found = await compartments.GetAsync(SampleFrame.Company);
        if (found.IsFailure)
        {
            var created = await compartments.CreateAsync(SampleFrame.Company, "Acme SA", subject, Installer);
            if (created.IsFailure)
            {
                Console.Error.WriteLine($"The compartment was not created: {created.Error}");
                return 1;
            }
            Console.WriteLine($"Compartment {SampleFrame.Company} created, owned by the administrator.");
        }
        else if (found.Value.Owner is null)
        {
            var seated = await scope.ServiceProvider.GetRequiredService<IOwnership>().SeatAsync(SampleFrame.Company, subject, Installer);
            if (seated.IsFailure)
            {
                Console.Error.WriteLine($"The owner of {SampleFrame.Company} was not seated: {seated.Error}");
                return 1;
            }
            Console.WriteLine($"Compartment {SampleFrame.Company} exists already: the administrator seated as its owner.");
        }
        else
            Console.WriteLine($"Compartment {SampleFrame.Company} exists already, owned by {found.Value.Owner}.");

        // 6. The administrator of the platform: applied at once, the installation being the one actor whose platform
        //    roles wait for no validation. The grant goes through the sink into Access.
        var assigned = await scope.ServiceProvider.GetRequiredService<IOperators>().AssignAsync(subject, SampleRoles.SuperAdmin, Installer);
        if (assigned.IsFailure)
        {
            Console.Error.WriteLine($"The administrator's platform role was not recorded: {assigned.Error}");
            return 1;
        }
        Console.WriteLine($"Administrator recorded in the registry: {RegistryRoles.Owner} of {SampleFrame.Company}, {SampleRoles.SuperAdmin} on the platform — projected into Access.");

        // 7. The clients of the issuer: the native client of the desktop, and the credentials of the inventory
        //    module — a service calling the API in its own name would use them; the secret generated is shown
        //    once, then the configuration's is re-applied so that a service's configuration can name the same.
        var catalog = scope.ServiceProvider.GetRequiredService<IssuerCatalog>();
        var provisioner = scope.ServiceProvider.GetRequiredService<IOidcClientProvisioner>();
        var provisioned = await provisioner.ProvisionModuleClientAsync(InventoryModule.Name);
        if (provisioned.IsSuccess)
            Console.WriteLine($"Module {InventoryModule.Name} provisioned: client {provisioned.Value.ClientId}, secret {provisioned.Value.Secret} (shown once).");
        else if (provisioned.Error?.Code == IssuerErrors.ClientExistsCode)
            Console.WriteLine($"Module {InventoryModule.Name} is provisioned already.");
        else
        {
            Console.Error.WriteLine($"The module {InventoryModule.Name} was not provisioned: {provisioned.Error}");
            return 1;
        }
        if ((await catalog.GetScopeAsync(IssuerModule.Name)).IsFailure)
            await catalog.CreateScopeAsync(new ScopeDefinition(IssuerModule.Name, Description: "Reading the issuer: the revoked tokens, the scopes."));

        var desktopDefinition = new ClientDefinition(DesktopClientId, "The desktop application", ClientKind.Native, Scopes: ["email", "profile", InventoryModule.Name, IssuerModule.Name]);
        var desktop = (await catalog.GetClientAsync(DesktopClientId)).IsFailure
            ? await catalog.CreateClientAsync(desktopDefinition)
            : await catalog.UpdateClientAsync(desktopDefinition);
        if (desktop.IsFailure)
        {
            Console.Error.WriteLine($"The client {DesktopClientId} was not declared: {desktop.Error}");
            return 1;
        }
        var applied2 = await catalog.ApplyConfiguredClientsAsync();
        Console.WriteLine($"Client {DesktopClientId} declared: native, authorization code with PKCE, redirect URIs {string.Join(", ", desktop.Value.Client.RedirectUris)}. Configuration applied to: {string.Join(", ", applied2)}.");

        if (token is not null)
        {
            var links = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Links;
            Console.WriteLine("Open this link once the application runs, and choose the administrator's password there:");
            Console.WriteLine(IdentityLinks.Of(links, null, IdentityLinks.ActivatePath, token));
        }
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

/// <summary>What the two verbs of the host read: the configuration of the application, and the installer's connection.</summary>
public static class SampleConfiguration
{
    /// <summary>
    /// The configuration the application reads, in its environment. The example is a development host: its issuer
    /// signs with development certificates, which only the Development environment accepts.
    /// </summary>
    public static (string Environment, IConfiguration Configuration) Load()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<LoggingMailer>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        return (environment, configuration);
    }

    /// <summary>The installer's connection, on the application's database: the role that owns the schemas.</summary>
    public static string AdministrationConnectionString(IConfiguration configuration)
    {
        var application = new NpgsqlConnectionStringBuilder(configuration["Stellaegis:Registry:PostgreSQL:ConnectionString"]
            ?? throw new InvalidOperationException("Stellaegis:Registry:PostgreSQL:ConnectionString is not configured."));
        var installer = configuration["Sample:Install:ConnectionString"]
            ?? throw new InvalidOperationException("Sample:Install:ConnectionString is not configured.");
        return new NpgsqlConnectionStringBuilder(installer) { Database = application.Database }.ConnectionString;
    }
}
