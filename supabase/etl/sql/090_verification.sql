-- Phase 6 verification query set
-- Execute only after a future migration run.

-- Row count report
select 'clients' as entity, count(*) as target_count from public.clients
union all
select 'matters', count(*) from public.matters
union all
select 'matter_notes', count(*) from public.matter_notes
union all
select 'trust_transactions', count(*) from public.trust_transactions
union all
select 'invoices', count(*) from public.invoices
union all
select 'documents', count(*) from public.documents;

-- Orphan checks
select 'matters.client_id' as check_name, count(*) as orphan_count
from public.matters m
left join public.clients c on c.id = m.client_id
where c.id is null
union all
select 'matter_notes.matter_id', count(*)
from public.matter_notes n
left join public.matters m on m.id = n.matter_id
where m.id is null
union all
select 'trust_transactions.trust_account_id', count(*)
from public.trust_transactions t
left join public.trust_accounts a on a.id = t.trust_account_id
where a.id is null;

-- Sample verification
select m.case_number, m.display_name, u.display_name as responsible_user
from public.matters m
join public.users u on u.id = m.responsible_user_id
order by m.created_at desc
limit 20;

select i.invoice_number, i.total_amount, i.amount_paid, i.balance_amount
from public.invoices i
order by i.issue_date desc
limit 20;
