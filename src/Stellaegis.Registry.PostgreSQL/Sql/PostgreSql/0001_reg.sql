-- ============================================================================
-- Stellaegis.Registry — the registry schema, script 0001
-- ============================================================================
-- Applied by the installation of the application (DatabaseLifecycle.InstallAsync of
-- Stellaegis.Primitives.Data), under the role that owns the schema; the schema is created by the
-- installation before this script. The application connects under an ordinary role, granted its
-- data rights by the installation. The record of the scripts applied is the installation's,
-- reg._schema_migrations, not this script's.
--
-- Eleven tables: the compartments and the transfers of their ownership with the roles a transfer
-- keeps, the users (the projection of the accounts of Identity), the catalogue of the operations the
-- modules declare, the roles with their operations and the roles they let their holder assign, and
-- the two parallel models of the rights — the persons in the compartments (memberships,
-- member_roles) and the operators of the platform (operator_roles) — never mixed. The effective rights are not here:
-- the registry computes them from these tables and hands them to the decision point, Access.
--
-- No surrogate key: nothing joins these tables on anything but their natural keys. No row-level
-- security: the module has no compartment, it acts on the compartments. Foreign keys where they
-- protect: a role names operations of the catalogue, a member role names a membership and a role,
-- an operator role names a user and a role; a role still held is not deleted. All constraints
-- explicitly named.
--
-- Every write carries its actor: created_by and modified_by on the rows that are created once and
-- changed after, granted_by on the grants. Never defaulted — an actor forgotten is an error, not
-- "system" — and in the grammar of an actor of Primitives: 1 to 128 characters, no control
-- character, no surrounding whitespace. The timestamps go with them.
-- ============================================================================

-- ── Grammars ─────────────────────────────────────────────────────────────────
-- A subject is the sub of an account of Identity: a GUID written as 32 hex digits.
-- A compartment follows the grammar of Compartment: 3 to 63 lowercase letters, digits and single
-- hyphens, starting with a letter and ending with a letter or a digit.
-- A module name is 3 to 63 lowercase letters and digits, starting with a letter.
-- An operation is an identity target.name: each part 1 to 63 lowercase letters, digits and single
-- underscores, starting with a letter and ending with a letter or a digit.
-- A role name is an identifier: the grammar of one part of an operation.
-- An actor is 1 to 128 characters without control characters nor surrounding whitespace.
-- The checks below repeat those grammars so that a row written past the module — a script, a repair
-- by hand — cannot hold what the registry would never produce.

-- ── Compartments ─────────────────────────────────────────────────────────────
-- A compartment is never deleted and its name is never reused: terminated is a status, and the row
-- stays as a tombstone. Suspended keeps the memberships and roles; the rights are withdrawn until it
-- resumes. Its owner, the legal holder of the compartment, is absent while none was seated — a
-- compartment created for a designation —; seated, it changes by a transfer only.

create table reg.compartments (
  compartment   varchar(63)  not null,
  display_name  text         not null,
  status        varchar(10)  not null,
  created_at    timestamptz  not null,
  created_by    varchar(128) not null,
  modified_at   timestamptz  not null,
  modified_by   varchar(128) not null,
  owner_subject varchar(32),

  constraint pk_compartments primary key (compartment),
  constraint ck_compartments_compartment check (
    compartment ~ '^[a-z][a-z0-9-]{1,61}[a-z0-9]$' and compartment !~ '--'),
  constraint ck_compartments_display_name check (char_length(display_name) between 1 and 200),
  constraint ck_compartments_status check (status in ('active', 'suspended', 'terminated')),
  constraint ck_compartments_owner_subject check (owner_subject ~ '^[0-9a-f]{32}$'),
  constraint ck_compartments_created_by check (created_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint ck_compartments_modified_by check (modified_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$')
);

comment on table  reg.compartments              is 'The compartments of the application. One row per compartment, never deleted: a name is never reused.';
comment on column reg.compartments.compartment  is 'The name of the compartment, the one the frame and the routes use.';
comment on column reg.compartments.status       is 'active, suspended (rights withdrawn, memberships kept) or terminated (memberships gone, the row stays).';
comment on column reg.compartments.owner_subject is 'The subject of the owner, the legal holder of the compartment; null while none was seated. Changed by a transfer only.';
comment on column reg.compartments.created_by   is 'The actor who created the compartment: the installation, or an operator. Never defaulted.';
comment on column reg.compartments.modified_by  is 'The actor of the last change; the creator, until a change.';

-- ── Users ────────────────────────────────────────────────────────────────────
-- The projection of the accounts of Identity: who exists, to give a membership or a role to. The
-- registry authenticates nothing. Deactivated keeps the memberships and roles; the rights are withdrawn.

create table reg.users (
  subject       varchar(32)  not null,
  email         text         not null,
  display_name  text         not null,
  status        varchar(11)  not null,
  created_at    timestamptz  not null,
  created_by    varchar(128) not null,
  modified_at   timestamptz  not null,
  modified_by   varchar(128) not null,

  constraint pk_users primary key (subject),
  constraint ck_users_subject check (subject ~ '^[0-9a-f]{32}$'),
  constraint ck_users_email check (char_length(email) between 1 and 320),
  constraint ck_users_display_name check (char_length(display_name) between 1 and 200),
  constraint ck_users_status check (status in ('active', 'deactivated')),
  constraint ck_users_created_by check (created_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint ck_users_modified_by check (modified_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$')
);

comment on table  reg.users          is 'The projection of the accounts of Identity. One row per subject.';

alter table reg.compartments
  add constraint fk_compartments_owner foreign key (owner_subject) references reg.users (subject);
comment on column reg.users.subject  is 'The sub of the account: a GUID written as 32 hex digits.';
comment on column reg.users.status   is 'active, or deactivated: blocked or deleted in Identity, rights withdrawn, memberships kept.';
comment on column reg.users.created_by  is 'The actor who recorded the user: the person themselves when they registered, or the installation.';
comment on column reg.users.modified_by is 'The actor of the last change; the creator, until a change.';

-- ── Catalogue ────────────────────────────────────────────────────────────────
-- What can be granted: the operations the modules declare, recorded by the installation from the
-- declarations. Never seeded by hand, never removed by the installation: an operation a module no
-- longer declares stays grantable until someone decides otherwise.

create table reg.operations (
  operation  varchar(127) not null,
  module     varchar(63)  not null,
  kind       varchar(6)   not null,

  constraint pk_operations primary key (operation),
  constraint ck_operations_operation check (
    operation ~ '^[a-z]([a-z0-9_]{0,61}[a-z0-9])?\.[a-z]([a-z0-9_]{0,61}[a-z0-9])?$' and operation !~ '__'),
  constraint ck_operations_module check (module ~ '^[a-z][a-z0-9]{2,62}$'),
  constraint ck_operations_kind check (kind in ('read', 'write', 'delete'))
);

comment on table  reg.operations            is 'The catalogue of the operations the modules declare, recorded by the installation. One row per operation.';
comment on column reg.operations.operation  is 'The identity of the operation, target.name, as the module declares it.';
comment on column reg.operations.kind       is 'What the operation does to the data: read, write or delete.';

create index ix_operations_module on reg.operations (module);

-- ── Roles ────────────────────────────────────────────────────────────────────
-- A role is a name and a set of operations of the catalogue, in one scope: compartment — held by a
-- member, applicable in every compartment — or platform — held by an operator, in the shared space.
-- A declared role is one the application declares in code: the installation re-applies it, and it is
-- not deleted. Its category says what it is for, administration or business, never deduced from its
-- operations. It lets its holder assign the roles of its assignable list: compartment roles as a whole —
-- none, the business ones, all —, every platform role, and the roles named in role_assignable_roles. A
-- compartment role never lets its holder assign a platform role; a platform role always requires a
-- second factor.

create table reg.roles (
  role                    varchar(63)  not null,
  scope                   varchar(11)  not null,
  category                varchar(14)  not null,
  display_name            text         not null,
  declared                boolean      not null,
  assigns_compartment     varchar(8)   not null,
  assigns_platform        boolean      not null,
  requires_second_factor  boolean      not null,
  created_at    timestamptz  not null,
  created_by    varchar(128) not null,
  modified_at   timestamptz  not null,
  modified_by   varchar(128) not null,

  constraint pk_roles primary key (role),
  constraint ck_roles_role check (role ~ '^[a-z]([a-z0-9_]{0,61}[a-z0-9])?$' and role !~ '__'),
  constraint ck_roles_scope check (scope in ('compartment', 'platform')),
  constraint ck_roles_category check (category in ('administration', 'business')),
  constraint ck_roles_assigns_compartment check (assigns_compartment in ('none', 'business', 'all')),
  constraint ck_roles_assigns_platform check (scope = 'platform' or assigns_platform = false),
  constraint ck_roles_requires_second_factor check (scope = 'compartment' or requires_second_factor = true),
  constraint ck_roles_display_name check (char_length(display_name) between 1 and 200),
  constraint ck_roles_created_by check (created_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint ck_roles_modified_by check (modified_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$')
);

comment on table  reg.roles           is 'The roles: a name and a set of operations, in one scope. One row per role.';
comment on column reg.roles.scope     is 'compartment: held by a member in a compartment; platform: held by an operator of the platform.';
comment on column reg.roles.declared  is 'Whether the application declares the role in code: re-applied by the installation, never deleted.';
comment on column reg.roles.category  is 'What the role is for: administration (who holds what) or business (what the modules do).';
comment on column reg.roles.assigns_compartment is 'The compartment roles its holder may assign as a whole, those created later included: none, business, all.';
comment on column reg.roles.assigns_platform    is 'Whether its holder may assign every platform role; a platform role only.';
comment on column reg.roles.requires_second_factor is 'Whether its operations are granted only to a session that used a second factor; always for a platform role.';
comment on column reg.roles.created_by  is 'The actor who created the role: the installation for a declared role, an operator otherwise.';
comment on column reg.roles.modified_by is 'The actor of the last change; the creator, until a change.';

create table reg.role_operations (
  role       varchar(63)  not null,
  operation  varchar(127) not null,

  constraint pk_role_operations primary key (role, operation),
  constraint fk_role_operations_role foreign key (role) references reg.roles (role) on delete cascade,
  constraint fk_role_operations_operation foreign key (operation) references reg.operations (operation)
);

comment on table reg.role_operations is 'The operations of each role, each in the catalogue. One row per (role, operation).';

create index ix_role_operations_operation on reg.role_operations (operation);

create table reg.role_assignable_roles (
  role             varchar(63)  not null,
  assignable_role  varchar(63)  not null,

  constraint pk_role_assignable_roles primary key (role, assignable_role),
  constraint fk_role_assignable_roles_role foreign key (role) references reg.roles (role) on delete cascade,
  constraint fk_role_assignable_roles_assignable foreign key (assignable_role) references reg.roles (role)
);

comment on table reg.role_assignable_roles is 'The roles each role lets its holder assign by name, besides its selectors. One row per (role, assignable role); a role named here is not deleted.';

create index ix_role_assignable_roles_assignable on reg.role_assignable_roles (assignable_role);

-- ── Memberships, and the compartment roles of the members ────────────────────
-- The first parallel model: the persons in the compartments. A membership admits a person in a
-- compartment; the member roles say what she may do there. Removing the membership removes its roles.

create table reg.memberships (
  subject      varchar(32)  not null,
  compartment  varchar(63)  not null,
  granted_at   timestamptz  not null,
  granted_by   varchar(128) not null,

  constraint pk_memberships primary key (subject, compartment),
  constraint ck_memberships_granted_by check (granted_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint fk_memberships_subject foreign key (subject) references reg.users (subject),
  constraint fk_memberships_compartment foreign key (compartment) references reg.compartments (compartment)
);

comment on table reg.memberships is 'The compartments each person is a member of. One row per (subject, compartment).';
comment on column reg.memberships.granted_by is 'The actor who admitted the person in the compartment.';

create index ix_memberships_compartment on reg.memberships (compartment);

create table reg.member_roles (
  subject      varchar(32)  not null,
  compartment  varchar(63)  not null,
  role         varchar(63)  not null,
  granted_at   timestamptz  not null,
  granted_by   varchar(128) not null,

  constraint pk_member_roles primary key (subject, compartment, role),
  constraint ck_member_roles_granted_by check (granted_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint fk_member_roles_membership foreign key (subject, compartment) references reg.memberships (subject, compartment) on delete cascade,
  constraint fk_member_roles_role foreign key (role) references reg.roles (role)
);

comment on table reg.member_roles is 'The compartment roles of each member. One row per (subject, compartment, role); gone with the membership.';
comment on column reg.member_roles.granted_by is 'The actor who assigned the role.';

-- A role changed recomputes every holder: the holders of a role are read by role.
create index ix_member_roles_role on reg.member_roles (role);

-- ── Operator roles ───────────────────────────────────────────────────────────
-- The second parallel model: the operators of the platform. A user with any row here is an operator,
-- admitted in the shared space; the platform roles say what she may do there.

create table reg.operator_roles (
  subject     varchar(32)  not null,
  role        varchar(63)  not null,
  granted_at  timestamptz  not null,
  granted_by  varchar(128) not null,

  constraint pk_operator_roles primary key (subject, role),
  constraint ck_operator_roles_granted_by check (granted_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint fk_operator_roles_subject foreign key (subject) references reg.users (subject),
  constraint fk_operator_roles_role foreign key (role) references reg.roles (role)
);

comment on table reg.operator_roles is 'The platform roles of each operator. One row per (subject, role).';
comment on column reg.operator_roles.granted_by is 'The actor who assigned the role.';

create index ix_operator_roles_role on reg.operator_roles (role);

-- ── Transfers of ownership ───────────────────────────────────────────────────
-- The ownership is proposed by the owner and accepted by its recipient, a member, within its lifetime:
-- one transfer waiting per compartment, a new one replacing it. The outgoing owner keeps the roles
-- named in ownership_transfer_kept_roles; the others go with the ownership.

create table reg.ownership_transfers (
  compartment   varchar(63)  not null,
  from_subject  varchar(32)  not null,
  to_subject    varchar(32)  not null,
  proposed_at   timestamptz  not null,
  proposed_by   varchar(128) not null,
  expires_at    timestamptz  not null,

  constraint pk_ownership_transfers primary key (compartment),
  constraint ck_ownership_transfers_parties check (from_subject <> to_subject),
  constraint ck_ownership_transfers_expires_at check (expires_at > proposed_at),
  constraint ck_ownership_transfers_proposed_by check (proposed_by ~ '^[^[:space:][:cntrl:]]([^[:cntrl:]]{0,126}[^[:space:][:cntrl:]])?$'),
  constraint fk_ownership_transfers_compartment foreign key (compartment) references reg.compartments (compartment),
  constraint fk_ownership_transfers_from foreign key (from_subject) references reg.users (subject),
  constraint fk_ownership_transfers_to foreign key (to_subject) references reg.users (subject)
);

comment on table reg.ownership_transfers is 'The transfer of ownership waiting for each compartment: proposed by the owner, accepted or declined by its recipient. One row per compartment.';

create table reg.ownership_transfer_kept_roles (
  compartment  varchar(63)  not null,
  role         varchar(63)  not null,

  constraint pk_ownership_transfer_kept_roles primary key (compartment, role),
  constraint fk_ownership_transfer_kept_roles_transfer foreign key (compartment) references reg.ownership_transfers (compartment) on delete cascade,
  constraint fk_ownership_transfer_kept_roles_role foreign key (role) references reg.roles (role)
);

comment on table reg.ownership_transfer_kept_roles is 'The roles the outgoing owner keeps once the transfer is done. One row per (compartment, role); gone with the transfer.';
