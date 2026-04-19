# Canonical Schema Design

Bu dokuman Faz 4 cikisidir. Amac, yeni Supabase/Postgres veritabanini sifirdan kurarken kullanilacak final karar setini sabitlemektir.

Bu fazda:
- yeni DB olusturulmaz
- cutover yapilmaz
- mevcut legacy prod DB'ye dokunulmaz

## Tasarim Ilkeleri

1. Yeni veritabani `canonical` gercekliktir; legacy schema'ya bire bir sadik kalmaz.
2. Tum finansal alanlar `decimal/numeric` kullanir; `double` kullanilmaz.
3. Tum business tablolari `tenant_id` zorunlu tasir.
4. Tum mutable aggregate root'larda `row_version uuid not null` kullanilir.
5. Tum kritik tablolarda `created_at`, `created_by_user_id`, `updated_at`, `updated_by_user_id` vardir.
6. Soft-delete gereken tablolarda `is_deleted`, `deleted_at`, `deleted_by_user_id` vardir.
7. Check constraint ve enum disiplini uygulanir; serbest string status tasinmaz.
8. Trust ve billing tarafinda append-only ledger mantigi korunur.
9. Canonical schema `EnsureCreated` ile degil migration seti ile kurulur.

## Global Convention Seti

### Kimlik ve Tipler

- Tum PK alanlari `uuid`
- Tum FK alanlari `uuid`
- Para alanlari `numeric(18,2)`
- Oran/rate alanlari `numeric(18,4)`
- Zaman alanlari `timestamptz`
- JSON payload alanlari `jsonb`

### Soft Delete Kurali

Soft-delete uygulanacak ana tablolar:
- clients
- matters
- matter_notes
- documents
- users
- leads
- integration_connections

Hard-delete olmayan immutable tablolar:
- audit_logs
- trust_journal_entries
- trust_transactions
- trust_approval_decisions
- stripe_webhook_events

## Final Entity Listesi

### Foundation / Identity

- tenants
- firm_entities
- offices
- users
- role_definitions
- user_role_assignments
- staff_profiles
- auth_sessions
- mfa_challenges
- retention_policies
- audit_logs

### CRM / Matter Core

- clients
- client_contacts
- client_status_history
- leads
- matters
- matter_clients
- matter_staff_assignments
- matter_notes
- matter_note_revisions
- opposing_parties
- conflict_checks
- conflict_results

### Workflow

- tasks
- task_status_history
- calendar_events
- deadlines
- notifications

### Trust Accounting

- trust_accounts
- trust_matter_ledgers
- trust_posting_batches
- trust_transactions
- trust_transaction_allocations
- trust_journal_entries
- trust_approval_requests
- trust_approval_decisions
- trust_month_closes
- trust_reconciliation_snapshots
- trust_reconciliation_packets
- trust_compliance_exports
- trust_operational_alerts

### Billing / Payments

- billing_rate_cards
- billing_rate_card_entries
- matter_billing_policies
- time_entries
- expenses
- invoices
- invoice_line_items
- invoice_payor_allocations
- billing_payments
- billing_payment_allocations
- payment_plans
- outcome_fee_plans
- outcome_fee_plan_versions

### Documents / Portal / Communications

- documents
- document_versions
- document_shares
- document_comments
- document_content_indexes
- signature_requests
- signature_audit_entries
- client_portal_accounts
- client_messages
- email_messages
- outbound_emails
- sms_messages
- appointment_requests
- client_transparency_profiles
- client_transparency_snapshots
- client_transparency_timeline_items

### Integrations

- integration_connections
- integration_secrets
- integration_runs
- integration_entity_links
- integration_mapping_profiles
- integration_inbox_events
- integration_outbox_events
- integration_review_queue_items
- integration_conflict_queue_items
- stripe_webhook_events

## Aggregate Bazli Canonical Tasarim

## 1. Foundation / Identity

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `tenants` | `id` PK | `slug` unique, `status in ('active','suspended','disabled')` | audit + `row_version` | status bazli disable |
| `firm_entities` | `id` PK, `tenant_id` FK | `(tenant_id, code)` unique | audit + `row_version` | soft-delete |
| `offices` | `id` PK, `tenant_id`, `firm_entity_id` FK | `(tenant_id, firm_entity_id, code)` unique | audit + `row_version` | soft-delete |
| `users` | `id` PK, `tenant_id` FK | `(tenant_id, normalized_email)` unique, `status in ('active','invited','disabled')` | audit + `row_version` | soft-delete |
| `role_definitions` | `id` PK | `(tenant_id, key)` unique | audit + `row_version` | hard-delete yok |
| `user_role_assignments` | `id` PK, `user_id`, `role_id` FK | `(user_id, role_id)` unique | created audit | hard-delete yok |
| `staff_profiles` | `id` PK, `tenant_id`, `user_id` FK | `(tenant_id, user_id)` unique | audit + `row_version` | soft-delete |
| `auth_sessions` | `id` PK, `tenant_id`, `user_id` FK | `session_key` unique | audit + `row_version` | 90 gun |
| `mfa_challenges` | `id` PK, `tenant_id`, `user_id` FK | `challenge_key` unique | audit + `row_version` | 30 gun |
| `audit_logs` | `id` PK, `tenant_id` FK | `(tenant_id, sequence)` unique | immutable | retention policy |
| `retention_policies` | `id` PK, `tenant_id` FK | `(tenant_id, policy_key)` unique | audit + `row_version` | hard-delete yok |

Karar:
- `users.role` string kolonu kalkar.
- `users.employee_role` ayrilip `staff_profiles` ve `user_role_assignments` altina gider.

## 2. Clients / Matters / Notes

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `clients` | `id` PK, `tenant_id` FK | `(tenant_id, normalized_email)` partial unique, `(tenant_id, client_number)` unique nullable, `kind in ('individual','organization')`, `status in ('active','inactive','prospect','archived')` | audit + `row_version` | soft-delete |
| `client_contacts` | `id` PK, `tenant_id`, `client_id` FK | `(tenant_id, client_id, normalized_email)` unique nullable | audit + `row_version` | soft-delete |
| `client_status_history` | `id` PK, `tenant_id`, `client_id` FK | check: `previous_status <> new_status` | append-only | 7 yil |
| `leads` | `id` PK, `tenant_id` FK | `status in ('new','qualified','unqualified','converted','archived')` | audit + `row_version` | soft-delete |
| `matters` | `id` PK, `tenant_id`, `client_id`, `responsible_user_id`, `firm_entity_id`, `office_id` FK | `(tenant_id, case_number_normalized)` unique, `status in ('intake','open','on_hold','closed','archived')`, `fee_model in ('hourly','fixed','contingency','hybrid')` | audit + `row_version` | soft-delete |
| `matter_clients` | `id` PK, `tenant_id`, `matter_id`, `client_id` FK | `(tenant_id, matter_id, client_id, relationship_type)` unique | audit + `row_version` | hard-delete yok |
| `matter_staff_assignments` | `id` PK, `tenant_id`, `matter_id`, `user_id` FK | `(tenant_id, matter_id, user_id, assignment_role)` unique | audit + `row_version` | hard-delete yok |
| `matter_notes` | `id` PK, `tenant_id`, `matter_id` FK | `visibility in ('internal','private','client_visible')`, `note_type in ('general','call','meeting','strategy','billing')` | audit + `row_version` | soft-delete |
| `matter_note_revisions` | `id` PK, `tenant_id`, `matter_note_id` FK | `(matter_note_id, revision_number)` unique | append-only | 10 yil |
| `opposing_parties` | `id` PK, `tenant_id`, `matter_id` FK | `(tenant_id, matter_id, normalized_name)` unique | audit + `row_version` | soft-delete |
| `conflict_checks` | `id` PK, `tenant_id`, `matter_id` FK | `status in ('pending','cleared','waiver_required','blocked')` | audit + `row_version` | hard-delete yok |
| `conflict_results` | `id` PK, `tenant_id`, `conflict_check_id` FK | `severity in ('low','medium','high','blocker')` | append-only | 10 yil |

Karar:
- `matters.trust_balance` canonical schema'da yoktur.
- `matters.responsible_attorney` string yerine `responsible_user_id` gelir.
- `matter_notes.body` plain text veya sanitize edilmis rich text olarak tutulur.

## 3. Workflow

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `tasks` | `id` PK, `tenant_id`, `matter_id`, `assigned_user_id` FK | `status in ('todo','in_progress','blocked','completed','cancelled')`, `priority in ('low','normal','high','urgent')` | audit + `row_version` | soft-delete |
| `task_status_history` | `id` PK, `tenant_id`, `task_id` FK | check: status degisimi zorunlu | append-only | 3 yil |
| `calendar_events` | `id` PK, `tenant_id`, `matter_id` FK | `event_type in ('court','meeting','deadline','hearing','task_reminder','general')` | audit + `row_version` | soft-delete |
| `deadlines` | `id` PK, `tenant_id`, `matter_id` FK | `status in ('open','completed','waived','missed')` | audit + `row_version` | soft-delete |
| `notifications` | `id` PK, `tenant_id`, `user_id` FK | `channel in ('in_app','email','sms')`, `status in ('pending','sent','read','failed')` | audit + `row_version` | 180 gun |

## 4. Trust Accounting

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `trust_accounts` | `id` PK, `tenant_id`, `firm_entity_id`, `office_id` FK | `(tenant_id, account_number_masked, bank_name)` unique, `status in ('active','frozen','closed')` | audit + `row_version` | hard-delete yok |
| `trust_matter_ledgers` | `id` PK, `tenant_id`, `trust_account_id`, `matter_id`, `client_id` FK | `(tenant_id, trust_account_id, matter_id, client_id)` unique | audit + `row_version` | hard-delete yok |
| `trust_posting_batches` | `id` PK, `tenant_id` FK | `(tenant_id, command_id)` unique, `status in ('pending','posted','voided','failed')` | audit + `row_version` | hard-delete yok |
| `trust_transactions` | `id` PK, `tenant_id`, `trust_account_id`, `matter_id`, `posting_batch_id` FK | `transaction_type in ('deposit','withdrawal','transfer','reversal')`, `amount > 0`, `approval_status in ('not_required','pending','approved','rejected')` | audit + `row_version`, mutable only pre-post | hard-delete yok |
| `trust_transaction_allocations` | `id` PK, `tenant_id`, `trust_transaction_id`, `trust_matter_ledger_id` FK | `(tenant_id, trust_transaction_id, trust_matter_ledger_id, allocation_type)` unique, `amount > 0` | append-only | hard-delete yok |
| `trust_journal_entries` | `id` PK, `tenant_id`, `trust_transaction_id`, `trust_matter_ledger_id` FK | `(tenant_id, trust_transaction_id, entry_side, entry_order)` unique, `entry_side in ('debit','credit')` | immutable | 10 yil |
| `trust_approval_requests` | `id` PK, `tenant_id`, `trust_transaction_id` FK | `(tenant_id, trust_transaction_id)` unique, `status in ('pending','approved','rejected','expired')` | audit + `row_version` | hard-delete yok |
| `trust_approval_decisions` | `id` PK, `tenant_id`, `approval_request_id`, `decided_by_user_id` FK | `decision in ('approve','reject','override')` | append-only | 10 yil |
| `trust_month_closes` | `id` PK, `tenant_id`, `trust_account_id` FK | `(tenant_id, trust_account_id, close_month)` unique, `status in ('open','in_progress','closed','reopened')` | audit + `row_version` | hard-delete yok |
| `trust_reconciliation_snapshots` | `id` PK, `tenant_id`, `trust_account_id` FK | `(tenant_id, trust_account_id, snapshot_date)` unique | audit + `row_version` | 10 yil |
| `trust_reconciliation_packets` | `id` PK, `tenant_id`, `snapshot_id` FK | `(tenant_id, packet_number)` unique, `status in ('draft','sealed','superseded')` | audit + `row_version` | immutable after sealed |
| `trust_compliance_exports` | `id` PK, `tenant_id`, `trust_account_id` FK | `(tenant_id, export_type, export_period_start, export_period_end)` unique | audit + `row_version` | 10 yil |
| `trust_operational_alerts` | `id` PK, `tenant_id`, `trust_account_id` nullable FK | `severity in ('info','warning','high','critical')`, `status in ('open','acknowledged','resolved')` | audit + `row_version` | 3 yil |

Karar:
- Bakiye tablo kolonu degil, `trust_journal_entries` uzerinden projection'dir.
- Trust tarafinda soft-delete yoktur; durum/lifecycle ile kapanis vardir.

## 5. Billing / Payments

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `billing_rate_cards` | `id` PK, `tenant_id`, `firm_entity_id` FK | `(tenant_id, code)` unique | audit + `row_version` | soft-delete |
| `billing_rate_card_entries` | `id` PK, `tenant_id`, `rate_card_id` FK | role + UTBMS bazli partial unique | audit + `row_version` | soft-delete |
| `matter_billing_policies` | `id` PK, `tenant_id`, `matter_id` FK | `(tenant_id, matter_id)` unique, `billing_mode in ('hourly','fixed','contingency','hybrid')` | audit + `row_version` | hard-delete yok |
| `time_entries` | `id` PK, `tenant_id`, `matter_id`, `user_id` FK | `minutes > 0`, `status in ('draft','submitted','approved','billed','written_off')` | audit + `row_version` | soft-delete |
| `expenses` | `id` PK, `tenant_id`, `matter_id` FK | `amount >= 0`, `status in ('draft','approved','billed','reimbursed','written_off')` | audit + `row_version` | soft-delete |
| `invoices` | `id` PK, `tenant_id`, `client_id`, `matter_id` FK | `(tenant_id, invoice_number)` unique, `status in ('draft','issued','partially_paid','paid','void','written_off')` | audit + `row_version` | hard-delete yok |
| `invoice_line_items` | `id` PK, `tenant_id`, `invoice_id` FK | `quantity > 0`, `unit_price >= 0` | audit + `row_version` | hard-delete yok |
| `invoice_payor_allocations` | `id` PK, `tenant_id`, `invoice_id`, `client_id` FK | `(tenant_id, invoice_id, client_id, allocation_scope)` unique | audit + `row_version` | hard-delete yok |
| `billing_payments` | `id` PK, `tenant_id`, `client_id` FK | `amount > 0`, `payment_status in ('pending','settled','failed','refunded','voided')` | audit + `row_version` | hard-delete yok |
| `billing_payment_allocations` | `id` PK, `tenant_id`, `billing_payment_id`, `invoice_id` FK | `(tenant_id, billing_payment_id, invoice_id)` unique | append-only | hard-delete yok |
| `payment_plans` | `id` PK, `tenant_id`, `client_id`, `matter_id` FK | `status in ('draft','active','paused','completed','cancelled')` | audit + `row_version` | hard-delete yok |
| `outcome_fee_plans` | `id` PK, `tenant_id`, `matter_id` FK | active status icin partial unique, `status in ('draft','active','retired')` | audit + `row_version` | hard-delete yok |
| `outcome_fee_plan_versions` | `id` PK, `tenant_id`, `outcome_fee_plan_id` FK | `(outcome_fee_plan_id, version_number)` unique | append-only | 10 yil |

## 6. Documents / Portal / Communications

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `documents` | `id` PK, `tenant_id`, `matter_id`, `uploaded_by_user_id` FK | `status in ('draft','active','archived','legal_hold')` | audit + `row_version` | soft-delete, legal hold override |
| `document_versions` | `id` PK, `tenant_id`, `document_id` FK | `(document_id, version_number)` unique | append-only | retention policy |
| `document_shares` | `id` PK, `tenant_id`, `document_id`, `client_id` FK | `(tenant_id, document_id, client_id, share_scope)` unique | audit + `row_version` | soft-delete |
| `document_comments` | `id` PK, `tenant_id`, `document_id`, `author_user_id` FK | no empty body | audit + `row_version` | soft-delete |
| `document_content_indexes` | `id` PK, `tenant_id`, `document_version_id` FK | `(tenant_id, document_version_id)` unique | audit + `row_version` | rebuildable |
| `signature_requests` | `id` PK, `tenant_id`, `document_id`, `client_id` FK | `status in ('draft','sent','viewed','signed','expired','cancelled')` | audit + `row_version` | 10 yil |
| `signature_audit_entries` | `id` PK, `tenant_id`, `signature_request_id` FK | append-only | immutable | 10 yil |
| `client_portal_accounts` | `id` PK, `tenant_id`, `client_id` FK | `(tenant_id, client_id)` unique, `(tenant_id, normalized_email)` unique, `status in ('pending','active','locked','disabled')` | audit + `row_version` | soft-delete |
| `client_messages` | `id` PK, `tenant_id`, `client_id`, `matter_id` FK | `direction in ('inbound','outbound')` | audit + `row_version` | 3 yil |
| `email_messages` | `id` PK, `tenant_id`, `matter_id` FK | `(tenant_id, provider_message_id)` unique nullable | audit + `row_version` | 3 yil |
| `outbound_emails` | `id` PK, `tenant_id`, `email_message_id` FK | `(tenant_id, provider_send_id)` unique nullable | audit + `row_version` | 1 yil |
| `sms_messages` | `id` PK, `tenant_id`, `client_id` FK | `(tenant_id, provider_message_id)` unique nullable | audit + `row_version` | 1 yil |
| `appointment_requests` | `id` PK, `tenant_id`, `client_id` FK | `status in ('pending','approved','declined','cancelled')` | audit + `row_version` | 2 yil |
| `client_transparency_profiles` | `id` PK, `tenant_id`, `matter_id` FK | `(tenant_id, matter_id)` unique | audit + `row_version` | hard-delete yok |
| `client_transparency_snapshots` | `id` PK, `tenant_id`, `matter_id` FK | `(tenant_id, matter_id, snapshot_at)` unique | append-only | 2 yil |
| `client_transparency_timeline_items` | `id` PK, `tenant_id`, `matter_id` FK | `visibility in ('internal','client_visible')` | audit + `row_version` | 2 yil |

## 7. Integrations

| Tablo | PK/FK | Unique / Check | Audit / Concurrency | Retention |
| --- | --- | --- | --- | --- |
| `integration_connections` | `id` PK, `tenant_id` FK | `(tenant_id, provider, provider_key, external_account_id)` unique, `status in ('connected','paused','error','revoked')` | audit + `row_version` | soft-delete |
| `integration_secrets` | `id` PK, `tenant_id`, `connection_id` FK | `(tenant_id, connection_id, secret_type)` unique | audit + `row_version` | hard-delete yok |
| `integration_runs` | `id` PK, `tenant_id`, `connection_id` FK | `status in ('queued','running','completed','failed','cancelled')` | audit + `row_version` | 1 yil |
| `integration_entity_links` | `id` PK, `tenant_id`, `connection_id` FK | local ve external entity kombinasyonu unique | audit + `row_version` | hard-delete yok |
| `integration_mapping_profiles` | `id` PK, `tenant_id`, `connection_id` FK | `(tenant_id, connection_id, profile_key)` unique | audit + `row_version` | soft-delete |
| `integration_inbox_events` | `id` PK, `tenant_id`, `connection_id` FK | `(tenant_id, provider_event_id)` unique nullable | append-only | 180 gun |
| `integration_outbox_events` | `id` PK, `tenant_id`, `connection_id` FK | `(tenant_id, event_key)` unique | audit + `row_version` | 180 gun |
| `integration_review_queue_items` | `id` PK, `tenant_id`, `connection_id` FK | `status in ('open','assigned','resolved','dismissed')` | audit + `row_version` | 1 yil |
| `integration_conflict_queue_items` | `id` PK, `tenant_id`, `connection_id` FK | `status in ('open','resolved','ignored')` | audit + `row_version` | 1 yil |
| `stripe_webhook_events` | `id` PK | `provider_event_id` unique | immutable | 1 yil |

## Final SQL Seti Bolumleme Karari

Phase 5'te SQL migration seti asagidaki bolumlerle uretilir:

1. `001_foundation_tenants_identity.sql`
2. `010_clients_matters_core.sql`
3. `020_workflow_tasks_calendar.sql`
4. `030_trust_accounting_core.sql`
5. `040_billing_payments.sql`
6. `050_documents_signatures_portal.sql`
7. `060_integrations.sql`
8. `070_audit_retention_views.sql`
9. `080_seed_reference_data.sql`

## Canonical Disinda Birakilan Alanlar

Canonical v1 cekirdeginin disinda kalan veya sonraki dalgaya birakilan alanlar:
- AI drafting session tablolari
- App directory marketplace tablolari
- jurisdiction authoring/test harness detaylari
- deneysel trust risk radar alt tablolari

Bu dokumanla final entity listesi ve aggregate bazli PK/FK/index/check/audit/concurrency/retention kurallari sabitlenmistir.
