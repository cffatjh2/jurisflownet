create or replace view public.v_active_matters as
select
  m.id,
  m.tenant_id,
  m.client_id,
  m.responsible_user_id,
  m.case_number,
  m.display_name,
  m.status,
  m.fee_model,
  m.open_date,
  c.display_name as client_name
from public.matters m
join public.clients c on c.id = m.client_id
where m.is_deleted = false
  and m.status in ('intake', 'open', 'on_hold');

create or replace view public.v_invoice_balances as
select
  i.id,
  i.tenant_id,
  i.client_id,
  i.matter_id,
  i.invoice_number,
  i.status,
  i.total_amount,
  i.amount_paid,
  (i.total_amount - i.amount_paid) as calculated_balance
from public.invoices i;

create or replace view public.v_trust_matter_balances as
select
  l.id as trust_matter_ledger_id,
  l.tenant_id,
  l.trust_account_id,
  l.matter_id,
  l.client_id,
  coalesce(sum(case when e.entry_side = 'credit' then e.amount else -e.amount end), 0)::numeric(18,2) as balance_amount
from public.trust_matter_ledgers l
left join public.trust_journal_entries e on e.trust_matter_ledger_id = l.id
group by l.id, l.tenant_id, l.trust_account_id, l.matter_id, l.client_id;

create or replace view public.v_open_notifications as
select
  n.id,
  n.tenant_id,
  n.user_id,
  n.channel,
  n.status,
  n.title,
  n.message_text,
  n.created_at
from public.notifications n
where n.is_deleted = false
  and n.status in ('pending', 'sent');
