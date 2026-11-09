-- ============================================================================
-- Stellaegis.Registry — the registry schema, script 0002 (Registry 7.0.0)
-- ============================================================================
-- Applied after 0001 by the installation, replayed as it was the first time: a database installed
-- by an earlier version takes it by running the installation again, and the application refuses to
-- start until it has.

-- ── Acts that wait: the hand delivery of a reset link ────────────────────────
-- A reset link of an active account handed over to an administrator gives them the account: it
-- waits for a second person, as a platform role does. Validated, it changes nothing in the registry;
-- its author spends it once, through the accounts, which consumed_at records.

alter table reg.pending_acts add column consumed_at timestamptz;

alter table reg.pending_acts drop constraint ck_pending_acts_kind;
alter table reg.pending_acts add constraint ck_pending_acts_kind
  check (kind in ('platform_role', 'role_definition', 'ownership_rescue', 'hand_delivery'));

alter table reg.pending_acts drop constraint ck_pending_acts_target;
alter table reg.pending_acts add constraint ck_pending_acts_target check (
  (kind = 'platform_role' and subject is not null and role is not null and compartment is null and definition is null)
  or (kind = 'role_definition' and subject is null and role is not null and compartment is null and definition is not null)
  or (kind = 'ownership_rescue' and subject is not null and role is null and compartment is not null and definition is null)
  or (kind = 'hand_delivery' and subject is not null and role is null and compartment is null and definition is null));

-- Spent once, and only once validated.
alter table reg.pending_acts add constraint ck_pending_acts_consumed
  check (consumed_at is null or (kind = 'hand_delivery' and validated_at is not null));
