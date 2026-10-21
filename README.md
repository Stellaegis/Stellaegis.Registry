# Stellaegis.Registry

Stellaegis.Registry is the registry of a .NET application assembled on
[Stellaegis.Primitives](https://github.com/Stellaegis/Stellaegis.Primitives): what gives a right its meaning. It
holds the compartments, the users, the catalogue of the operations the modules declare, the roles that group them,
the memberships of the people in the compartments and the roles of the operators of the platform. From those it
computes the effective rights of a person in a space, and hands them — synchronously, after every change — to the
decision point, [Stellaegis.Access](https://github.com/Stellaegis/Stellaegis.Access), through a port the host
implements in five lines.

It knows neither accounts (`Stellaegis.Identity`, whose registered users it records), nor tokens
(`Stellaegis.Issuer`), nor decisions (`Stellaegis.Access`). It is the enterprise subset of a registry: one
compartment or several, no onboarding, no invitation, no provisioning, no tenant portal — those belong to the
package of the SaaS, to come. It depends on Primitives alone; a host composes it with the three others.

The library targets .NET 10 and is released under the MIT license.

## Packages

| Package | Contents | Depends on |
| --- | --- | --- |
| `Stellaegis.Registry` | The compartments, the users, the catalogue, the roles, the memberships, the operators; the effective rights and the `IRightsSink` port; the module and its operations; the role declarations; the in-memory stores | `Stellaegis.Primitives`, `Stellaegis.Primitives.Hosting` |
| `Stellaegis.Registry.PostgreSQL` | The six stores on PostgreSQL, and the `reg` schema declared as a module database for the installation and the verification | `Stellaegis.Primitives.Data`, Npgsql, Dapper |
| `Stellaegis.Registry.AspNetCore` | The administration routes — compartments, users, catalogue, roles, memberships, operators —, each under its operation, decided in the shared space | `Stellaegis.Primitives.AspNetCore`, ASP.NET Core |

The three packages share one version, and follow the major version of Stellaegis.Primitives: 5.0.0 runs on
Primitives 5.1.0.

## A host, in full

An enterprise application — one compartment, the company, and the shared space of the platform — holding its
accounts, issuing its tokens, deciding its rights and giving them their meaning, in one process, with a module of
its own. This is `samples/Stellaegis.Registry.Sample`, the first assembly of the whole base of the family: it runs
as is on a PostgreSQL server and its `README` plays the chain from end to end. The registry's part of it:

```csharp
// The registry on PostgreSQL (Stellaegis:Registry:PostgreSQL), its administration routes, and where the effective
// rights it computes go: the projection of Access, in process, through one adapter.
builder.Services.AddStellaegisRegistryPostgreSQL(builder.Configuration);
builder.Services.AddStellaegisRegistryAdministration();
builder.Services.AddStellaegisRightsSink<AccessRightsSink>();

// The bus of the host records a registered account in the registry's directory, by the person themselves, and
// deactivates a blocked one — which withdraws its rights from Access through the sink.
builder.Services.AddSingleton<IEventPublisher, SampleEventPublisher>();

var app = builder.Build();
await app.Services.VerifyStellaegisDatabasesAsync();   // the four schemas, under the application's role
...
app.MapGroup("/api/admin").MapRegistryAdminEndpoints()
    .RequireAuthorizationPerOperation(operation => operation.ToString());   // Access answers each policy
```

The adapter, the two ports having the same shape on purpose:

```csharp
public sealed class AccessRightsSink(IAccessProjection projection) : IRightsSink
{
    public Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken ct = default)
        => projection.ReplaceMembershipsAsync(subject, compartments, ct);
    public Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<string> operations, CancellationToken ct = default)
        => projection.ReplaceUserGrantsAsync(subject, compartment, operations, ct);
    public Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<string> operations, CancellationToken ct = default)
        => projection.ReplaceAdminGrantsAsync(subject, operations, ct);
    public Task RemoveCompartmentAsync(Compartment compartment, CancellationToken ct = default)
        => projection.RemoveCompartmentAsync(compartment, ct);
    public Task RemoveSubjectAsync(string subject, CancellationToken ct = default)
        => projection.RemoveSubjectAsync(AccessSubject.Person(subject), ct);
}
```

A host whose decision point runs in another process implements the same five methods on the projection routes of
Access. Without a sink registered, the snapshots go nowhere and a warning says so once.

## Declare, install, grant, project, decide

The registry is one link of a chain, and not the first:

1. **Declare**, in code. Every module declares its operations on its `ModuleDeclaration` — `items.read`,
   `items.write` —, and the application declares the roles it ships with, `RoleDeclaration`: a name, a scope, a
   display name, a set of operations.
2. **Install**, apart from serving. The installation records the catalogue from what the modules declare,
   `ICatalog.RecordAsync(services.GetStellaegisModules())` — nothing is seeded by hand —, re-applies the declared
   roles, `IRoles.DeclareAsync`, creates the compartment, and records the first administrator with their roles.
3. **Grant**, by the routes: roles created or changed, memberships, assignments.
4. **Project**: after every change that touches rights, the registry recomputes the snapshot of the subject in the
   space and hands it to `IRightsSink`, in the same operation — deterministically, rather than through an event
   whose delivery would have to be made reliable. The events the registry publishes say that something changed;
   they carry no snapshot.
5. **Decide**: Access, on the actor, the space and the operation of each request.

```csharp
public static class SampleRoles
{
    public static IReadOnlyList<RoleDeclaration> Declarations(IEnumerable<ModuleDeclaration> modules) =>
    [
        new("shop_manager", RoleScope.Compartment, "Chef d'atelier", [InventoryOperations.Read, InventoryOperations.Write]),
        new("operator", RoleScope.Compartment, "Opérateur", [InventoryOperations.Read]),
        new("super_admin", RoleScope.Platform, "Administrateur de la plateforme",
            modules.SelectMany(module => module.Operations).DistinctBy(operation => operation.Identity).ToList()),
    ];
}

// At installation, under the actor of the installation:
var modules = provider.GetStellaegisModules();
await catalog.RecordAsync(modules);
await roles.DeclareAsync(SampleRoles.Declarations(modules), Actor.Job("install"));
await compartments.CreateAsync(Compartment.Parse("acme"), "Acme SA", Actor.Job("install"));
await directory.RecordAsync(subject, email, displayName, Actor.Job("install"));
await operators.AssignAsync(subject, "super_admin", Actor.Job("install"));
await memberships.AddAsync(subject, acme, Actor.Job("install"));
await memberships.AssignRoleAsync(subject, acme, "tenant_administrator", Actor.Job("install"));
```

A declared role is created when missing and reset — display name and operations — when present: an edit by hand
is put back by the next installation, and a declared role is never deleted. A role created by hand is never touched
by a declaration, and a declaration whose name is taken by one is refused. The catalogue adds and updates the kind
of an operation, and removes nothing: an operation a module no longer declares stays grantable until someone
decides otherwise.

## What the registry holds

Two parallel models, never mixed — the people in the compartments, and the operators of the platform —, the
registry's own invariant since its first version:

| Table of `reg` | Key | What it is |
| --- | --- | --- |
| `compartments` | `compartment` | the compartments; `active`, `suspended` or `terminated` — never deleted, a name is never reused |
| `users` | `subject` | the projection of the accounts of Identity: the `sub`, the e-mail, the display name; `active` or `deactivated` |
| `operations` | `operation` | the catalogue: the identity `target.name`, the module, the kind |
| `roles`, `role_operations` | `role` | a name, a scope (`compartment` or `platform`), a display name, whether declared, its operations — each in the catalogue |
| `memberships` | `(subject, compartment)` | the compartments each person is a member of |
| `member_roles` | `(subject, compartment, role)` | the compartment roles of each member; gone with the membership |
| `operator_roles` | `(subject, role)` | the platform roles of each operator |

No surrogate key — nothing joins these tables on anything but their natural keys —, no row-level security — the
module has no compartment, it acts on them —, foreign keys where they protect: a role still held is not deleted, a
role names operations of the catalogue, a member role names a membership. The grammars are repeated as `CHECK`s,
so that a row written past the module cannot hold what the registry would never produce.

A subject is the `sub` of an account of Identity, a GUID written as 32 hex digits. A role name is an identifier,
`super_admin`, `tenant_administrator`. An operation's identity is global in a host: it is the key Access grants,
whatever module declares it — which is why the registry names its own operations on the directory of users
`directory.read` and `directory.write`, `users.read` being Identity's.

### The effective rights

A person holds, in a compartment, the union of the operations of their compartment roles there — if they are a
member, the compartment is active and they are; on the platform, the union of the operations of their platform
roles — if they are active. A deactivated user and a suspended compartment keep their rows and lose their rights at
the sink; reactivating and resuming project everything back. Terminating a compartment removes its memberships
from the registry as well, and its name stays taken. A role changed recomputes every holder.

### Every write names its actor

Every write of the registry carries who made it and when: `created_by` and `modified_by` on the compartments, the
users and the roles, `granted_by` on the memberships, the member roles and the operator roles — never defaulted,
an actor forgotten being an error rather than "system". The services take the `Actor` on every write, the rows keep
it, the summaries of the routes answer it, the events carry it (`By`). The routes pass the principal the host
authenticated; the installation names itself, `job:install`; a user recorded from their registration is recorded
by themselves.

## Installing, then verifying

`Stellaegis.Registry.PostgreSQL` declares its database, `RegistryDatabase.Value`: the schema `reg` and its scripts,
embedded in the package. An installer installs it under an administration role, which owns the schema and grants
the application's role its rights; a host verifies it at startup under the application's role, and refuses to
start on a schema missing or a script changed:

```csharp
// The installation, apart from the application.
await DatabaseLifecycle.InstallAsync([RegistryDatabase.Value], frame, openAsAdministrator, applicationRole, Actor.Job("install"));

// The host.
builder.Services.AddStellaegisRegistryPostgreSQL(builder.Configuration);   // Stellaegis:Registry:PostgreSQL:ConnectionString
builder.Services.AddStellaegisDatabaseVerification();                        // a generic host; a web host calls
await app.Services.VerifyStellaegisDatabasesAsync();                         // this one before serving
```

## The routes

`MapRegistryAdminEndpoints()` maps the administration routes under `registry`, each labelled with its operation;
the host declares who may call them — `RequireAuthorizationPerOperation`, answered by Access in the shared space.
The module has no compartments: its routes read no request context, and an operator of the platform who is also a
member of a compartment is, in that compartment, in the situation of any member.

| Routes | Operation |
| --- | --- |
| `GET compartments`, `GET compartments/{compartment}` | `compartments.read` |
| `POST compartments` (201), `PUT compartments/{compartment}` (the display name), `POST compartments/{compartment}/suspend`, `…/resume`, `…/terminate` | `compartments.write` |
| `GET users`, `GET users/{subject}` — no creation: a user is the projection of an account of Identity | `directory.read` |
| `POST users/{subject}/deactivate`, `…/reactivate` | `directory.write` |
| `GET catalog`, `GET catalog/{module}` | `catalog.read` |
| `GET roles?scope=`, `GET roles/{role}` | `roles.read` |
| `POST roles` (201), `PUT roles/{role}`, `DELETE roles/{role}` — a declared role is changed, never deleted | `roles.write` |
| `GET memberships/compartments/{compartment}`, `GET memberships/users/{subject}` | `memberships.read` |
| `PUT memberships/{compartment}/{subject}`, `DELETE …`, `PUT …/roles/{role}`, `DELETE …/roles/{role}` | `memberships.write` |
| `GET operators`, `GET operators/{subject}` | `operators.read` |
| `PUT operators/{subject}/roles/{role}`, `DELETE …` | `operators.write` |

A refusal of a service is answered as problem details with its code — `Compartment.Exists` (409),
`Compartment.Terminated`, `Role.Declared`, `Role.InUse`, `Membership.NotMember`, `User.Deactivated` (412),
`Catalog.UnknownOperation`, `Role.ScopeMismatch`, `Role.NameInvalid` (400), `*.NotFound` (404); a malformed name is
a 400 `Registry.Invalid`. The states and the scopes travel as words, `active`, `suspended`, `terminated`,
`deactivated`, `compartment`, `platform`.

## The users, from Identity

The registry records who exists, to give a membership or a role to; it authenticates nothing. The host wires its
bus to the directory: a registered account is recorded, by the person themselves; a blocked one is deactivated,
which withdraws its rights everywhere; an unblocked one is reactivated. A module subscribes to nothing — the host
calls the explicit operations from its subscriptions:

```csharp
case UserRegisteredEvent registered:
    var subject = registered.UserUniqueId.ToString("N");
    await directory.RecordAsync(subject, registered.Email, registered.DisplayName, Actor.Parse(subject), ct);
    break;
case UserBlockedEvent blocked:
    await directory.DeactivateAsync(blocked.UserUniqueId.ToString("N"), Actor.Job("identity"), ct);
    break;
```

## Events

`CompartmentChangedEvent`, `UserChangedEvent`, `RoleChangedEvent`, `MembershipChangedEvent` — each with who made the
change —, and `RightsChangedEvent` after the effective rights of a subject in a space were recomputed and handed to
the sink. It carries no snapshot: the rights went through the port, deterministically; the event only says that
they changed, for a cache of decisions apart to invalidate. Published through the `IEventPublisher` the host
registers, or nowhere.

## Options

| Section | Options |
| --- | --- |
| `Stellaegis:Registry:PostgreSQL` | `ConnectionString`, under the application's role |

Validated when the host starts.

## Tests

`tests/Stellaegis.Registry.Tests`: the catalogue from the declarations, the declared roles re-applied without
touching a role made by hand, an operation outside the catalogue refused; every change that touches rights read at a
recording sink — a membership, a role assigned and the union of the roles, a role changed recomputing every holder,
a user deactivated and reactivated, a compartment suspended, resumed and terminated —, in memory and replayed on
PostgreSQL under an ordinary role; the six stores against PostgreSQL, the `CHECK`s and the keys of the schema, a
role written whole or not at all, the actor kept on the rows, the installation and the verification; the routes
on the test server under their operations, each write read at the sink, the declared role kept, the actor of the
request on the row.

The PostgreSQL tests take the server named by `STELLAEGIS_TEST_POSTGRESQL`, and are skipped without it; the
publishing workflow runs them against a container, and fails on any test skipped.

## License

MIT — see [LICENSE](LICENSE). Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
