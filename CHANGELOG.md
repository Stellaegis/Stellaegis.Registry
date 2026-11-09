# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [7.1.0] — 2026-11-09

On `Stellaegis.Primitives` 5.6.0, and a token naming a job is never the installation.

### Fixed

- **A token naming a job is never the installation.** The routes read the actor of a request from its claims with
  `Actor.TryParse`, which takes `job:…`: a token whose `sub` was `job:install`, let through by the host's policy, was
  the installation — a platform role assigned at once, without the act a second person validates, and no bound on
  the roles assigned. The actor is read with `Actor.TryParseClaim`: a token naming a job, or the anonymous actor of
  the frame, fails the request; the rule on a person refuses it.

### Changed

- **`Stellaegis.Primitives` 5.6.0**, and `Stellaegis.Registry.Identity` on `Stellaegis.Identity` 9.0.1. The example
  host takes Issuer 5.4.1 and Access 6.1.0.

### Updating from 7.0.0

Take the version. A host meets what the startup of Primitives 5.6.0 now refuses, listed with its way out in the
CHANGELOG of Primitives, § *Updating from 5.5.0*. A host whose services act as a job call the services in code, as the
installation does: a token never carries a job.

## [7.0.0] — 2026-11-09

The gestures on a person, reviewed after a host had to open the administration of the accounts wide to let a manager
invite a colleague. An operation names a gesture, never a write; a gesture on a person is decided on that person — by
the rule A13 to A15 already applied to the roles, extended to whom holds them —; a reset link handed over for an
active account waits for a second person; and the registry answers the ports of `Stellaegis.Identity` 9 through a
package of composition, so that a manager invites without holding anything over the accounts.

### Updating from 6.0.0

1. **Install again.** The script `0002_reg.sql` adds `reg.pending_acts.consumed_at` and the constraints of the kinds
   of acts, on both engines: the installation applies it, nothing is reinstalled. The verification at startup refuses
   the application until it has. The same run records the catalogue, which retires the former `*.write` operations
   of the modules it records, from the catalogue and from the roles that held them.
2. **Give the roles the gestures.** A declared role names the new operations in its `RoleDeclaration`, and the
   installation applies it again; a role created at runtime that held a `*.write` grants nothing by it any more, and
   is defined again — `roles.define`, an act that waits for its validation. A policy, a test or a client that names
   an operation by its string follows the renaming listed below.
3. **Compose with Identity 9**, when the host holds the accounts: reference `Stellaegis.Registry.Identity` and call
   `AddStellaegisRegistryIdentity()` after the accounts and the registry. Without it, Identity refuses a reset link
   handed over for an active account and keeps the page `/invitation` closed. `AddStellaegisRegistryAdministration()`
   registers the rule on a person: the routes of Identity that act on an account ask it, and refuse who cannot reach
   the account.
4. **Grant the managers the gestures they make**, not the administration of the accounts: inviting a colleague is
   `invitations.send`, blocking one `users.block` of Identity, decided on the person.
5. **A store of your own** implements `IPendingActStore.TryConsumeAsync` and `ICatalogStore.RetireAsync`, and keeps
   `PendingAct.ConsumedAt` and the kind `hand_delivery`.

### Changed

- **The operations name the gestures, and the `write` operations are gone.** `compartments.write` becomes
  `compartments.create`, `.rename`, `.suspend` — suspending and resuming — and `.terminate`, the seating of an owner
  `ownership.designate`; `directory.write` becomes `directory.deactivate`; `roles.write` `roles.create`, `.define`,
  `.delete`; `memberships.write` `memberships.add`, `.remove`, `.grant`, `.revoke`; `operators.write`
  `operators.grant`, `.revoke`; `invitations.write` `invitations.send`, `.cancel`; `ownership.write`, the transfer,
  `ownership.transfer`; `validations.write` `validations.decide`. A role that held one of the former grants nothing by it any more: the installation retires
  them.
- **Recording the catalogue retires what a module recorded no longer declares**, from the catalogue and from the
  roles that held it, whose holders are projected again. It removed nothing: an operation renamed stayed, granting
  nothing. Only the modules recorded are read, another host's left alone. `ICatalogStore.RetireAsync`.
- **A gesture on a person reaches whom the actor could have assigned every role**, never oneself, never the owner of a
  compartment: `IUserDirectory.ReachesAsync`, which `AddStellaegisRegistryAdministration()` registers as the
  resource-based authorization of a `Subject` — the deactivation of the directory asks it, and the routes of
  `Stellaegis.Identity` 9 that act on an account. A refusal fails the requirement, whatever another handler says.
  `POST users/{subject}/deactivate` and `…/reactivate` load the person; who may deactivate a colleague no longer
  deactivates the administrator.
- **A reset link handed over for an active account waits for a second person**: the act `hand_delivery`,
  `IValidations.ProposeHandDeliveryAsync` and `TryConsumeHandDeliveryAsync` — the author and the validator on screen
  must both reach the person, the act spent once. By the script `0002`, `reg.pending_acts.consumed_at` and the
  constraints of its kinds, on both engines: an installation takes it by running again. `PendingAct.ConsumedAt`,
  `IPendingActStore.TryConsumeAsync`.
- **Stellaegis.Primitives 5.5.0**, for `Subject`; Microsoft.Extensions 10.0.14; Stellaegis.Identity 9.0.0 for the
  example and the composition.

### Added

- **`Stellaegis.Registry.Identity`, a package of composition**: the ports `Stellaegis.Identity` 9 declares, answered
  by the registry — `IHandDeliveryApprovals` by the act `hand_delivery`, `IInvitationAcceptance` by the invitations,
  the account recorded in the directory before it accepts. `AddStellaegisRegistryIdentity()`.
  The example composes them; its own operation `items.write` is `items.add`.


## [6.0.0] — 2026-10-23

Who may grant what, reviewed for its security, and the stores on SQL Server. A role now says what its holder may
assign, and nobody grants beyond it nor to themselves; a compartment has an owner nobody else touches; an invitation
gives a membership to someone who has no account yet; the acts that grant too much for one stolen session wait for a
second channel; and whether an operation requires a second factor is the operation's, carried to the decision point
with each grant.

### Migrating from 5.x

1. **Reinstall the schema.** `0001_reg.sql` is corrected in place — no database held real data yet: drop the schema
   `reg` (`drop schema reg cascade`) and run the installation again. The verification at startup refuses a schema
   installed from the 5.x script, and says so.
2. **Adapt the sink.** `IRightsSink.ReplaceUserGrantsAsync` and `ReplaceAdminGrantsAsync` take
   `IReadOnlyCollection<OperationGrant>`, the operation and whether it requires a second factor; Access 6.0.0 takes
   them as `AccessGrant`s, `grants.Select(grant => new AccessGrant(grant.Operation, grant.RequiresSecondFactor))`.
   Every operation of the shared space requires one: an operator signs in with a second factor.
3. **Give the declared roles a category**, and the roles their holder assigns: `RoleDeclaration(name, scope,
   category, displayName, operations, assignable)`. A role whose list is empty lets its holder assign nothing — an
   administration role that assigned in 5.x needs its `AssignableRoles`. Declare the operations of `owner`, the role
   the registry reserves, if the owner is to hold any.
4. **Seat the owner at installation**, when the host knows it: `ICompartments.CreateAsync(compartment, displayName,
   owner, actor)` creates the compartment and seats its owner at once; otherwise the compartment waits for a
   designation. The owner holds `owner`; a host that gave its first administrator an administration role of the
   compartment gives them the ownership instead.
5. **Expect a sensitive act to wait.** `IRoles.CreateAsync` and `UpdateAsync` take a `RoleDefinition` and answer an
   `ActOutcome<Role>`; `IOperators.AssignAsync` answers the `PendingAct` when it waits. Their routes answer 202 with
   the act. Give the host a `validate` verb that calls `IValidations.ValidateAsync(actId, actor,
   ValidationChannel.Console)` under the administration connection, the actor the system account that runs it, or
   let a second person holding `validations.write` validate on screen. The installation's acts wait for nothing.
6. **Declare the sensitive operations** on their `Operation` (`requiresSecondFactor: true`, Primitives 5.3.0); harden
   one a module left open with `ICatalog.RequireSecondFactorAsync`, under a job.
7. **If the invitations are mapped** (`MapRegistryInvitationEndpoints`), register an `IInvitationSender` or declare
   `Stellaegis:Registry:Invitations:HandDelivery`, and schedule `IInvitations.PurgeAsync(RegistryJobs.Purge)`.
8. **A store of your own** implements the new members of the store interfaces — the ownership and its transfer on
   `ICompartmentStore`, `IUserStore.FindByEmailAsync`, `ICatalogStore.HardenAsync` —, `IInvitationStore` and
   `IPendingActStore`, and keeps what `Role`, `CompartmentRecord` and `CatalogEntry` now carry: the category and the
   assignable roles, the owner, the second factor.

### Added

- `Stellaegis.Registry.SqlServer`: the stores and the schema `reg` on SQL Server, the same tables and rules as on
  PostgreSQL, registered by `AddStellaegisRegistrySqlServer` from `Stellaegis:Registry:SqlServer`.
- The category of a role, `administration` or `business`, and `AssignableRoles`: the compartment roles of a category,
  every platform role, roles by name.
- The owner: `IOwnership` — seat, transfer proposed and accepted within seven days, cancel, rescue —, the role
  `owner`, the operations `ownership.write` and `ownership.rescue`, the routes under `compartments/{compartment}/owner`,
  `OwnershipChangedEvent`.
- The invitations: `IInvitations`, `IInvitationSender`, the options `Stellaegis:Registry:Invitations`,
  `MapRegistryInvitationEndpoints`, the operation `invitations.write`, `InvitationAcceptedEvent`.
- The validations: `IValidations`, `ValidationChannel`, `PendingAct`, the routes under `validations`, the operation
  `validations.write`, `ActProposedEvent` and `PendingActClosedEvent`.
- The second factor in the catalogue, `CatalogEntry.DeclaredSecondFactor` and `HardenedSecondFactor`, and on each
  grant, `OperationGrant`.

### Changed

- A person assigns and removes only the roles the lists of the roles they hold cover, never to themselves, and never
  changes the definition of a role they hold — refused with `Role.NotAssignable`, `Role.OwnRoles`,
  `Role.HeldByAuthor`. The installation, a job, is bounded by none of this.
- In the shared space every operation requires a second factor.
- The packages take `Stellaegis.Primitives` 5.3.0.

## [5.0.1] — 2026-10-21

### Fixed

- The readme the packages embed: the 5.0.0 packages carry the draft readme of the repository — "being extracted,
  nothing is published yet" —, the tag having been pushed before the readme was written. Nothing else changes;
  5.0.0 is to be unlisted.

## [5.0.0] — 2026-10-21

First release of the packages: the enterprise subset of the registry of the Stellaegis framework, extracted and
rewritten on `Stellaegis.Primitives` 5.1. They start at the major version of the family. The registry gives a right
its meaning — compartments, users, the catalogue of operations, roles, memberships, operators — and computes the
effective rights it hands to the decision point, `Stellaegis.Access`, through a port the host implements. It knows
neither accounts, nor tokens, nor decisions, and depends on Primitives alone.

### What it keeps from the framework

The two parallel models — the people in the compartments, the operators of the platform —, never mixed; the roles
as named sets of operations, declared by the application for the defaults and administered by the routes; the
projection of flat effective rights to the decision point, which never computes them; a compartment never deleted,
its name never reused; the users as a projection of the accounts.

### What changes for who comes from `Stellaegis.Registry` of the framework

- **The catalogue comes from the declarations.** `ICatalog.RecordAsync(GetStellaegisModules())` at installation
  replaces every permission seeder; an operation is what Primitives calls one, `target.name`, with its kind, and
  has no scope in the catalogue — the scope is where it is granted. The registry's own operations on the directory
  of users are `directory.read` and `directory.write`: an operation's identity is global in a host, and
  `users.read` is Identity's.
- **The projection is a port, `IRightsSink`**, called synchronously after every change, with the five methods of
  the projection of Access; no `PermissionChangedEvent` with a snapshot. The events say that something changed.
- **Roles have two scopes and one table**: `compartment` roles, the role templates of the framework, applicable in
  every compartment; `platform` roles, its admin roles. A role name is an identifier: `tenant-administrator` is
  `tenant_administrator`. Declared roles are re-applied by the installation and never deleted.
- **Memberships and member roles replace `memberships`, `membership_roles` and `user_permissions`**; the effective
  rights are not stored here. `operator_roles` replaces `admin_user_roles` and `admin_user_permissions`.
- **Compartments replace tenants**: a name in the grammar of `Compartment`, a display name, a status; the contact
  and billing columns, `apps`, `plans`, `subscriptions`, `invitations`, `tenant_oidc_clients`, the system tenants
  and every seeder are gone — what is the SaaS's waits for `Stellaegis.Tenancy`.
- **Every write names its actor**, kept on the row and carried by the event, never defaulted.
- **No surrogate key**: the tables are keyed on their natural keys; a subject is the `sub` of an account.
- **The routes act from the shared space**, each under an operation of the module, decided by the host's rights
  system; no policy `admin:*`, no `apiCode`; the states travel as words.
- **The provisioning of the client of an API is not the registry's**: the installation calls the port of the
  issuer.

### Fixed, from the framework

- A role assigned to someone who is not a member is refused, by the service and by the schema.
- A role still held cannot be deleted, by the service and by the schema.
