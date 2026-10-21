# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
