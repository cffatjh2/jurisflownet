create table if not exists public.tasks (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  assigned_user_id uuid null references public.users(id) on delete set null,
  title text not null,
  description text null,
  status text not null default 'todo' check (status in ('todo', 'in_progress', 'blocked', 'completed', 'cancelled')),
  priority text not null default 'normal' check (priority in ('low', 'normal', 'high', 'urgent')),
  outcome_text text null,
  due_at timestamptz null,
  reminder_at timestamptz null,
  completed_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_tasks_tenant_status_due
  on public.tasks (tenant_id, status, due_at asc nulls last)
  where is_deleted = false;

create index if not exists ix_tasks_tenant_matter_created
  on public.tasks (tenant_id, matter_id, created_at desc)
  where is_deleted = false;

create table if not exists public.task_status_history (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  task_id uuid not null references public.tasks(id) on delete cascade,
  previous_status text not null,
  new_status text not null,
  changed_at timestamptz not null default timezone('utc', now()),
  changed_by_user_id uuid null references public.users(id) on delete set null,
  note_text text null,
  constraint ck_task_status_history_distinct check (previous_status <> new_status)
);

create index if not exists ix_task_status_history_tenant_task_changed
  on public.task_status_history (tenant_id, task_id, changed_at desc);

create table if not exists public.calendar_events (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  title text not null,
  description text null,
  event_type text not null check (event_type in ('court', 'meeting', 'deadline', 'hearing', 'task_reminder', 'general')),
  starts_at timestamptz not null,
  ends_at timestamptz null,
  location_text text null,
  recurrence_rule text null,
  reminder_minutes integer not null default 0 check (reminder_minutes >= 0),
  reminder_sent boolean not null default false,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_calendar_events_tenant_starts
  on public.calendar_events (tenant_id, starts_at asc)
  where is_deleted = false;

create index if not exists ix_calendar_events_tenant_matter_starts
  on public.calendar_events (tenant_id, matter_id, starts_at asc)
  where is_deleted = false;

create table if not exists public.deadlines (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  title text not null,
  jurisdiction_code text null,
  rule_code text null,
  source_type text not null default 'manual' check (source_type in ('manual', 'rule', 'court', 'filing')),
  status text not null default 'open' check (status in ('open', 'completed', 'waived', 'missed')),
  due_at timestamptz not null,
  completed_at timestamptz null,
  notes_text text null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_deadlines_tenant_due_status
  on public.deadlines (tenant_id, due_at asc, status)
  where is_deleted = false;

create table if not exists public.notifications (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  user_id uuid not null references public.users(id) on delete cascade,
  matter_id uuid null references public.matters(id) on delete set null,
  client_id uuid null references public.clients(id) on delete set null,
  channel text not null default 'in_app' check (channel in ('in_app', 'email', 'sms')),
  status text not null default 'pending' check (status in ('pending', 'sent', 'read', 'failed')),
  title text not null,
  message_text text not null,
  link_target text null,
  sent_at timestamptz null,
  read_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_notifications_tenant_user_created
  on public.notifications (tenant_id, user_id, created_at desc)
  where is_deleted = false;

create index if not exists ix_notifications_tenant_status_created
  on public.notifications (tenant_id, status, created_at desc)
  where is_deleted = false;

drop trigger if exists trg_tasks_set_updated_at on public.tasks;
create trigger trg_tasks_set_updated_at before update on public.tasks
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_calendar_events_set_updated_at on public.calendar_events;
create trigger trg_calendar_events_set_updated_at before update on public.calendar_events
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_deadlines_set_updated_at on public.deadlines;
create trigger trg_deadlines_set_updated_at before update on public.deadlines
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_notifications_set_updated_at on public.notifications;
create trigger trg_notifications_set_updated_at before update on public.notifications
for each row execute function public.set_updated_at_and_row_version();
