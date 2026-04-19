# Phase 6 Preview Report

- scenario: B
- scenario-name: minimal-seed-onboarding
- aciklama: Legacy veri alinmadan canonical DB yalnizca seed tenant, admin ve sistem lookup seti ile acilir.
- toplam transform kurali: 23
- toplam id-map tanimi: 15
- toplam fk-rewire tanimi: 16
- toplam verification profili: 7

## Gerekli Artefaktlar

- sql/100_minimal_seed_and_onboarding.template.sql : ok

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

## Scenario B Ciktilari

- minimal seed template hazir
- tenant/admin/office/foundational role assignment onboarding akisi tanimli
- execution yok; sadece preview ve parameterized SQL template var
