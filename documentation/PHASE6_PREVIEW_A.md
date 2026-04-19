# Phase 6 Preview Report

- scenario: A
- scenario-name: legacy-to-canonical
- aciklama: Legacy veri canonical bos veritabanina idempotent SQL ve id map katmani ile tasinacak.
- toplam transform kurali: 23
- toplam id-map tanimi: 15
- toplam fk-rewire tanimi: 16
- toplam verification profili: 7

## Gerekli Artefaktlar

- manifest.phase6.json : ok
- transforms.phase6.json : ok
- id-maps.phase6.json : ok
- verification.phase6.json : ok
- sql/001_phase6_bootstrap.sql : ok
- sql/010_identity_and_org.sql : ok
- sql/020_clients_matters.sql : ok
- sql/030_workflow.sql : ok
- sql/040_trust.sql : ok
- sql/050_billing.sql : ok
- sql/060_documents_portal.sql : ok
- sql/070_integrations.sql : ok
- sql/090_verification.sql : ok

## ID Map Seti

- tenant : legacy_public.Tenant -> public.tenants
- firm_entity : legacy_public.FirmEntity -> public.firm_entities
- office : legacy_public.Office -> public.offices
- user : legacy_public.User -> public.users
- client : legacy_public.Client -> public.clients
- lead : legacy_public.Lead -> public.leads
- matter : legacy_public.Matter -> public.matters
- matter_note : legacy_public.MatterNote -> public.matter_notes
- task : legacy_public.Task -> public.tasks
- calendar_event : legacy_public.CalendarEvent -> public.calendar_events
- trust_account : legacy_public.TrustBankAccount -> public.trust_accounts
- trust_transaction : legacy_public.TrustTransaction -> public.trust_transactions
- invoice : legacy_public.Invoice -> public.invoices
- document : legacy_public.Document -> public.documents
- integration_connection : legacy_public.IntegrationConnection -> public.integration_connections

## FK Rewire Seti

- matters.client_id <- client
- matters.responsible_user_id <- user
- matter_clients.matter_id <- matter
- matter_clients.client_id <- client
- matter_notes.matter_id <- matter
- tasks.matter_id <- matter
- tasks.assigned_user_id <- user
- trust_matter_ledgers.matter_id <- matter
- trust_matter_ledgers.client_id <- client
- trust_transactions.trust_account_id <- trust_account
- trust_transactions.matter_id <- matter
- invoices.client_id <- client
- invoices.matter_id <- matter
- documents.matter_id <- matter
- client_portal_accounts.client_id <- client
- integration_runs.connection_id <- integration_connection

## Wave Ozeti

## etl-001 - Identity and organization

- kaynak tablolar: 7
- hedef tablolar: 9
- transform kurali: 3
- verification profili: identity-core
- script: supabase/etl/sql/010_identity_and_org.sql

Verification:
- row-count: tenants
- row-count: firm_entities
- row-count: offices
- row-count: users
- row-count: staff_profiles
- orphan-check: staff_profiles.user_id
- orphan-check: user_role_assignments.user_id
- orphan-check: auth_sessions.user_id
- sample-check: sample admin user role split
- sample-check: tenant slug uniqueness

## etl-002 - Clients and matters

- kaynak tablolar: 9
- hedef tablolar: 12
- transform kurali: 9
- verification profili: crm-matter-core
- script: supabase/etl/sql/020_clients_matters.sql

Verification:
- row-count: clients
- row-count: leads
- row-count: matters
- row-count: matter_clients
- row-count: matter_notes
- orphan-check: matters.client_id
- orphan-check: matter_clients.client_id
- orphan-check: matter_notes.matter_id
- sample-check: sample matter responsible_user_id resolution
- sample-check: sample note revision chain

## etl-003 - Workflow

- kaynak tablolar: 4
- hedef tablolar: 5
- transform kurali: 3
- verification profili: workflow-core
- script: supabase/etl/sql/030_workflow.sql

Verification:
- row-count: tasks
- row-count: calendar_events
- row-count: deadlines
- row-count: notifications
- orphan-check: tasks.assigned_user_id
- orphan-check: calendar_events.matter_id
- sample-check: sample task status history
- sample-check: sample reminder carry-over

## etl-004 - Trust

- kaynak tablolar: 11
- hedef tablolar: 13
- transform kurali: 3
- verification profili: trust-core
- script: supabase/etl/sql/040_trust.sql

Verification:
- row-count: trust_accounts
- row-count: trust_matter_ledgers
- row-count: trust_transactions
- row-count: trust_journal_entries
- orphan-check: trust_transactions.trust_account_id
- orphan-check: trust_transaction_allocations.trust_matter_ledger_id
- sample-check: sample allocation expansion from json
- sample-check: sample journal balance parity

## etl-005 - Billing

- kaynak tablolar: 12
- hedef tablolar: 13
- transform kurali: 2
- verification profili: billing-core
- script: supabase/etl/sql/050_billing.sql

Verification:
- row-count: time_entries
- row-count: expenses
- row-count: invoices
- row-count: invoice_line_items
- row-count: billing_payments
- orphan-check: invoices.client_id
- orphan-check: invoice_line_items.invoice_id
- orphan-check: billing_payment_allocations.invoice_id
- sample-check: sample invoice numbering dedupe
- sample-check: sample payment allocation parity

## etl-006 - Documents portal and communications

- kaynak tablolar: 13
- hedef tablolar: 16
- transform kurali: 2
- verification profili: documents-portal-core
- script: supabase/etl/sql/060_documents_portal.sql

Verification:
- row-count: documents
- row-count: document_versions
- row-count: client_portal_accounts
- row-count: client_messages
- orphan-check: document_versions.document_id
- orphan-check: client_portal_accounts.client_id
- orphan-check: signature_requests.document_id
- sample-check: sample document version chain
- sample-check: sample portal account split

## etl-007 - Integrations

- kaynak tablolar: 10
- hedef tablolar: 10
- transform kurali: 1
- verification profili: integration-core
- script: supabase/etl/sql/070_integrations.sql

Verification:
- row-count: integration_connections
- row-count: integration_runs
- row-count: integration_outbox_events
- orphan-check: integration_runs.connection_id
- orphan-check: integration_entity_links.connection_id
- sample-check: sample pending outbox replay candidate
- sample-check: sample connection secret reference rewrite

