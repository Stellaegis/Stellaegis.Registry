using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives;
using Stellaegis.Registry.InMemory;

namespace Stellaegis.Registry.Tests;

/// <summary>Keeps every event published, for the tests to read.</summary>
internal sealed class CapturingEventPublisher : IEventPublisher
{
    public ConcurrentQueue<IEvent> Events { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        Events.Enqueue(@event);
        return Task.CompletedTask;
    }
}

/// <summary>The registry on its stores — in memory unless the caller registers others —, a recording sink and a capturing publisher.</summary>
internal sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public TestHost(bool withSink = true, Action<IServiceCollection>? stores = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventPublisher>(Events);
        if (stores is null)
            services.AddStellaegisRegistryInMemory();
        else
            stores(services);
        if (withSink)
            services.AddStellaegisRightsSink(Sink);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    public CapturingEventPublisher Events { get; } = new();

    public RecordingRightsSink Sink { get; } = new();

    public IServiceProvider Services => _scope.ServiceProvider;

    public ICatalog Catalog => Services.GetRequiredService<ICatalog>();

    public IRoles Roles => Services.GetRequiredService<IRoles>();

    public ICompartments Compartments => Services.GetRequiredService<ICompartments>();

    public IUserDirectory Users => Services.GetRequiredService<IUserDirectory>();

    public IMemberships Memberships => Services.GetRequiredService<IMemberships>();

    public IOperators Operators => Services.GetRequiredService<IOperators>();

    public InMemoryRegistryData Data => Services.GetRequiredService<InMemoryRegistryData>();

    /// <summary>The catalogue of the two test modules, the declared roles, the compartment, two users: the state most tests start from.</summary>
    public async Task<TestHost> InstalledAsync(CancellationToken ct)
    {
        await Catalog.RecordAsync([Fixtures.Inventory, Fixtures.Billing, RegistryModule.Declaration], ct);
        Assert.True((await Roles.DeclareAsync(Fixtures.DeclaredRoles, Fixtures.By, ct)).IsSuccess);
        Assert.True((await Compartments.CreateAsync(Fixtures.Acme, "Acme SA", Fixtures.By, ct)).IsSuccess);
        Assert.True((await Users.RecordAsync(Fixtures.Jane, "jane@acme.test", "Jane Admin", Fixtures.By, ct)).IsSuccess);
        Assert.True((await Users.RecordAsync(Fixtures.John, "john@acme.test", "John Doe", Fixtures.By, ct)).IsSuccess);
        Sink.Clear();
        Events.Events.Clear();
        return this;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

/// <summary>Two modules of a test application, their operations, the roles it declares, a compartment, two users.</summary>
internal static class Fixtures
{
    private sealed class InventoryModule : IStellaegisModule
    {
        public static string Name => "inventory";
    }

    private sealed class BillingModule : IStellaegisModule
    {
        public static string Name => "billing";
    }

    /// <summary>The inventory module declared a second time, for the test that records the catalogue again.</summary>
    public sealed class InventoryModuleAgain : IStellaegisModule
    {
        public static string Name => "inventory";
    }

    public static readonly Operation ItemsRead = new("items", "read", OperationKind.Read);
    public static readonly Operation ItemsWrite = new("items", "write", OperationKind.Write);
    public static readonly Operation ItemsPurge = new("items", "purge", OperationKind.Delete);
    public static readonly Operation InvoicesRead = new("invoices", "read", OperationKind.Read);
    public static readonly Operation InvoicesIssue = new("invoices", "issue", OperationKind.Write);

    public static readonly ModuleDeclaration Inventory = ModuleDeclaration.Compartmented<InventoryModule>().WithOperations(ItemsRead, ItemsWrite, ItemsPurge);
    public static readonly ModuleDeclaration Billing = ModuleDeclaration.Compartmented<BillingModule>().WithOperations(InvoicesRead, InvoicesIssue);

    public static readonly RoleDeclaration Reader = new("reader", RoleScope.Compartment, "Reader", [ItemsRead, InvoicesRead]);
    public static readonly RoleDeclaration Accountant = new("accountant", RoleScope.Compartment, "Accountant", [InvoicesRead, InvoicesIssue]);
    public static readonly RoleDeclaration SuperAdmin = new("super_admin", RoleScope.Platform, "Platform administrator", [ItemsPurge, RegistryOperations.CompartmentsWrite]);
    public static readonly IReadOnlyList<RoleDeclaration> DeclaredRoles = [Reader, Accountant, SuperAdmin];

    /// <summary>The actor of the writes of the tests: the installation of a test, as an installation names itself.</summary>
    public static readonly Actor By = Actor.Job("tests");

    public static readonly Compartment Acme = Compartment.Parse("acme");
    public static readonly Compartment Globex = Compartment.Parse("globex");
    public static readonly string Jane = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301").ToString("N");
    public static readonly string John = Guid.Parse("8d1e3b2a-6c4f-4a8e-9b1d-2f3e4a5b6c7d").ToString("N");
}
