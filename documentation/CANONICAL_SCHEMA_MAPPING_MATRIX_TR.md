# Legacy to Canonical Mapping Matrix

Bu dokuman tablo bazli migration karar setidir. Her legacy tablo icin canonical hedef, tasinma kurali ve tasinmama gerekcesi belirtilir.

Kisaltmalar:
- `Evet`: veri tasinacak
- `Kosullu`: veri temizleme veya transform sonrasi tasinacak
- `Hayir`: canonical v1'e tasinmayacak

## Foundation / Identity

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `Tenant` | `tenants` | Evet | alanlar normalize edilerek bire bir tasinir | `slug`, `name`, `status` temizlenir |
| `FirmEntity` | `firm_entities` | Evet | legacy id -> canonical id map | `code` yoksa uretilir |
| `Office` | `offices` | Evet | entity baglari korunur | eksik entity baglari once temizlenir |
| `User` | `users`, `staff_profiles`, `user_role_assignments` | Kosullu | auth alanlari `users`; personel detaylari `staff_profiles`; `Role` string'i role assignment'a doner | `EmployeeRole` string'i job title/profile alanina tasinir |
| `AuthSession` | `auth_sessions` | Kosullu | aktif sessionlar tasinabilir | eski expired session'lar tasinmaz |
| `MfaChallenge` | `mfa_challenges` | Hayir | transient veri | yeni DB'de yeniden uretilir |
| `AuditLog` | `audit_logs` | Kosullu | sequence yeniden kurulabilir | sadece retention penceresindeki kayitlar tasinir |
| `RetentionPolicy` | `retention_policies` | Evet | policy anahtarlari normalize edilir | |

## CRM / Matter Core

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `Client` | `clients`, `client_portal_accounts` | Kosullu | ana musteri kaydi `clients`; portal auth alanlari `client_portal_accounts` | organization contact ayrimi gerekiyorsa split edilir |
| `ClientStatusHistory` | `client_status_history` | Evet | append-only tasinir | durum degerleri enum'a map edilir |
| `Lead` | `leads` | Evet | status/source normalize edilir | invalid durumlar fallback alir |
| `Matter` | `matters` | Kosullu | `ResponsibleAttorney` -> `responsible_user_id`; `TrustBalance` tasinmaz; `BillableRate` decimal'a doner | `Status` ve `FeeStructure` enum normalize edilir |
| `MatterClientLink` | `matter_clients` | Evet | `relationship_type` yoksa `primary` veya `co_client` uretilir | duplicate linkler merge edilir |
| `Matter` + user/employee alanlari | `matter_staff_assignments` | Kosullu | `CreatedByUserId` ve `ResponsibleAttorney` uzerinden ilk assignment seti uretilir | manuel review gerekebilir |
| `MatterNote` | `matter_notes`, `matter_note_revisions` | Evet | mevcut note aktif surum olur; rev1 kaydi da olusturulur | visibility default `internal` |
| `OpposingParty` | `opposing_parties` | Evet | isimler normalize edilir | |
| `ConflictCheck` | `conflict_checks` | Evet | status map edilir | |
| `ConflictResult` | `conflict_results` | Evet | severity normalize edilir | |

## Workflow

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `Task` | `tasks`, `task_status_history` | Evet | mevcut durum `tasks`; ilk tarihce kaydi uretilir | `AssignedTo` text varsa `assigned_user_id` adapter ile cozulur |
| `CalendarEvent` | `calendar_events` | Evet | `RowVersion` korunur veya yeniden uretilir | unsupported event type `general` olur |
| `Deadline` | `deadlines` | Evet | kural/ref alanlari normalize edilir | |
| `Notification` | `notifications` | Kosullu | sadece aktif/okunmamis veya son N gun tasinir | eski backlog tasinmaz |

## Trust

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `TrustBankAccount` | `trust_accounts` | Evet | hesap metadata normalize edilir | |
| `ClientTrustLedger` | `trust_matter_ledgers` | Kosullu | matter + client + account baglari yeniden kurulur | orphan ledger'lar raporlanir |
| `TrustPostingBatch` | `trust_posting_batches` | Evet | command id ve status korunur | |
| `TrustTransaction` | `trust_transactions` | Evet | decimal alanlar bire bir; status enum normalize edilir | posted kayitlar immutable kabul edilir |
| `TrustTransaction` + `AllocationsJson` | `trust_transaction_allocations` | Kosullu | JSON allocation payload satirlara ayrilir | parse edilemeyen kayitlar review queue'ya gider |
| `TrustJournalEntry` | `trust_journal_entries` | Evet | append-only tasinir | source of truth |
| daginik approval modelleri | `trust_approval_requests`, `trust_approval_decisions` | Kosullu | approval modelleri tek root altinda birlestirilir | karar izi korunur |
| `TrustMonthClose*` | `trust_month_closes` | Kosullu | step detail JSONB veya child table sonraki faz | v1'de root seviye tasinir |
| `TrustReconciliationSnapshot` | `trust_reconciliation_snapshots` | Evet | snapshot metadata tasinir | |
| `TrustReconciliationPacket` | `trust_reconciliation_packets` | Evet | packet status normalize edilir | |
| `TrustComplianceExport` | `trust_compliance_exports` | Evet | file reference document katmanina baglanabilir | |
| `TrustOperationalAlert` | `trust_operational_alerts` | Evet | severity/status normalize edilir | |
| `TrustOutstandingItem`, `ReconciliationRecord`, `TrustStatementImport`, `TrustPhase6Artifacts` | canonical v1 core disi | Hayir | archive/read-only veya sonraki moduller | cutover bloklamaz |
| `TrustRisk*` | canonical v1 core disi | Hayir | deneysel risk radar alani | sonraki faz |

## Billing / Payments

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `BillingRateCard` | `billing_rate_cards` | Evet | code/name normalize edilir | |
| `BillingRateCardEntry` | `billing_rate_card_entries` | Evet | role + UTBMS alanlari normalize edilir | |
| `MatterBillingPolicy` | `matter_billing_policies` | Evet | fee mode enum map | |
| `TimeEntry` | `time_entries` | Evet | minutes/rate decimal normalize edilir | |
| `Expense` | `expenses` | Evet | kategori ve reimbursement alanlari normalize edilir | |
| `Invoice` | `invoices` | Evet | invoice number unique cleanup gerekir | duplicate varsa remap |
| `InvoiceLineItem` | `invoice_line_items` | Evet | quantity/price kontrol edilir | |
| `InvoicePayorAllocation`, `InvoiceLinePayorAllocation` | `invoice_payor_allocations` | Kosullu | line-level detail gerekirse sonraki child table'a ayrilir | v1 root allocation ile acilir |
| `PaymentTransaction` | `billing_payments` | Evet | payment rail/status normalize edilir | provider metadata JSONB |
| `BillingPaymentAllocation` | `billing_payment_allocations` | Evet | invoice baglari korunur | |
| `PaymentPlan` | `payment_plans` | Evet | schedule JSON ayrisabilir | |
| `OutcomeFee*` | `outcome_fee_plans`, `outcome_fee_plan_versions` | Kosullu | root + version state tasinir; forecast alt detaylari sadeleşir | v1 sadeleştirilmis |

## Documents / Portal / Communications

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `Document` | `documents` | Evet | logical root olur | binary metadata ayrilir |
| `DocumentVersion` | `document_versions` | Evet | version zinciri korunur | |
| `DocumentShare` | `document_shares` | Evet | share scope enum'a map edilir | |
| `DocumentComment` | `document_comments` | Evet | author baglari temizlenir | |
| `DocumentContentIndex` | `document_content_indexes` | Kosullu | gerekiyorsa yeniden build edilebilir | rebuild tercih edilebilir |
| `DocumentContentToken` | canonical v1 core disi | Hayir | search pipeline intermediate veri | yeniden indekslenir |
| `SignatureRequest` | `signature_requests` | Evet | provider alanlari JSONB olabilir | |
| `SignatureAuditEntry` | `signature_audit_entries` | Evet | append-only tasinir | |
| `ClientMessage` | `client_messages` | Evet | direction/status normalize edilir | |
| `EmailMessage` | `email_messages` | Evet | provider message id canonical alana gider | |
| `OutboundEmail` | `outbound_emails` | Evet | delivery metadata korunur | |
| `SmsMessage` | `sms_messages` | Evet | provider metadata normalize edilir | |
| `AppointmentRequest` | `appointment_requests` | Evet | status normalize edilir | |
| `ClientTransparency*` | `client_transparency_*` | Kosullu | sadece client-facing gerekliler tasinir | review action/delay reason sonraki faz olabilir |

## Integrations

| Legacy | Canonical | Tasinacak mi | Mapping Kurali | Not |
| --- | --- | --- | --- | --- |
| `IntegrationConnection` | `integration_connections` | Evet | status/category normalize edilir | |
| `IntegrationSecret` | `integration_secrets` | Kosullu | secret plaintext tasinmaz; sadece secret reference veya vault key tasinir | rotate gerekebilir |
| `IntegrationRun` | `integration_runs` | Kosullu | sadece son N ay veya acik runlar | eski runlar archive |
| `IntegrationEntityLink` | `integration_entity_links` | Evet | entity type map gerekir | |
| `IntegrationMappingProfile` | `integration_mapping_profiles` | Evet | mapping payload JSONB | |
| `IntegrationInboxEvent` | `integration_inbox_events` | Kosullu | retention penceresindeki olaylar | |
| `IntegrationOutboxEvent` | `integration_outbox_events` | Kosullu | pending / failed item'lar oncelikli | |
| `IntegrationReviewQueueItem` | `integration_review_queue_items` | Evet | acik queue item'lar tasinir | |
| `IntegrationConflictQueueItem` | `integration_conflict_queue_items` | Evet | acik conflict item'lar tasinir | |
| `StripeWebhookEvent` | `stripe_webhook_events` | Kosullu | idempotency geregi son donem eventleri tasinabilir | gerekirse yeniden hydrate |

## Tasinmayacak veya Arsivlenecek Alanlar

| Legacy | Karar | Gerekce |
| --- | --- | --- |
| AI drafting tablolari | Tasinmaz | canonical legal ops core'u degil |
| App directory tablolari | Tasinmaz | marketplace modulu cutover bloklamaz |
| jurisdiction authoring/test harness'in tamami | Tasinmaz veya sonraki wave | operationally core degil |
| transient MFA challenge kayitlari | Tasinmaz | yeniden uretilir |
| eski notification backlog | Tasinmaz | veri yukunu azaltir |
| rebuild edilebilir search token/index kayitlari | tercihen tasinmaz | re-index daha dogru |

## Ozet

- Kesin tasinacak cekirdek set: tenant, user, client, matter, note, trust root, invoice, document, integration connection
- Temizlik gerektiren set: role split, matter staff assignment, trust allocation JSON, portal split, outcome-fee ve transparency alt modelleri
- Tasinmayabilecek set: AI, experimental, rebuildable ve transient tablolar

Bu karar seti Faz 6 ETL kapsam sinirini belirler.
