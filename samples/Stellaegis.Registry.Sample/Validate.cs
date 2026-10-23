using Microsoft.Extensions.Hosting.Internal;
using Stellaegis.Access.PostgreSQL;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;
using Stellaegis.Registry.PostgreSQL;

namespace Stellaegis.Registry.Sample;

/// <summary>
/// The second channel of the sensitive acts: what a person asked on the administration routes — a platform role, the
/// definition of a role, the rescue of a compartment — waits, and is validated here, by whoever may open the
/// administration connection of the database. The same executable, a process apart, under the installer's role, the
/// actor the system account that runs it: whoever stole a web access asks, and the act waits.
/// <code>
/// dotnet run -- validate                    # the acts waiting
/// dotnet run -- validate &lt;act-id&gt;           # validates one: it takes effect at once
/// dotnet run -- validate &lt;act-id&gt; --reject  # refuses it
/// </code>
/// </summary>
public static class Validate
{
    public static async Task<int> RunAsync(string[] args)
    {
        Guid? actId = null;
        if (args.Length > 0)
        {
            if (!Guid.TryParse(args[0], out var parsed) || args.Length > 2 || (args.Length == 2 && args[1] != "--reject"))
            {
                Console.Error.WriteLine("usage: validate [<act-id> [--reject]]");
                return 2;
            }
            actId = parsed;
        }

        // The registry, and Access it projects into, under the administration connection: the frontier of this
        // channel is the system access that guards it, not a session of the application.
        var (environment, configuration) = SampleConfiguration.Load();
        var administration = SampleConfiguration.AdministrationConnectionString(configuration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment, ApplicationName = "validate", ContentRootPath = AppContext.BaseDirectory });
        services.AddStellaegisFrame(SampleFrame.Value);
        services.AddStellaegisModule(InventoryModule.Declaration);
        services.AddStellaegisAccessPostgreSQL(options => options.ConnectionString = administration);
        services.AddStellaegisRegistryPostgreSQL(options => options.ConnectionString = administration);
        services.AddStellaegisRightsSink<AccessRightsSink>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var validations = scope.ServiceProvider.GetRequiredService<IValidations>();

        if (actId is null)
        {
            var now = DateTimeOffset.UtcNow;
            var waiting = (await validations.ListAsync()).Where(act => act.IsPendingAt(now)).ToList();
            if (waiting.Count == 0)
                Console.WriteLine("No act waits for a validation.");
            foreach (var act in waiting)
                Console.WriteLine($"{act.ActId}  {Describe(act)} — asked by {act.Proposed.By} at {act.Proposed.At:u}, until {act.ExpiresAt:u}");
            return 0;
        }

        // Who validates is the account of the system that ran the verb, recorded on the act.
        var actor = Actor.Parse($"system:{Environment.UserDomainName}\\{Environment.UserName}");
        var reject = args.Length == 2;
        var result = reject
            ? await validations.RejectAsync(actId.Value, actor, ValidationChannel.Console)
            : await validations.ValidateAsync(actId.Value, actor, ValidationChannel.Console);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"The act {actId} was not {(reject ? "rejected" : "validated")}: {result.Error}");
            return 1;
        }
        Console.WriteLine($"Act {actId} {(reject ? "rejected" : "validated")} by {actor}.");
        return 0;
    }

    private static string Describe(PendingAct act) => act.Kind switch
    {
        PendingActKind.PlatformRole => $"platform role {act.Role} for {act.Subject}",
        PendingActKind.RoleDefinition => $"role {act.Role} ({act.Scope?.ToString().ToLowerInvariant()}): {string.Join(", ", act.Definition?.Operations ?? [])}",
        _ => $"rescue of {act.Compartment}: {act.Subject} seated as its owner",
    };
}
