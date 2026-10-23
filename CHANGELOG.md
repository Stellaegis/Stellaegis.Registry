# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
