# Canonical Schema Migration Backlog

Bu backlog Faz 5 ve Faz 6'da uretilecek SQL migration seti ile ETL scriptlerinin is sirasini tanimlar.

## Backlog Ilkeleri

1. Her backlog maddesi additive SQL veya veri tasima scripti olarak ayrilir.
2. Canonical schema bos DB kurulumunu hedefler.
3. Legacy prod DB'de bu backlog'un hicbiri calistirilmaz.
4. Her dalga icin DDL, seed/reference data, verification query seti ve rollback notu ayri yazilir.

## Wave 1: Foundation

Teslimatlar:
- `tenants`
- `firm_entities`
- `offices`
- `users`
- `role_definitions`
- `user_role_assignments`
- `staff_profiles`
- `auth_sessions`
- `mfa_challenges`
- `audit_logs`
- `retention_policies`

Verification:
- tenant isolation smoke query
- email uniqueness testleri
- role assignment FK testleri

## Wave 2: CRM ve Matter Core

Teslimatlar:
- `clients`
- `client_contacts`
- `client_status_history`
- `leads`
- `matters`
- `matter_clients`
- `matter_staff_assignments`
- `matter_notes`
- `matter_note_revisions`
- `opposing_parties`
- `conflict_checks`
- `conflict_results`

Verification:
- same-tenant FK kontrolu
- duplicate matter client link engeli
- row version conflict testi

## Wave 3: Workflow

Teslimatlar:
- `tasks`
- `task_status_history`
- `calendar_events`
- `deadlines`
- `notifications`

Verification:
- assignee FK ve matter FK testi
- notification retention query

## Wave 4: Trust Core

Teslimatlar:
- `trust_accounts`
- `trust_matter_ledgers`
- `trust_posting_batches`
- `trust_transactions`
- `trust_transaction_allocations`
- `trust_journal_entries`

Verification:
- debit/credit denge query'leri
- negative balance rule query
- allocation toplam = transaction toplam testi

## Wave 5: Trust Governance

Teslimatlar:
- `trust_approval_requests`
- `trust_approval_decisions`
- `trust_month_closes`
- `trust_reconciliation_snapshots`
- `trust_reconciliation_packets`
- `trust_compliance_exports`
- `trust_operational_alerts`

Verification:
- approval audit trail
- close uniqueness per month
- compliance export idempotency

## Wave 6: Billing ve Payments

Teslimatlar:
- `billing_rate_cards`
- `billing_rate_card_entries`
- `matter_billing_policies`
- `time_entries`
- `expenses`
- `invoices`
- `invoice_line_items`
- `invoice_payor_allocations`
- `billing_payments`
- `billing_payment_allocations`
- `payment_plans`
- `outcome_fee_plans`
- `outcome_fee_plan_versions`

Verification:
- invoice total reconciliation
- payment allocation balance query
- payor allocation uniqueness

## Wave 7: Documents / Portal / Communications

Teslimatlar:
- `documents`
- `document_versions`
- `document_shares`
- `document_comments`
- `document_content_indexes`
- `signature_requests`
- `signature_audit_entries`
- `client_portal_accounts`
- `client_messages`
- `email_messages`
- `outbound_emails`
- `sms_messages`
- `appointment_requests`
- `client_transparency_profiles`
- `client_transparency_snapshots`
- `client_transparency_timeline_items`

Verification:
- document version chain testleri
- share authorization query seti
- portal account uniqueness

## Wave 8: Integrations

Teslimatlar:
- `integration_connections`
- `integration_secrets`
- `integration_runs`
- `integration_entity_links`
- `integration_mapping_profiles`
- `integration_inbox_events`
- `integration_outbox_events`
- `integration_review_queue_items`
- `integration_conflict_queue_items`
- `stripe_webhook_events`

Verification:
- idempotency unique constraint testleri
- queue retry lifecycle query'leri
- entity link uniqueness testi

## Wave 9: Seed / Reference / Views

Teslimatlar:
- sistem rolleri seed'i
- enum lookup veya reference data seed'i
- reporting view backlog'u
- trust ve billing projection view'lari

Verification:
- bos DB bootstrap testi
- staging smoke test icin asgari seed seti

## Data Migration Backlog

### ETL 1: Identity ve Organization Map

- tenant id map
- user id map
- role split scripti
- staff profile transform

### ETL 2: Clients ve Matters

- client temizligi ve normalized email backfill
- matter status/fee model normalization
- matter staff assignment uretilmesi
- note revision initial snapshot scripti

### ETL 3: Trust

- trust account / ledger map
- allocations json parse ve satirlastirma
- journal entry consistency raporu
- orphan trust reference raporu

### ETL 4: Billing

- invoice numbering duplicate cleanup
- payment allocation rebuild
- outcome-fee version flattening

### ETL 5: Documents ve Portal

- document object key map
- portal account split
- old search indexleri rebuild secenegi

### ETL 6: Integrations

- connection and entity link map
- pending outbox replay hazirligi
- stale run / event pruning

## Verification Backlog

Her wave icin asgari:
- row count report
- orphan FK report
- duplicate unique key report
- enum normalization report
- sample business verification checklist

Bu backlog ile canonical schema karar seti migration dalgalarina bolunmus durumdadir.
