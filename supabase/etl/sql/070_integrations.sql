-- Scenario A template: Integrations

insert into public.integration_connections (
  id, tenant_id, provider, provider_key, category, external_account_id, account_label,
  account_email, status, sync_enabled, sync_cursor, delta_token, metadata_json,
  last_sync_at, last_webhook_at, created_at, updated_at, row_version
)
select
  c."Id"::uuid,
  c."TenantId"::uuid,
  trim(c."Provider"),
  trim(c."ProviderKey"),
  trim(c."Category"),
  nullif(trim(c."ExternalAccountId"), ''),
  nullif(trim(c."AccountLabel"), ''),
  nullif(trim(c."AccountEmail"), ''),
  case lower(trim(coalesce(c."Status", 'connected')))
    when 'paused' then 'paused'
    when 'error' then 'error'
    when 'revoked' then 'revoked'
    else 'connected'
  end,
  coalesce(c."SyncEnabled", true),
  nullif(trim(c."SyncCursor"), ''),
  nullif(trim(c."DeltaToken"), ''),
  case when nullif(trim(c."MetadataJson"), '') is null then null else c."MetadataJson"::jsonb end,
  c."LastSyncAt",
  c."LastWebhookAt",
  coalesce(c."ConnectedAt", timezone('utc', now())),
  coalesce(c."UpdatedAt", coalesce(c."ConnectedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."IntegrationConnections" c
on conflict (id) do update
set status = excluded.status,
    sync_enabled = excluded.sync_enabled,
    sync_cursor = excluded.sync_cursor,
    delta_token = excluded.delta_token,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.integration_entity_links (
  id, tenant_id, connection_id, local_entity_type, local_entity_id, external_entity_type, external_entity_id,
  metadata_json, created_at, updated_at, row_version
)
select
  l."Id"::uuid,
  l."TenantId"::uuid,
  l."ConnectionId"::uuid,
  trim(l."LocalEntityType"),
  l."LocalEntityId"::uuid,
  trim(l."ExternalEntityType"),
  trim(l."ExternalEntityId"),
  case when nullif(trim(l."MetadataJson"), '') is null then null else l."MetadataJson"::jsonb end,
  coalesce(l."CreatedAt", timezone('utc', now())),
  coalesce(l."UpdatedAt", coalesce(l."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."IntegrationEntityLinks" l
on conflict (id) do update
set metadata_json = excluded.metadata_json,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();
