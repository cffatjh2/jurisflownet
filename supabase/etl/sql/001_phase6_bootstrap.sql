-- Phase 6 bootstrap template
-- This file is not executed automatically in this phase.
-- It defines helper tables/functions for a future migration run.

create schema if not exists migration_work;

create table if not exists migration_work.run_registry (
  run_id uuid primary key default gen_random_uuid(),
  scenario text not null check (scenario in ('A', 'B')),
  status text not null default 'planned' check (status in ('planned', 'preview', 'running', 'completed', 'failed', 'cancelled')),
  started_at timestamptz null,
  completed_at timestamptz null,
  notes_json jsonb null
);

create table if not exists migration_work.id_map (
  map_key text not null,
  source_id text not null,
  target_id uuid not null,
  natural_key text null,
  created_at timestamptz not null default timezone('utc', now()),
  primary key (map_key, source_id)
);

create index if not exists ix_migration_work_id_map_target
  on migration_work.id_map (map_key, target_id);

create table if not exists migration_work.issue_log (
  issue_id uuid primary key default gen_random_uuid(),
  severity text not null check (severity in ('info', 'warning', 'error')),
  entity_name text not null,
  source_id text null,
  issue_code text not null,
  details_json jsonb null,
  created_at timestamptz not null default timezone('utc', now())
);

create or replace function migration_work.register_id_map(
  p_map_key text,
  p_source_id text,
  p_target_id uuid,
  p_natural_key text default null
)
returns void
language plpgsql
as $$
begin
  insert into migration_work.id_map (map_key, source_id, target_id, natural_key)
  values (p_map_key, p_source_id, p_target_id, p_natural_key)
  on conflict (map_key, source_id) do update
  set target_id = excluded.target_id,
      natural_key = excluded.natural_key;
end;
$$;
