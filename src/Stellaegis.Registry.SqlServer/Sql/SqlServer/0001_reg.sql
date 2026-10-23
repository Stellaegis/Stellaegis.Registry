-- ============================================================================
-- Stellaegis.Registry — the registry schema on SQL Server, script 0001
-- ============================================================================
-- Applied by the installation of the application (DatabaseLifecycle.InstallAsync of
-- Stellaegis.Primitives.Data), under a login that owns the schema; the schema is created by the
-- installation before this script. The application connects as a member of the database role the
-- installation grants its data rights. The record of the scripts applied is the installation's,
-- reg._schema_migrations, not this script's.
--
-- The same fourteen tables as the PostgreSQL script of Stellaegis.Registry.PostgreSQL, the same names,
-- the same rules. What differs, and why:
--   * every identifier — a compartment, a subject, an operation, a module, a role, a hash — carries a
--     binary collation: a database of SQL Server is usually case-insensitive, and the registry
--     compares them exactly, as PostgreSQL does; the keys referring to them carry it too;
--   * the grammars PostgreSQL checks by a regular expression are checked here by LIKE patterns under
--     that binary collation, an underscore written [_], since it is a wildcard;
--   * the definition proposed for a role is JSON in an nvarchar(max), checked by isjson;
--   * every table is stored in the order of its key, clustered; none is a heap.
--
-- No row-level security: the module has no compartment, it acts on the compartments.
-- All constraints explicitly named.
-- ============================================================================

-- ── Compartments ─────────────────────────────────────────────────────────────

create table reg.compartments (
  compartment   nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  display_name  nvarchar(200)     not null,
  status        nvarchar(10)      not null,
  created_at    datetimeoffset(3) not null,
  created_by    nvarchar(128)     not null,
  modified_at   datetimeoffset(3) not null,
  modified_by   nvarchar(128)     not null,
  owner_subject nvarchar(32)  collate Latin1_General_100_BIN2 null,

  constraint pk_compartments primary key clustered (compartment),
  constraint ck_compartments_compartment check (
    len(compartment) between 3 and 63 and compartment like N'[a-z]%' and compartment not like N'%[^a-z0-9-]%'
    and compartment not like N'%-' and compartment not like N'%--%'),
  constraint ck_compartments_display_name check (len(display_name) >= 1),
  constraint ck_compartments_status check (status collate Latin1_General_100_BIN2 in (N'active', N'suspended', N'terminated')),
  constraint ck_compartments_owner_subject check (len(owner_subject) = 32 and owner_subject not like N'%[^0-9a-f]%'),
  constraint ck_compartments_created_by check (len(created_by) between 1 and 128 and datalength(created_by) = 2 * len(created_by) and created_by not like N' %' and created_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_compartments_modified_by check (len(modified_by) between 1 and 128 and datalength(modified_by) = 2 * len(modified_by) and modified_by not like N' %' and modified_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%')
);

-- ── Users ────────────────────────────────────────────────────────────────────

create table reg.users (
  subject       nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  email         nvarchar(320)     not null,
  display_name  nvarchar(200)     not null,
  status        nvarchar(11)      not null,
  created_at    datetimeoffset(3) not null,
  created_by    nvarchar(128)     not null,
  modified_at   datetimeoffset(3) not null,
  modified_by   nvarchar(128)     not null,

  constraint pk_users primary key clustered (subject),
  constraint ck_users_subject check (len(subject) = 32 and subject not like N'%[^0-9a-f]%'),
  constraint ck_users_email check (len(email) >= 1),
  constraint ck_users_display_name check (len(display_name) >= 1),
  constraint ck_users_status check (status collate Latin1_General_100_BIN2 in (N'active', N'deactivated')),
  constraint ck_users_created_by check (len(created_by) between 1 and 128 and datalength(created_by) = 2 * len(created_by) and created_by not like N' %' and created_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_users_modified_by check (len(modified_by) between 1 and 128 and datalength(modified_by) = 2 * len(modified_by) and modified_by not like N' %' and modified_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%')
);

alter table reg.compartments
  add constraint fk_compartments_owner foreign key (owner_subject) references reg.users (subject);

-- The address of an invitation finds whether it belongs to a user already: the collation of the
-- database compares it without case.
create index ix_users_email on reg.users (email);

-- ── Catalogue ────────────────────────────────────────────────────────────────

create table reg.operations (
  operation               nvarchar(127) collate Latin1_General_100_BIN2 not null,
  module                  nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  kind                    nvarchar(6)   not null,
  declared_second_factor  bit           not null,
  hardened_second_factor  bit           not null,

  constraint pk_operations primary key clustered (operation),
  constraint ck_operations_operation check (
    operation like N'[a-z]%.[a-z]%' and operation not like N'%.%.%' and operation not like N'%[^a-z0-9_.]%'
    and operation not like N'%[_]' and operation not like N'%[_].%' and operation not like N'%[_][_]%'
    and charindex(N'.', operation) between 2 and 64 and len(operation) - charindex(N'.', operation) between 1 and 63),
  constraint ck_operations_module check (len(module) between 3 and 63 and module like N'[a-z]%' and module not like N'%[^a-z0-9]%'),
  constraint ck_operations_kind check (kind collate Latin1_General_100_BIN2 in (N'read', N'write', N'delete'))
);

create index ix_operations_module on reg.operations (module);

-- ── Roles ────────────────────────────────────────────────────────────────────

create table reg.roles (
  role                 nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  scope                nvarchar(11)      not null,
  category             nvarchar(14)      not null,
  display_name         nvarchar(200)     not null,
  declared             bit               not null,
  assigns_compartment  nvarchar(8)       not null,
  assigns_platform     bit               not null,
  created_at           datetimeoffset(3) not null,
  created_by           nvarchar(128)     not null,
  modified_at          datetimeoffset(3) not null,
  modified_by          nvarchar(128)     not null,

  constraint pk_roles primary key clustered (role),
  constraint ck_roles_role check (role like N'[a-z]%' and role not like N'%[^a-z0-9_]%' and role not like N'%[_]' and role not like N'%[_][_]%'),
  constraint ck_roles_scope check (scope collate Latin1_General_100_BIN2 in (N'compartment', N'platform')),
  constraint ck_roles_category check (category collate Latin1_General_100_BIN2 in (N'administration', N'business')),
  constraint ck_roles_assigns_compartment check (assigns_compartment collate Latin1_General_100_BIN2 in (N'none', N'business', N'all')),
  constraint ck_roles_assigns_platform check (scope collate Latin1_General_100_BIN2 = N'platform' or assigns_platform = 0),
  constraint ck_roles_display_name check (len(display_name) >= 1),
  constraint ck_roles_created_by check (len(created_by) between 1 and 128 and datalength(created_by) = 2 * len(created_by) and created_by not like N' %' and created_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_roles_modified_by check (len(modified_by) between 1 and 128 and datalength(modified_by) = 2 * len(modified_by) and modified_by not like N' %' and modified_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%')
);

create table reg.role_operations (
  role       nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  operation  nvarchar(127) collate Latin1_General_100_BIN2 not null,

  constraint pk_role_operations primary key clustered (role, operation),
  constraint fk_role_operations_role foreign key (role) references reg.roles (role) on delete cascade,
  constraint fk_role_operations_operation foreign key (operation) references reg.operations (operation)
);

create index ix_role_operations_operation on reg.role_operations (operation);

create table reg.role_assignable_roles (
  role             nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  assignable_role  nvarchar(63)  collate Latin1_General_100_BIN2 not null,

  constraint pk_role_assignable_roles primary key clustered (role, assignable_role),
  constraint fk_role_assignable_roles_role foreign key (role) references reg.roles (role) on delete cascade,
  constraint fk_role_assignable_roles_assignable foreign key (assignable_role) references reg.roles (role)
);

create index ix_role_assignable_roles_assignable on reg.role_assignable_roles (assignable_role);

-- ── Memberships ──────────────────────────────────────────────────────────────

create table reg.memberships (
  subject      nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  compartment  nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  granted_at   datetimeoffset(3) not null,
  granted_by   nvarchar(128)     not null,

  constraint pk_memberships primary key clustered (subject, compartment),
  constraint ck_memberships_granted_by check (len(granted_by) between 1 and 128 and datalength(granted_by) = 2 * len(granted_by) and granted_by not like N' %' and granted_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint fk_memberships_subject foreign key (subject) references reg.users (subject),
  constraint fk_memberships_compartment foreign key (compartment) references reg.compartments (compartment)
);

create index ix_memberships_compartment on reg.memberships (compartment);

create table reg.member_roles (
  subject      nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  compartment  nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  role         nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  granted_at   datetimeoffset(3) not null,
  granted_by   nvarchar(128)     not null,

  constraint pk_member_roles primary key clustered (subject, compartment, role),
  constraint ck_member_roles_granted_by check (len(granted_by) between 1 and 128 and datalength(granted_by) = 2 * len(granted_by) and granted_by not like N' %' and granted_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint fk_member_roles_membership foreign key (subject, compartment) references reg.memberships (subject, compartment) on delete cascade,
  constraint fk_member_roles_role foreign key (role) references reg.roles (role)
);

create index ix_member_roles_role on reg.member_roles (role);

-- ── Operators ────────────────────────────────────────────────────────────────

create table reg.operator_roles (
  subject     nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  role        nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  granted_at  datetimeoffset(3) not null,
  granted_by  nvarchar(128)     not null,

  constraint pk_operator_roles primary key clustered (subject, role),
  constraint ck_operator_roles_granted_by check (len(granted_by) between 1 and 128 and datalength(granted_by) = 2 * len(granted_by) and granted_by not like N' %' and granted_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint fk_operator_roles_subject foreign key (subject) references reg.users (subject),
  constraint fk_operator_roles_role foreign key (role) references reg.roles (role)
);

create index ix_operator_roles_role on reg.operator_roles (role);

-- ── Transfers of ownership ───────────────────────────────────────────────────

create table reg.ownership_transfers (
  compartment   nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  from_subject  nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  to_subject    nvarchar(32)  collate Latin1_General_100_BIN2 not null,
  proposed_at   datetimeoffset(3) not null,
  proposed_by   nvarchar(128)     not null,
  expires_at    datetimeoffset(3) not null,

  constraint pk_ownership_transfers primary key clustered (compartment),
  constraint ck_ownership_transfers_parties check (from_subject <> to_subject),
  constraint ck_ownership_transfers_expires_at check (expires_at > proposed_at),
  constraint ck_ownership_transfers_proposed_by check (len(proposed_by) between 1 and 128 and datalength(proposed_by) = 2 * len(proposed_by) and proposed_by not like N' %' and proposed_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint fk_ownership_transfers_compartment foreign key (compartment) references reg.compartments (compartment),
  constraint fk_ownership_transfers_from foreign key (from_subject) references reg.users (subject),
  constraint fk_ownership_transfers_to foreign key (to_subject) references reg.users (subject)
);

create table reg.ownership_transfer_kept_roles (
  compartment  nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  role         nvarchar(63)  collate Latin1_General_100_BIN2 not null,

  constraint pk_ownership_transfer_kept_roles primary key clustered (compartment, role),
  constraint fk_ownership_transfer_kept_roles_transfer foreign key (compartment) references reg.ownership_transfers (compartment) on delete cascade,
  constraint fk_ownership_transfer_kept_roles_role foreign key (role) references reg.roles (role)
);

-- ── Invitations ──────────────────────────────────────────────────────────────

create table reg.invitations (
  invitation_id     uniqueidentifier  not null,
  token_hash        nchar(64) collate Latin1_General_100_BIN2 not null,
  compartment       nvarchar(63)  collate Latin1_General_100_BIN2 not null,
  email             nvarchar(320)     not null,
  designates_owner  bit               not null,
  delivery          nvarchar(5)       not null,
  issued_at         datetimeoffset(3) not null,
  issued_by         nvarchar(128)     not null,
  expires_at        datetimeoffset(3) not null,
  accepted_at       datetimeoffset(3) null,
  accepted_by       nvarchar(32)  collate Latin1_General_100_BIN2 null,
  revoked_at        datetimeoffset(3) null,
  revoked_by        nvarchar(128)     null,

  constraint pk_invitations primary key clustered (invitation_id),
  constraint ux_invitations_token_hash unique (token_hash),
  constraint ck_invitations_token_hash check (token_hash not like N'%[^0-9a-f]%'),
  constraint ck_invitations_email check (len(email) >= 3 and email collate Latin1_General_100_BIN2 = lower(email) collate Latin1_General_100_BIN2),
  constraint ck_invitations_delivery check (delivery collate Latin1_General_100_BIN2 in (N'email', N'hand')),
  constraint ck_invitations_issued_by check (len(issued_by) between 1 and 128 and datalength(issued_by) = 2 * len(issued_by) and issued_by not like N' %' and issued_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_invitations_expires_at check (expires_at > issued_at),
  constraint ck_invitations_accepted check ((accepted_at is null and accepted_by is null) or (accepted_at is not null and accepted_by is not null)),
  constraint ck_invitations_revoked check ((revoked_at is null and revoked_by is null) or (revoked_at is not null and revoked_by is not null)),
  constraint ck_invitations_revoked_by check (len(revoked_by) between 1 and 128 and datalength(revoked_by) = 2 * len(revoked_by) and revoked_by not like N' %' and revoked_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_invitations_one_end check (accepted_at is null or revoked_at is null),
  constraint fk_invitations_compartment foreign key (compartment) references reg.compartments (compartment),
  constraint fk_invitations_accepted_by foreign key (accepted_by) references reg.users (subject)
);

create index ix_invitations_compartment_email on reg.invitations (compartment, email);

create table reg.invitation_roles (
  invitation_id  uniqueidentifier  not null,
  role           nvarchar(63)  collate Latin1_General_100_BIN2 not null,

  constraint pk_invitation_roles primary key clustered (invitation_id, role),
  constraint ck_invitation_roles_role check (role like N'[a-z]%' and role not like N'%[^a-z0-9_]%' and role not like N'%[_]' and role not like N'%[_][_]%'),
  constraint fk_invitation_roles_invitation foreign key (invitation_id) references reg.invitations (invitation_id) on delete cascade
);

-- ── Acts waiting for a validation ────────────────────────────────────────────

create table reg.pending_acts (
  act_id        uniqueidentifier  not null,
  kind          nvarchar(16)      not null,
  subject       nvarchar(32)  collate Latin1_General_100_BIN2 null,
  role          nvarchar(63)  collate Latin1_General_100_BIN2 null,
  compartment   nvarchar(63)  collate Latin1_General_100_BIN2 null,
  definition    nvarchar(max)     null,
  proposed_at   datetimeoffset(3) not null,
  proposed_by   nvarchar(128)     not null,
  expires_at    datetimeoffset(3) not null,
  validated_at  datetimeoffset(3) null,
  validated_by  nvarchar(128)     null,
  rejected_at   datetimeoffset(3) null,
  rejected_by   nvarchar(128)     null,

  constraint pk_pending_acts primary key clustered (act_id),
  constraint ck_pending_acts_kind check (kind collate Latin1_General_100_BIN2 in (N'platform_role', N'role_definition', N'ownership_rescue')),
  constraint ck_pending_acts_target check (
    (kind collate Latin1_General_100_BIN2 = N'platform_role' and subject is not null and role is not null and compartment is null and definition is null)
    or (kind collate Latin1_General_100_BIN2 = N'role_definition' and subject is null and role is not null and compartment is null and definition is not null)
    or (kind collate Latin1_General_100_BIN2 = N'ownership_rescue' and subject is not null and role is null and compartment is not null and definition is null)),
  constraint ck_pending_acts_definition check (isjson(definition) = 1),
  constraint ck_pending_acts_proposed_by check (len(proposed_by) between 1 and 128 and datalength(proposed_by) = 2 * len(proposed_by) and proposed_by not like N' %' and proposed_by collate Latin1_General_100_BIN2 not like N'%[' + nchar(1) + N'-' + nchar(31) + nchar(127) + N']%'),
  constraint ck_pending_acts_expires_at check (expires_at > proposed_at),
  constraint ck_pending_acts_validated check ((validated_at is null and validated_by is null) or (validated_at is not null and validated_by is not null)),
  constraint ck_pending_acts_rejected check ((rejected_at is null and rejected_by is null) or (rejected_at is not null and rejected_by is not null)),
  constraint ck_pending_acts_one_end check (validated_at is null or rejected_at is null),
  constraint fk_pending_acts_subject foreign key (subject) references reg.users (subject),
  constraint fk_pending_acts_compartment foreign key (compartment) references reg.compartments (compartment)
);
