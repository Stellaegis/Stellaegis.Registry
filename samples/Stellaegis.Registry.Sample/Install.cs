using System.Data.Common;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;
using Stellaegis.Access.PostgreSQL;
using Stellaegis.Identity;
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
///   <item>the compartment of the company;</item>
///   <item>the first administrator, created in Identity — whose identifier comes back —, recorded in the registry,
///   made administrator of the platform and of the compartment: the registry projects their rights into Access;</item>
///   <item>the public client of the desktop declared, and the credentials of the inventory module provisioned, in
///   the issuer.</item>
/// </list>
/// Every write names its actor, <c>job:install</c>. Run again, it changes nothing.
/// <code>
/// dotnet run -- install --admin-email admin@acme.test --admin-password 'Correct-Horse-9' [--display-name Admin]
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
        var password = Argument(args, "--admin-password");
        var displayName = Argument(args, "--display-name") ?? "Administrator";
        if (email is null || password is null)
        {
            Console.Error.WriteLine("usage: install --admin-email <email> --admin-password <password> [--display-name <name>]");
            return 2;
        }

        // The example is a development host: its issuer signs with development certificates, which only the
        // Development environment accepts. The installer reads the same configuration as the application.
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<LoggingMailer>(optional: true)
            .AddEnvironmentVariables()
            .Build();

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
        var adminOnDatabase = new NpgsqlConnectionStringBuilder(installer.ConnectionString) { Database = database }.ConnectionString;
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
        Console.WriteLine($"Roles declared: {SampleRoles.TenantAdministrator}, {SampleRoles.ShopManager}, {SampleRoles.Operator} in the compartments; {SampleRoles.SuperAdmin} on the platform.");

        // 4. The compartment of the company.
        var compartments = scope.ServiceProvider.GetRequiredService<ICompartments>();
        var created = await compartments.CreateAsync(SampleFrame.Company, "Acme SA", Installer);
        if (created.IsSuccess)
            Console.WriteLine($"Compartment {SampleFrame.Company} created.");
        else if (created.Error?.Code == RegistryErrors.CompartmentExistsCode)
            Console.WriteLine($"Compartment {SampleFrame.Company} exists already.");
        else
        {
            Console.Error.WriteLine($"The compartment was not created: {created.Error}");
            return 1;
        }

        // 5. The first administrator: created in Identity, whose identifier comes back; recorded in the registry;
        //    administrator of the platform and of the compartment. Each grant goes through the sink into Access.
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministrationService>();
        var account = await accounts.CreateAsync(email, displayName, password, Installer);
        string subject;
        if (account.IsSuccess)
        {
            subject = account.Value.ToString("N");
            Console.WriteLine($"Administrator {email} created in Identity, {subject}.");
        }
        else if (account.Error?.Code == IdentityErrors.EmailTakenCode)
        {
            var existing = await scope.ServiceProvider.GetRequiredService<IAuthUserStore>().GetByEmailAsync(email);
            if (existing is null)
            {
                Console.Error.WriteLine($"Administrator {email} exists in Identity and cannot be read back.");
                return 1;
            }
            subject = existing.UserUniqueId.ToString("N");
            Console.WriteLine($"Administrator {email} exists already in Identity, {subject}.");
        }
        else
        {
            Console.Error.WriteLine($"The administrator was not created: {account.Error}");
            return 1;
        }

        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var recorded = await directory.RecordAsync(subject, email, displayName, Installer);
        if (recorded.IsFailure)
        {
            Console.Error.WriteLine($"The administrator was not recorded in the registry: {recorded.Error}");
            return 1;
        }
        var operators = scope.ServiceProvider.GetRequiredService<IOperators>();
        var memberships = scope.ServiceProvider.GetRequiredService<IMemberships>();
        foreach (var result in new[]
        {
            await operators.AssignAsync(subject, SampleRoles.SuperAdmin, Installer),
            await memberships.AddAsync(subject, SampleFrame.Company, Installer),
            await memberships.AssignRoleAsync(subject, SampleFrame.Company, SampleRoles.TenantAdministrator, Installer),
        })
        {
            if (result.IsFailure)
            {
                Console.Error.WriteLine($"The administrator's rights were not recorded: {result.Error}");
                return 1;
            }
        }
        Console.WriteLine($"Administrator recorded in the registry: {SampleRoles.SuperAdmin} on the platform, {SampleRoles.TenantAdministrator} in {SampleFrame.Company} — projected into Access.");

        // 6. The clients of the issuer: the native client of the desktop, and the credentials of the inventory
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
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
