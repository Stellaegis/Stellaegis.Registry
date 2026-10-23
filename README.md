# Stellaegis.Registry

Stellaegis.Registry is the registry of a .NET application assembled on
[Stellaegis.Primitives](https://github.com/Stellaegis/Stellaegis.Primitives): what gives a right its meaning. It
holds the compartments and their owners, the users, the catalogue of the operations the modules declare, the roles
that group them, the memberships of the people in the compartments, the roles of the operators of the platform, the
invitations, and the sensitive acts waiting for a second channel. From those it computes the effective rights of a
person in a space, and hands them — synchronously, after every change — to the decision point,
[Stellaegis.Access](https://github.com/Stellaegis/Stellaegis.Access), through a port the host implements in five
lines.

It knows neither accounts (`Stellaegis.Identity`, whose registered users it records), nor tokens
(`Stellaegis.Issuer`), nor decisions (`Stellaegis.Access`). It is the enterprise subset of a registry: one
compartment or several, no onboarding, no provisioning, no tenant portal — those belong to the package of the SaaS,
to come. It depends on Primitives alone; a host composes it with the three others.

The library targets .NET 10 and is released under the MIT license.

## Packages

| Package | Contents | Depends on |
| --- | --- | --- |
| `Stellaegis.Registry` | The compartments and their owners, the users, the catalogue, the roles, the memberships, the operators, the invitations, the validations; the effective rights and the `IRightsSink` port; the module and its operations; the role declarations; the in-memory stores | `Stellaegis.Primitives`, `Stellaegis.Primitives.Hosting` |
| `Stellaegis.Registry.PostgreSQL` | The stores on PostgreSQL, and the `reg` schema declared as a module database for the installation and the verification | `Stellaegis.Primitives.Data`, Npgsql, Dapper |
| `Stellaegis.Registry.SqlServer` | The same stores and the same `reg` schema on SQL Server | `Stellaegis.Primitives.Data`, Microsoft.Data.SqlClient, Dapper |
| `Stellaegis.Registry.AspNetCore` | The administration routes, each under its operation, decided in the shared space; the invitation routes | `Stellaegis.Primitives.AspNetCore`, ASP.NET Core |

The four packages share one version. They started at the major version of Stellaegis.Primitives, 5; 6.0.0 runs on
Primitives 5.3.0.

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

The adapter, the two ports having the same shape on purpose. Each grant says whether its operation requires a
second factor, and Access, reading the `amr` of the session, refuses it without one:

```csharp
public sealed class AccessRightsSink(IAccessProjection projection) : IRightsSink
{
    public Task ReplaceMembershipsAsync(string subject, IReadOnlyCollection<Compartment> compartments, CancellationToken ct = default)
        => projection.ReplaceMembershipsAsync(subject, compartments, ct);
    public Task ReplaceUserGrantsAsync(string subject, Compartment compartment, IReadOnlyCollection<OperationGrant> grants, CancellationToken ct = default)
        => projection.ReplaceUserGrantsAsync(subject, compartment, grants.Select(grant => new AccessGrant(grant.Operation, grant.RequiresSecondFactor)).ToList(), ct);
    public Task ReplaceAdminGrantsAsync(string subject, IReadOnlyCollection<OperationGrant> grants, CancellationToken ct = default)
        => projection.ReplaceAdminGrantsAsync(subject, grants.Select(grant => new AccessGrant(grant.Operation, grant.RequiresSecondFactor)).ToList(), ct);
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
   `items.write`, and whether one requires a second factor —, and the application declares the roles it ships
   with, `RoleDeclaration`: a name, a scope, a category, a display name, a set of operations, the roles it lets its
   holder assign.
2. **Install**, apart from serving. The installation records the catalogue from what the modules declare,
   `ICatalog.RecordAsync(services.GetStellaegisModules())` — nothing is seeded by hand —, re-applies the declared
   roles, `IRoles.DeclareAsync`, and records the first administrator with their roles.
3. **Grant**, by the routes: roles created or changed, memberships, assignments, invitations — each bounded by the
   roles of whoever grants, the sensitive ones waiting for a second channel.
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
        // The operations of the owner of a compartment: a role the registry reserves, seated with the ownership.
        new(RegistryRoles.Owner, RoleScope.Compartment, RoleCategory.Administration, "Propriétaire du compartiment",
            [InventoryOperations.Read, InventoryOperations.Write]),
        new("shop_manager", RoleScope.Compartment, RoleCategory.Business, "Chef d'atelier",
            [InventoryOperations.Read, InventoryOperations.Write], AssignableRoles.Of("operator")),
        new("operator", RoleScope.Compartment, RoleCategory.Business, "Opérateur", [InventoryOperations.Read]),
        new("super_admin", RoleScope.Platform, RoleCategory.Administration, "Administrateur de la plateforme",
            modules.SelectMany(module => module.Operations).DistinctBy(operation => operation.Identity).ToList(),
            AssignableRoles.Every(CompartmentRoleSelector.All, allPlatform: true)),
    ];
}

// At installation, under the actor of the installation — an enterprise host, its one compartment owned by its
// first administrator:
var installer = Actor.Job("install");
var modules = provider.GetStellaegisModules();
await catalog.RecordAsync(modules);
await roles.DeclareAsync(SampleRoles.Declarations(modules), installer);
await directory.RecordAsync(subject, email, displayName, installer);
await compartments.CreateAsync(Compartment.Parse("acme"), "Acme SA", subject, installer);   // created, its owner seated
await operators.AssignAsync(subject, "super_admin", installer);
```

The compartment created with its owner is the gesture of a host whose owner is known at installation. A host of
several compartments installs its operators only; each compartment is created later without an owner,
`ICompartments.CreateAsync(compartment, displayName, actor)`, and its owner is seated by a designation — an
invitation that designates the owner, or `IOwnership.SeatAsync`.

A declared role is created when missing and reset — display name, category, operations, assignable roles — when
present: an edit by hand is put back by the next installation, and a declared role is never deleted. A role created
by hand is never touched by a declaration, and a declaration whose name is taken by one is refused. The catalogue
adds and updates the kind of an operation, and removes nothing: an operation a module no longer declares stays
grantable until someone decides otherwise.

## What the registry holds

Two parallel models, never mixed — the people in the compartments, and the operators of the platform —, the
registry's own invariant since its first version:

| Table of `reg` | Key | What it is |
| --- | --- | --- |
| `compartments` | `compartment` | the compartments and their owner; `active`, `suspended` or `terminated` — never deleted, a name is never reused |
| `users` | `subject` | the projection of the accounts of Identity: the `sub`, the e-mail, the display name; `active` or `deactivated` |
| `operations` | `operation` | the catalogue: the identity `target.name`, the module, the kind, whether a second factor is declared or hardened |
| `roles`, `role_operations`, `role_assignable_roles` | `role` | a name, a scope (`compartment` or `platform`), a category (`administration` or `business`), a display name, whether declared, its operations — each in the catalogue —, the roles it lets its holder assign |
| `memberships` | `(subject, compartment)` | the compartments each person is a member of |
| `member_roles` | `(subject, compartment, role)` | the compartment roles of each member; gone with the membership |
| `operator_roles` | `(subject, role)` | the platform roles of each operator |
| `ownership_transfers`, `ownership_transfer_kept_roles` | `compartment` | a transfer of ownership waiting for its recipient, and the roles the outgoing owner keeps |
| `invitations`, `invitation_roles` | `invitation_id` | the invitations, their token kept as a SHA-256 hash, the roles they give |
| `pending_acts` | `act_id` | the sensitive acts waiting for their validation, validated, rejected or lapsed |

No surrogate key — nothing joins these tables on anything but their natural keys, the invitation and the act being
the two things that have no other name than the identifier drawn for them —, no row-level security — the module has
no compartment, it acts on them —, foreign keys where they protect: a role still held is not deleted, a role names
operations of the catalogue, a member role names a membership. The grammars are repeated as `CHECK`s, so that a row
written past the module cannot hold what the registry would never produce. The SQL Server schema has the same
tables, names and rules; its identifiers carry a binary collation, so that it compares them exactly whatever the
collation of the database.

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

### The second factor goes with the operation

Whether an operation requires a second factor is the operation's, never the role's: `items.purge` is sensitive
whichever role grants it, `items.read` is not because a powerful role also grants it. The module declares it on its
`Operation` (`requiresSecondFactor: true`), the catalogue records it, and the installation may harden an operation
the module left open — `ICatalog.RequireSecondFactorAsync`, under a job — and never weaken one. Each grant the sink
receives carries the requirement, `OperationGrant(Operation, RequiresSecondFactor)`; in the shared space every
operation requires one.

### Who assigns what

A role says which roles its holder may assign: `AssignableRoles` — the compartment roles of a category (`none`,
`business` or `all`), every platform role or none, and roles by name. A person assigns, and removes, only the roles
the lists of the roles they hold cover — in the compartment for a compartment role, on the platform for a platform
role —, never to themselves, and never changes the definition of a role they hold. The installation, a job, is
bounded by none of this.

### The owner

A compartment has at most one owner, its legal holder, carried by the compartment. The owner is its first member
and holds the role the registry reserves, `owner`: an administration role whose list covers every compartment role,
whose operations the application declares. Nobody else touches the owner's membership or roles there, the operators
included; the owner gives the ownership by a transfer — proposed, accepted by its recipient within seven days,
everything checked again at the acceptance, applied at once, the outgoing owner keeping the roles they chose. A
compartment whose owner is unreachable is rescued by the platform, `ownership.rescue`, an act that waits for a
validation.

### The invitations

An invitation gives a membership and roles to an address that has no account yet, or that is not a member: issued
under the lists of its author — never `owner`, never to the author's own address, never to a member —, its token
drawn at random and kept as a hash, sent by the host's `IInvitationSender` or handed over when the host declares it.
A newer invitation to the same address and compartment revokes those waiting. Accepted all or nothing, everything
checked again, by an account whose address is the one invited and verified; the rights are granted by the author.
An operator whose roles cover every compartment role may invite the owner of a compartment that has none. The
acceptance is no route of the administration: the pages of the accounts call `IInvitations` in process
(`Stellaegis.Identity` 6.1.0). The invitations never accepted are purged after their retention,
`IInvitations.PurgeAsync(RegistryJobs.Purge)`, from the host's scheduler.

### The acts that wait for a validation

Some acts grant too much for one stolen session: assigning a platform role — whoever holds the platform holds every
compartment —, creating a role or changing what it grants (not its display name), rescuing a compartment. Proposed
by a person, they wait seven days in `reg.pending_acts` with no effect anywhere, and the route answers 202 with the
act. Two channels validate one:

- **the screen**: another person holding `validations.write`, whose lists cover the platform role and who does not
  hold the role redefined — never the author;
- **the console**: the `validate` verb of the host, the same executable in a process apart, under the
  administration connection of the database, the actor the system account that runs it,
  `IValidations.ValidateAsync(actId, actor, ValidationChannel.Console)`. Whoever stole a web access asks, and the act
  waits; the console may validate the act of the only operator.

The act is checked again when validated, and applied as if its author had done it then. Removing a platform role is
immediate. The installation's acts wait for nothing.

## Installing, then verifying

`Stellaegis.Registry.PostgreSQL` and `Stellaegis.Registry.SqlServer` declare their database,
`RegistryDatabase.Value`: the schema `reg` and its scripts, embedded in the package. An installer installs it under
an administration role, which owns the schema and grants the application's role its rights; a host verifies it at
startup under the application's role, and refuses to start on a schema missing or a script changed:

```csharp
// The installation, apart from the application.
await DatabaseLifecycle.InstallAsync([RegistryDatabase.Value], frame, openAsAdministrator, applicationRole, Actor.Job("install"));

// The host.
builder.Services.AddStellaegisRegistryPostgreSQL(builder.Configuration);   // Stellaegis:Registry:PostgreSQL:ConnectionString
// or AddStellaegisRegistrySqlServer(builder.Configuration);               // Stellaegis:Registry:SqlServer:ConnectionString
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
| `GET compartments`, `GET compartments/{compartment}`, `GET compartments/{compartment}/owner/transfer` | `compartments.read` |
| `POST compartments` (201), `PUT compartments/{compartment}` (the display name), `POST compartments/{compartment}/suspend`, `…/resume`, `…/terminate`, `PUT compartments/{compartment}/owner/{subject}` (seats the owner of a compartment that has none) | `compartments.write` |
| `PUT compartments/{compartment}/owner/transfer`, `POST …/owner/transfer/accept`, `DELETE …/owner/transfer` | `ownership.write` |
| `PUT compartments/{compartment}/owner/rescue/{subject}` (202) | `ownership.rescue` |
| `GET users`, `GET users/{subject}` — no creation: a user is the projection of an account of Identity | `directory.read` |
| `POST users/{subject}/deactivate`, `…/reactivate` | `directory.write` |
| `GET catalog`, `GET catalog/{module}` | `catalog.read` |
| `GET roles?scope=`, `GET roles/{role}` | `roles.read` |
| `POST roles` (202, the act waiting), `PUT roles/{role}` (200 for the display name alone, 202 otherwise), `DELETE roles/{role}` — a declared role is changed, never deleted | `roles.write` |
| `GET memberships/compartments/{compartment}`, `GET memberships/users/{subject}` | `memberships.read` |
| `PUT memberships/{compartment}/{subject}`, `DELETE …`, `PUT …/roles/{role}`, `DELETE …/roles/{role}` | `memberships.write` |
| `GET operators`, `GET operators/{subject}`, `GET validations` | `operators.read` |
| `PUT operators/{subject}/roles/{role}` (202), `DELETE …` | `operators.write` |
| `POST validations/{id}/validate`, `POST validations/{id}/reject` | `validations.write` |

`MapRegistryInvitationEndpoints()` maps the invitations apart, and refuses a host that could deliver none — no
`IInvitationSender`, no `HandDelivery` declared — at startup:

| Routes | Operation |
| --- | --- |
| `POST compartments/{compartment}/invitations` (201; the token in the answer when handed over) | `invitations.write` |
| `GET compartments/{compartment}/invitations` | `memberships.read` |
| `DELETE invitations/{id}` — its author, the owner of its compartment, or anyone who could issue it | `invitations.write` |

A refusal of a service is answered as problem details with its code:

| Status | Codes |
| --- | --- |
| 400 | `Role.ScopeMismatch`, `Role.NameInvalid`, `Role.AssignsPlatform`, `Role.NotHeld`, `Transfer.ToSelf`, `Invitation.EmailInvalid`, `Catalog.UnknownOperation`; a malformed name, `Registry.Invalid` |
| 403 | `Role.HeldByAuthor`, `Role.NotAssignable`, `Role.OwnRoles`, `Role.Reserved`, `Owner.Untouchable`, `Owner.NotOwner`, `Transfer.NotRecipient`, `Invitation.HandDeliveryClosed`, `Act.Own`, `Act.NotValidator`, `Catalog.InstallationOnly` |
| 404 | `Compartment.NotFound`, `User.NotFound`, `Role.NotFound`, `Transfer.NotFound`, `Invitation.NotFound`, `Invitation.Invalid`, `Act.NotFound` |
| 409 | `Compartment.Exists`, `Role.Exists`, `Role.NotDeclared`, `Owner.Seated`, `Invitation.AlreadyMember`, `Act.Pending` |
| 412 | `Compartment.Terminated`, `Compartment.NotActive`, `User.Deactivated`, `Role.Declared`, `Role.InUse`, `Role.NamedAssignable`, `Membership.NotMember`, `Transfer.Expired`, `Transfer.Stale`, `Invitation.NoSender`, `Invitation.NotPending`, `Invitation.Stale`, `Act.Closed`, `Act.Expired` |

The states, the scopes and the kinds travel as words, `active`, `suspended`, `terminated`, `deactivated`,
`compartment`, `platform`, `administration`, `business`, `platform_role`, `role_definition`, `ownership_rescue`.

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

## Every write names its actor

Every write of the registry carries who made it and when: `created_by` and `modified_by` on the compartments, the
users and the roles, `granted_by` on the memberships, the member roles and the operator roles, `proposed_by`,
`validated_by` and `rejected_by` on the acts, `issued_by` and `revoked_by` on the invitations — never defaulted, an
actor forgotten being an error rather than "system". The services take the `Actor` on every write, the rows keep
it, the summaries of the routes answer it, the events carry it (`By`). The routes pass the principal the host
authenticated; the installation names itself, `job:install`; a user recorded from their registration is recorded
by themselves; a right applied after a validation or an acceptance is granted by whoever proposed or invited.

## Events

`CompartmentChangedEvent`, `OwnershipChangedEvent`, `UserChangedEvent`, `RoleChangedEvent`,
`MembershipChangedEvent`, `InvitationAcceptedEvent`, `ActProposedEvent`, `PendingActClosedEvent` — each with who
made the change —, and `RightsChangedEvent` after the effective rights of a subject in a space were recomputed and
handed to the sink. It carries no snapshot: the rights went through the port, deterministically; the event only
says that they changed, for a cache of decisions apart to invalidate. Published through the `IEventPublisher` the
host registers, or nowhere.

## Options

| Section | Options |
| --- | --- |
| `Stellaegis:Registry:PostgreSQL` or `Stellaegis:Registry:SqlServer` | `ConnectionString`, under the application's role |
| `Stellaegis:Registry:Invitations` | `Lifetime` (7 days), `HandDelivery` (`false`: an invitation is sent, never handed over), `Retention` (30 days, never less) |

Validated when the host starts.

## Tests

`tests/Stellaegis.Registry.Tests`: the catalogue from the declarations and the second factor it keeps, the declared
roles re-applied without touching a role made by hand; who assigns what, the owner and the transfer, the
invitations, the acts that wait and their two channels; every change that touches rights read at a recording
sink, in memory and replayed on each engine under the application's role; the stores against PostgreSQL and SQL
Server, the `CHECK`s and the keys of each schema, a role written whole or not at all, the actor kept on the rows,
the installation and the verification; the routes on the test server under their operations.

The database tests take the servers named by `STELLAEGIS_TEST_POSTGRESQL` and `STELLAEGIS_TEST_SQLSERVER`, and are
skipped without them. On SQL Server they create their database and drop it, and run as a user without login, by
`EXECUTE AS USER`, so that an instance accepting integrated security only runs them. The publishing workflow runs
them against two containers, and fails on any test skipped.

## License

MIT — see [LICENSE](LICENSE). Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
