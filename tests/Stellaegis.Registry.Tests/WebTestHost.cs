using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stellaegis.Primitives;
using Stellaegis.Primitives.Hosting;
using Stellaegis.Registry.AspNetCore;

namespace Stellaegis.Registry.Tests;

/// <summary>
/// A web host of the registry assembled in memory on the test server, with an authentication read from headers —
/// <c>X-Actor</c> for the subject, <c>X-Grants</c> for the operations granted, or <c>Authorization: Bearer</c> with
/// every operation granted, for a service — and one policy per operation, as a host with rights of its own has. A
/// recording sink reads what every change projects.
/// </summary>
internal static class WebTestHost
{
    public static WebApplicationBuilder Builder(ApplicationFrame? frame = null, RecordingRightsSink? sink = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddStellaegisFrame(frame ?? ApplicationFrame.WithoutCompartments);
        builder.Services.AddStellaegisRegistryInMemory();
        builder.Services.AddStellaegisRegistryAdministration();
        builder.Services.AddStellaegisRightsSink(sink ?? new RecordingRightsSink());
        return builder;
    }

    /// <summary>The header authentication, and one policy per operation of the module requiring its grant.</summary>
    public static WebApplicationBuilder WithHeaderAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(HeaderAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.SchemeName, null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (var operation in RegistryOperations.All)
                options.AddPolicy(operation.ToString(), policy => policy.RequireClaim(HeaderAuthentication.GrantClaim, operation.ToString()));
        });
        return builder;
    }

    /// <summary>The catalogue of the test modules, the declared roles, the compartment and the two users recorded, as an installation would.</summary>
    public static async Task InstallAsync(this WebApplication app, CancellationToken ct)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<ICatalog>().RecordAsync([Fixtures.Inventory, Fixtures.Billing, RegistryModule.Declaration], ct);
        Assert.True((await services.GetRequiredService<IRoles>().DeclareAsync(Fixtures.DeclaredRoles, Fixtures.By, ct)).IsSuccess);
        Assert.True((await services.GetRequiredService<ICompartments>().CreateAsync(Fixtures.Acme, "Acme SA", Fixtures.By, ct)).IsSuccess);
        var users = services.GetRequiredService<IUserDirectory>();
        Assert.True((await users.RecordAsync(Fixtures.Jane, "jane@acme.test", "Jane Admin", Fixtures.By, ct)).IsSuccess);
        Assert.True((await users.RecordAsync(Fixtures.John, "john@acme.test", "John Doe", Fixtures.By, ct)).IsSuccess);
        app.Sink().Clear();
    }

    public static RecordingRightsSink Sink(this WebApplication app) => (RecordingRightsSink)app.Services.GetRequiredService<IRightsSink>();

    /// <summary>A client of the host calling as <paramref name="actor"/>, holding <paramref name="grants"/>.</summary>
    public static HttpClient ClientAs(this WebApplication app, string? actor, params string[] grants)
    {
        var client = app.GetTestClient();
        if (actor is not null)
            client.DefaultRequestHeaders.Add(HeaderAuthentication.ActorHeader, actor);
        if (grants.Length > 0)
            client.DefaultRequestHeaders.Add(HeaderAuthentication.GrantsHeader, string.Join(",", grants));
        return client;
    }

    public static async Task<string?> CodeOfAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);
        return problem?.Code;
    }

    private sealed record ProblemBody(string? Code);

    internal sealed class HeaderAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";
        public const string ActorHeader = "X-Actor";
        public const string GrantsHeader = "X-Grants";
        public const string GrantClaim = "grant";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? subject = null;
            IEnumerable<string> grants = [];
            if (Request.Headers.TryGetValue(ActorHeader, out var actor))
            {
                subject = actor.ToString();
                grants = Request.Headers.TryGetValue(GrantsHeader, out var granted)
                    ? granted.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
            }
            else if (AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var bearer) && bearer.Scheme == "Bearer" && bearer.Parameter is { } token)
            {
                subject = token;
                grants = RegistryOperations.All.Select(operation => operation.ToString());
            }
            if (subject is null)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new("sub", subject) };
            claims.AddRange(grants.Select(grant => new Claim(GrantClaim, grant)));
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
