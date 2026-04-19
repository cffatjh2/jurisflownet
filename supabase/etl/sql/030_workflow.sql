-- Scenario A template: Workflow

insert into public.tasks (
  id, tenant_id, matter_id, assigned_user_id, title, description, status, priority, outcome_text,
  due_at, reminder_at, created_at, updated_at, row_version
)
select
  t."Id"::uuid,
  t."TenantId"::uuid,
  nullif(t."MatterId", '')::uuid,
  assignee.target_id,
  trim(t."Title"),
  nullif(trim(t."Description"), ''),
  case lower(trim(t."Status"))
    when 'completed' then 'completed'
    when 'done' then 'completed'
    when 'blocked' then 'blocked'
    when 'in progress' then 'in_progress'
    else 'todo'
  end,
  case lower(trim(t."Priority"))
    when 'high' then 'high'
    when 'urgent' then 'urgent'
    when 'low' then 'low'
    else 'normal'
  end,
  nullif(trim(t."Outcome"), ''),
  t."DueDate",
  t."ReminderAt",
  coalesce(t."CreatedAt", timezone('utc', now())),
  coalesce(t."UpdatedAt", coalesce(t."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Tasks" t
left join migration_work.id_map assignee
  on assignee.map_key = 'user'
 and assignee.natural_key = concat(t."TenantId", ':', lower(trim(coalesce(t."AssignedTo", ''))))
on conflict (id) do update
set assigned_user_id = excluded.assigned_user_id,
    status = excluded.status,
    priority = excluded.priority,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.calendar_events (
  id, tenant_id, matter_id, title, description, event_type, starts_at, location_text, recurrence_rule, reminder_minutes,
  reminder_sent, created_at, updated_at, row_version
)
select
  e."Id"::uuid,
  e."TenantId"::uuid,
  nullif(e."MatterId", '')::uuid,
  trim(e."Title"),
  nullif(trim(e."Description"), ''),
  case lower(trim(e."Type"))
    when 'court' then 'court'
    when 'meeting' then 'meeting'
    when 'deadline' then 'deadline'
    else 'general'
  end,
  e."Date",
  nullif(trim(e."Location"), ''),
  nullif(trim(e."RecurrencePattern"), ''),
  coalesce(e."ReminderMinutes", 0),
  coalesce(e."ReminderSent", false),
  coalesce(e."CreatedAt", timezone('utc', now())),
  coalesce(e."UpdatedAt", coalesce(e."CreatedAt", timezone('utc', now()))),
  coalesce(nullif(e."RowVersion", '')::uuid, gen_random_uuid())
from legacy_public."CalendarEvents" e
on conflict (id) do update
set event_type = excluded.event_type,
    starts_at = excluded.starts_at,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();
