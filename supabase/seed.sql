do $$
begin
  if exists (select 1 from information_schema.tables where table_schema = 'public' and table_name = 'role_definitions') then
    insert into public.role_definitions (
      tenant_id,
      key,
      display_name,
      description,
      permissions_json,
      is_system
    )
    select
      null,
      seed_roles.key,
      seed_roles.display_name,
      seed_roles.description,
      seed_roles.permissions_json,
      true
    from (
      values
        ('admin', 'Admin', 'Full tenant administration access', '{"scope":"all"}'::jsonb),
        ('partner', 'Partner', 'Firm-wide legal and billing oversight', '{"scope":"firm"}'::jsonb),
        ('manager', 'Manager', 'Operational management and approvals', '{"scope":"operations"}'::jsonb),
        ('associate', 'Associate', 'Matter and workflow execution access', '{"scope":"matter"}'::jsonb),
        ('paralegal', 'Paralegal', 'Matter support and note/task management', '{"scope":"support"}'::jsonb),
        ('billing', 'Billing', 'Billing and receivables operations', '{"scope":"billing"}'::jsonb),
        ('trust_ops', 'Trust Operations', 'Trust accounting review and close operations', '{"scope":"trust"}'::jsonb),
        ('client_portal_manager', 'Client Portal Manager', 'Portal credential and communication management', '{"scope":"portal"}'::jsonb),
        ('integration_manager', 'Integration Manager', 'Integration connection and queue oversight', '{"scope":"integration"}'::jsonb)
    ) as seed_roles(key, display_name, description, permissions_json)
    where not exists (
      select 1
      from public.role_definitions existing
      where existing.tenant_id is null
        and existing.key = seed_roles.key
    );
  end if;
end $$;
