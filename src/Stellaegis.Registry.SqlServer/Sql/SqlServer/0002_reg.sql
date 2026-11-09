-- ============================================================================
-- Stellaegis.Registry — the registry schema on SQL Server, script 0002 (Registry 7.0.0)
-- ============================================================================
-- Applied after 0001 by the installation, replayed as it was the first time: a database installed
-- by an earlier version takes it by running the installation again, and the application refuses to
-- start until it has. The same change as the PostgreSQL script 0002 of Stellaegis.Registry.PostgreSQL.

-- ── Acts that wait: the hand delivery of a reset link ────────────────────────
-- A reset link of an active account handed over to an administrator gives them the account: it
-- waits for a second person, as a platform role does. Validated, it changes nothing in the registry;
-- its author spends it once, through the accounts, which consumed_at records.

alter table reg.pending_acts add consumed_at datetimeoffset(3) null;
go

alter table reg.pending_acts drop constraint ck_pending_acts_kind;
go

alter table reg.pending_acts add constraint ck_pending_acts_kind
  check (kind collate Latin1_General_100_BIN2 in (N'platform_role', N'role_definition', N'ownership_rescue', N'hand_delivery'));
go

alter table reg.pending_acts drop constraint ck_pending_acts_target;
go

alter table reg.pending_acts add constraint ck_pending_acts_target check (
  (kind collate Latin1_General_100_BIN2 = N'platform_role' and subject is not null and role is not null and compartment is null and definition is null)
  or (kind collate Latin1_General_100_BIN2 = N'role_definition' and subject is null and role is not null and compartment is null and definition is not null)
  or (kind collate Latin1_General_100_BIN2 = N'ownership_rescue' and subject is not null and role is null and compartment is not null and definition is null)
  or (kind collate Latin1_General_100_BIN2 = N'hand_delivery' and subject is not null and role is null and compartment is null and definition is null));
go

-- Spent once, and only once validated.
alter table reg.pending_acts add constraint ck_pending_acts_consumed
  check (consumed_at is null or (kind collate Latin1_General_100_BIN2 = N'hand_delivery' and validated_at is not null));
go
