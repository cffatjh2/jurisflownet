# Matters, Notes, and Trust Remediation Plan

## Scope
This plan is based on code verification of the following files:

- `JurisFlow.Server/Controllers/MattersController.cs`
- `JurisFlow.Server/Controllers/MatterNotesController.cs`
- `JurisFlow.Server/Controllers/TrustController.cs`
- `JurisFlow.Server/Models/Matter.cs`
- `JurisFlow.Server/Models/MatterNote.cs`
- `JurisFlow.Server/Models/MatterClientLink.cs`

The verification also included supporting infrastructure that materially affects the risk profile:

- `JurisFlow.Server/Services/MatterAccessService.cs`
- `JurisFlow.Server/Services/FirmStructureService.cs`
- `JurisFlow.Server/Services/AuditLogger.cs`
- `JurisFlow.Server/Services/TrustActionAuthorizationService.cs`
- `JurisFlow.Server/Services/TrustAccountingOptions.cs`
- `JurisFlow.Server/Data/JurisFlowDbContext.cs`
- `JurisFlow.Server/Program.cs`

For a production rollout sequence tailored to the current `Render + Supabase` deployment model, see [MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md:1>).

## Executive Summary
The current implementation has a workable skeleton but is not release-ready for a legal SaaS production surface.

The highest-risk gaps are real and confirmed:

1. `MattersController` binds EF entities directly on create and update, which allows over-posting into fields that should not be client-controlled.
2. `MatterNotesController` uses read permission (`CanSeeMatterNotes`) to authorize create and update, which makes note mutation broader than intended.
3. `Matter` treats money and trust summary state as writable request data (`double BillableRate`, `double TrustBalance`), which conflicts with the stronger trust-ledger model already present elsewhere in the codebase.
4. `Matter` and `MatterNote` have no optimistic concurrency protection, so lost updates are expected under normal multi-user editing.
5. Matter lifecycle and deletion behavior are string-driven and controller-heavy, which is brittle and hard to operate safely.
6. Audit behavior is inconsistent across controllers: `MattersController` swallows audit failures, while `MatterNotesController` can fail the request after the business write has already succeeded.

Some claims in the external analysis need adjustment:

1. `MatterClientLink` duplicate protection already exists in `JurisFlowDbContext` via a unique index on `(TenantId, MatterId, ClientId)`.
2. Trust mutation and export actions are not protected only by `StaffOnly`; many of them are additionally gated in service code through `TrustActionAuthorizationService` and `TrustAccountingOptions.RoleMatrix`.
3. Tenant filtering is not absent. `JurisFlowDbContext` adds a shadow `TenantId` property and a global query filter for all entities, plus a special matter filter for `Status != "Deleted"`.

Those partial protections are not enough to clear the release gate. The resource scope, API contract, and concurrency model are still too loose for production.

## Verification Matrix

### Confirmed Release Blockers

1. Entity binding and over-posting in matters
Evidence:
- `PostMatter(Matter matter)` and `PutMatter(string id, Matter matter)` accept the EF entity directly.
- `PutMatter` explicitly copies writable request values into sensitive fields including `OpenDate`, `BillableRate`, `TrustBalance`, `CurrentOutcomeFeePlanId`, conflict flags, and sharing flags.
Impact:
- Mass assignment into workflow, financial, sharing, and audit-adjacent fields.
Required fix:
- Replace entity binding with request and response DTOs plus explicit mapping.

2. Client existence check without full resource scope validation
Evidence:
- `ResolveValidClientIdAsync` only checks `AnyAsync(c => c.Id == normalizedClientId)`.
- `FirmStructureService.ResolveEntityOfficeAsync` validates active entity and office existence, but not whether the current user is allowed to bind the matter to that client, entity, or office.
Impact:
- Tenant filtering helps, but controller-level write authorization still does not prove that the actor is allowed to use the referenced client or organizational scope.
Required fix:
- Add resource-aware validators for client, entity, office, and matter writes.

3. Writable trust summary and weak money typing on `Matter`
Evidence:
- `Matter.BillableRate` is `double`.
- `Matter.TrustBalance` is `double`.
- `PutMatter` assigns `existingMatter.TrustBalance = matter.TrustBalance`.
Impact:
- Precision risk and a second, user-editable source of truth for trust state.
Required fix:
- Convert money and rate fields to `decimal`, and remove trust balance from the write model.

4. Notes create and update are authorized by read permission
Evidence:
- `CreateNote` and `UpdateNote` both check `_matterAccess.CanSeeMatterNotes(matter, User)`.
- `DeleteNote` is stricter and allows delete only for privileged users, creators, or matter managers.
Impact:
- A user who may only read notes can also create and edit them.
Required fix:
- Split note permissions into read, create, edit, and delete decisions.

5. Audit strategy is inconsistent and unsafe
Evidence:
- `MattersController.TryAuditAsync` catches and suppresses all exceptions.
- `MatterNotesController` awaits `_auditLogger.LogAsync(...)` directly after `SaveChangesAsync(...)`.
- `AuditLogger` defaults to fail-closed in production through `Security:AuditLogFailClosed`.
Impact:
- Matter changes can succeed without audit.
- Note changes can persist in the database and still return 500 if audit enqueue fails.
Required fix:
- Use one consistent audit delivery model with transactional guarantees or a proper outbox.

6. No optimistic concurrency on `Matter` and `MatterNote`
Evidence:
- `Matter` has no row version or concurrency token.
- `MatterNote` has no row version or concurrency token.
- Repository-wide search shows trust entities and calendar events have `RowVersion`, but matters and matter notes do not.
Impact:
- Silent last-write-wins behavior for core matter and note edits.
Required fix:
- Add a concurrency token and return `409 Conflict` on stale updates.

7. Lifecycle model is string-based and restore semantics are lossy
Evidence:
- `DeletedMatterStatus` is a controller constant.
- `GetMatters` treats `"Open"` as anything not `"Archived"` and not `"Closed"`.
- `RestoreMatter` forces `matter.Status = "Open"`.
Impact:
- New statuses can be misclassified, and restore loses the prior lifecycle state.
Required fix:
- Replace ad hoc strings with an explicit state model and separated system flags.

8. Delete flow is too large and operationally fragile
Evidence:
- `DeleteMatter` manually clears or deletes a large dependency graph from controller code.
- The flow tolerates missing tables and columns at runtime through `TableHasColumnAsync`, `SafeDeleteAsync`, and `SafeClearMatterReferenceAsync`.
Impact:
- High regression risk, hard deployment guarantees, and controller code owning domain cleanup policy.
Required fix:
- Move deletion orchestration into an application service and stop relying on runtime schema drift tolerance for production safety.

9. Matter domain fields lack strong constraints
Evidence:
- `CaseNumber`, `Name`, `PracticeArea`, `Status`, `FeeStructure`, and `ResponsibleAttorney` are broad strings in `Matter`.
- No case-number uniqueness or status check constraint was found in `JurisFlowDbContext`.
Impact:
- Weak data quality, duplicate case numbers, and unstable filtering.
Required fix:
- Add max lengths, normalized columns, uniqueness, and constrained value sets.

10. `ResponsibleAttorney` is free text
Evidence:
- `Matter.ResponsibleAttorney` is a required string.
Impact:
- Broken referential integrity, unstable reporting, and awkward future authorization rules.
Required fix:
- Replace with a user foreign key and expose display name through projections.

11. Matter audit fields are incomplete
Evidence:
- `Matter` has `CreatedByUserId` but no `CreatedAt`, `UpdatedAt`, or `UpdatedByUserId`.
- `MatterNote` already carries both created and updated timestamps and user ids.
Impact:
- Inconsistent audit metadata across closely related aggregates.
Required fix:
- Standardize audit columns across mutable domain entities.

12. No case-number uniqueness on matters
Evidence:
- No unique index or normalized case-number field was found for `Matter`.
Impact:
- Duplicate matter identifiers inside a tenant are currently allowed unless blocked elsewhere.
Required fix:
- Add a normalized unique index at the tenant scope.

13. API-only related-client fields live on the persistence entity
Evidence:
- `Matter.RelatedClientIds` and `Matter.RelatedClients` are `[NotMapped]`.
- Secondary client writes are currently disabled through `EnableSecondaryClientWrites => false`.
Impact:
- Persistence and API shape are mixed, and the public contract exposes inactive feature behavior.
Required fix:
- Move those fields to response DTOs and hide inactive write paths.

14. Note body is raw text with no sanitization policy
Evidence:
- `MatterNote.Body` is stored and returned as-is.
- `NormalizeBody` only trims and truncates.
- No sanitizer or rendering contract for notes was found.
Impact:
- Stored XSS remains a real risk if the frontend ever renders note bodies as HTML or rich text.
Required fix:
- Define notes as plain text or sanitize rich text before storage and rendering.

15. Note body has controller-only length enforcement
Evidence:
- `MatterNote.Body` has `[Required]` but no `[MaxLength]`.
- `NormalizeBody` truncates to 10000 characters only in controller code.
Impact:
- Invariant is not enforced at the model or database layer.
Required fix:
- Add model-level and database-level limits.

16. No note revision history or soft delete
Evidence:
- `DeleteNote` removes the row directly.
- `UpdateNote` overwrites the body in place.
Impact:
- No durable history for legal notes.
Required fix:
- Add revision storage and soft-delete semantics.

17. Pagination is missing or weak
Evidence:
- `GetMatters` returns the full query result.
- `GetNotes` returns the full note list.
- `GetTransactions` uses `limit` but does not clamp it to a server-side maximum.
Impact:
- Performance and abuse risk, especially on large tenants.
Required fix:
- Add paging contracts and server-side caps.

18. Some trust reads still use tracking queries and entity graph returns
Evidence:
- `GetTrustAccounts`, `GetLedgers`, `GetTransactions`, and `GetReconciliations` do not use `AsNoTracking`.
- `GetLedgers` and `GetTransactions` return included entities directly.
Impact:
- Unnecessary tracking overhead and broader-than-needed response surfaces.
Required fix:
- Standardize trust reads on no-tracking projection DTOs.

19. No rate limiting is applied to these controllers
Evidence:
- `Program.cs` defines named rate-limit policies.
- Repository search shows rate limiting is used on other endpoints, but not on the matter, note, or trust endpoints under review.
Impact:
- Mutation and search surfaces are easier to abuse.
Required fix:
- Apply endpoint-appropriate rate limiting and request body size limits.

### Partially Confirmed or Revised Findings

1. Tenant isolation is present, but not sufficient
Verified state:
- `JurisFlowDbContext` adds a shadow `TenantId` property plus a global query filter for all entities.
- `Matter` adds an extra filter to exclude `Status == "Deleted"`.
Revision:
- The problem is not "no tenant filter." The problem is that resource authorization still does not prove allowed use of referenced client, entity, office, and trust resources for the current actor.

2. Trust authorization is partially stronger than the original analysis suggested
Verified state:
- `TrustActionAuthorizationService` enforces role-based action authorization.
- `TrustAccountingService`, `TrustComplianceExportService`, `TrustRecoveryService`, and `TrustStatementIngestionService` call `EnsureAllowed(...)` for many mutating operations.
- Default role matrix already distinguishes actions such as approve, signoff, override, export, and policy management.
Revision:
- The real gap is narrower and more precise:
  - broad trust read endpoints are still exposed to any `StaffOnly` actor
  - the controller surface does not express the narrower policy model clearly
  - read authorization is not resource-scoped the way matter access is

3. `MatterClientLink` duplicate protection already exists
Verified state:
- `JurisFlowDbContext` defines a unique index on `(TenantId, MatterId, ClientId)`.
Revision:
- No immediate duplicate-fix migration is needed for that exact rule.
- Follow-up improvements still make sense: add explicit relationship type, stronger audit metadata, and keep the index when refactoring the model.

4. Trust concurrency is ahead of matter concurrency
Verified state:
- `TrustTransaction`, `ClientTrustLedger`, and `TrustBankAccount` already have `RowVersion` plus `[ConcurrencyCheck]`.
- Trust service code refreshes row versions on mutation.
Revision:
- The concurrency finding remains valid for `Matter` and `MatterNote`, but it should not be presented as a trust-wide absence of concurrency control.

5. Money handling is inconsistent, not uniformly broken
Verified state:
- Trust subsystem models already use `decimal`.
- The weak money typing under review is specifically on `Matter`.
Revision:
- The plan should remove money-like writable fields from matter APIs rather than imply the entire trust system uses `double`.

6. String ids are a long-term improvement, not a release blocker for this wave
Verified state:
- Current ids are string-backed GUID values across much of the codebase.
Revision:
- Moving all ids to `Guid` database types is a large cross-cutting migration and should not block the first hardening pass unless there is a clear operational requirement.

## Assumptions for This Remediation Wave

1. Keep existing string ids for now. Do not couple the release-hardening work to a full id-type migration.
2. Keep the current tenant shadow-property model for this pass, but strengthen resource authorization around it.
3. Preserve the existing trust role matrix and make it explicit at the controller boundary rather than rewriting the trust authorization stack from scratch.
4. Treat trust balance on `Matter` as a derived read concern. Do not keep any client-writable path for it.

## Target Release Criteria
The release gate should remain closed until the following are true:

1. Matter create and update use DTO whitelists and reject unmapped payload fields.
2. Matter and note updates use optimistic concurrency and surface stale writes as `409 Conflict`.
3. Note create, edit, and delete permissions are distinct and covered by tests.
4. Trust read endpoints no longer expose broad data access to every `StaffOnly` role.
5. Matter money-like fields are no longer weakly typed or client-writable in the wrong places.
6. Case-number uniqueness and core status constraints exist at the database level.
7. Audit behavior is consistent and reliable for matter and note mutations.
8. Large list endpoints are paginated and capped.
9. Deletion and lifecycle behavior have explicit rules and tests.

## Workstream Plan

### Workstream 1: API Contract Hardening
Goal:
- Eliminate direct entity binding and separate persistence shape from API shape.

Tasks:
1. Add new matter contracts:
- `CreateMatterRequest`
- `UpdateMatterRequest`
- `MatterResponse`
- `MatterListItemResponse`

2. Restrict writeable matter fields to a deliberate whitelist.

3. Remove the following from the write contract:
- `TrustBalance`
- `CurrentOutcomeFeePlanId`
- `ConflictCheckCleared`
- `ConflictWaiverObtained`
- `OpenDate`
- `CreatedByUserId`
- direct sharing flags unless product owner explicitly confirms they belong in this endpoint

4. Move `RelatedClientIds` and `RelatedClients` out of `Matter` and into response DTOs.

5. Replace anonymous-object error bodies with `ProblemDetails`.

6. Add FluentValidation for request contracts.

Acceptance criteria:
1. Unknown or blocked fields cannot change persisted state.
2. API responses no longer expose EF entities directly.
3. Validation rules are enforced independently of controller mapping code.

### Workstream 2: Authorization and Resource Scope
Goal:
- Make write and read decisions explicit, resource-based, and testable.

Tasks:
1. Extend `MatterAccessService` with dedicated methods:
- `CanCreateMatterAsync(...)`
- `CanUpdateMatterAsync(...)`
- `CanReadMatterNotes(...)`
- `CanCreateMatterNote(...)`
- `CanEditMatterNote(...)`
- `CanDeleteMatterNote(...)`

2. Add client, entity, office, and matter resource validators that confirm:
- same tenant
- valid organizational scope
- current actor is allowed to use the referenced resource

3. Refactor `MatterNotesController` to use the new note-specific permission methods.

4. Introduce narrower trust policies for controller reads:
- `TrustRead`
- `TrustManage`
- `TrustApprove`
- `TrustExport`

5. Apply those policies at the controller or action level so the narrower intent is visible before service code executes.

6. Decide where `NotFound` should be returned instead of `Forbid` to avoid resource enumeration.

Acceptance criteria:
1. Read-only note users cannot create or edit notes.
2. Matter create and update reject out-of-scope client, entity, and office references.
3. Trust reads are not available to every generic staff role by default.

### Workstream 3: Domain Model Hardening
Goal:
- Strengthen matter and note entities so core invariants are enforced below the controller layer.

Tasks for `Matter`:
1. Add:
- `CreatedAt`
- `UpdatedAt`
- `UpdatedByUserId`
- `RowVersion`
- `CaseNumberNormalized`
- `NameNormalized`

2. Replace or phase out:
- `ResponsibleAttorney` -> `ResponsibleAttorneyUserId`
- `BillableRate` -> `decimal`
- `TrustBalance` -> remove from write model and entity if possible, otherwise mark read-only and stop persisting user input

3. Define max lengths for free-text and identifier-like fields.

4. Convert `Status` and `FeeStructure` to constrained values.

Tasks for `MatterNote`:
1. Add:
- `[MaxLength]` on `Body`
- `RowVersion`
- `Visibility`
- `IsDeleted`
- `DeletedAt`
- `DeletedByUserId`

2. Decide whether note history is:
- separate revision table
- append-only version table
- immutable note events with reconstructed current state

Tasks for `MatterClientLink`:
1. Preserve the existing unique index.
2. Add `RelationshipType`.
3. Add audit metadata if links remain mutable.

Acceptance criteria:
1. Core invariants are present in model configuration and migration output.
2. Matter and note state cannot rely on controller-only validation.

### Workstream 4: Persistence and Indexing
Goal:
- Add the missing database rules that protect correctness and performance.

Tasks:
1. Add a normalized unique index for case number, for example:
- `(TenantId, CaseNumberNormalized)` unique

2. Add matter indexes that match current access patterns:
- `(TenantId, Status, OpenDate)`
- `(TenantId, ClientId)`
- `(TenantId, EntityId, OfficeId, Status)`
- `(TenantId, ResponsibleAttorneyUserId, Status)` after the FK migration

3. Keep existing `MatterClientLink` unique index and note indexes.

4. Add check constraints or enum conversions for:
- matter status
- fee structure
- note visibility if modeled as a string

5. Add row-version columns and concurrency configuration for matter and notes.

Acceptance criteria:
1. Duplicate case numbers cannot be inserted within the allowed scope.
2. Status and fee-structure garbage values are rejected by the database.
3. Concurrency tokens exist and are enforced.

### Workstream 5: Audit and Outbox Reliability
Goal:
- Make audit and downstream workflow dispatch reliable and uniform.

Tasks:
1. Remove controller-level audit swallowing for matters.

2. Stop writing business data and then independently attempting audit in a way that can fail after commit.

3. Choose one delivery strategy:
- same-transaction audit record insert
- outbox table with guaranteed background delivery

4. Capture before and after values for matter and note edits, not just free-form text details.

5. Apply the same pattern to workflow dispatch currently done by `TryTriggerOutcomeFeePlannerAsync`.

Acceptance criteria:
1. Mutation success and audit reliability follow one consistent rule.
2. A failure mode cannot leave the API returning success with no audit on one path and returning 500 after commit on another.

### Workstream 6: Lifecycle and Delete Refactor
Goal:
- Make matter archive, restore, and delete behavior explicit and recoverable.

Tasks:
1. Replace string-only lifecycle handling with an explicit status model plus separate system flags when needed.

2. Introduce lifecycle metadata:
- `ArchivedAt`
- `ArchivedByUserId`
- `ClosedAt`
- `ClosedByUserId`
- `DeletedAt`
- `DeletedByUserId`

3. Define restore semantics precisely:
- restore from archive only
- restore deleted tombstone only
- restore to prior lifecycle state, not always `"Open"`

4. Move the delete orchestration out of `MattersController` into a dedicated service.

5. Remove production reliance on runtime "missing schema is acceptable" cleanup logic.

Acceptance criteria:
1. Restore does not silently destroy prior lifecycle state.
2. Delete behavior is service-owned, documented, and testable.

### Workstream 7: Performance and Abuse Controls
Goal:
- Reduce unnecessary data exposure and make heavy endpoints safe under growth.

Tasks:
1. Add pagination to:
- `GetMatters`
- `GetNotes`
- trust list endpoints

2. Clamp all page sizes and `limit` values server-side.

3. Convert trust read endpoints to projection DTOs with `AsNoTracking`.

4. Add request body size limits for matter and note writes.

5. Apply rate limiting policies to matter, note, and trust mutation endpoints.

Acceptance criteria:
1. Unbounded result sets are removed.
2. Tracking overhead is eliminated from read-only trust endpoints.
3. Abuse protections match the sensitivity of the endpoints.

## File-by-File Implementation Outline

### `JurisFlow.Server/Controllers/MattersController.cs`
Planned changes:
1. Replace entity binding with DTO binding.
2. Delegate validation and mapping to a service layer.
3. Stop returning `Matter` directly.
4. Remove client-writable trust and workflow fields.
5. Refactor lifecycle operations to use explicit commands and concurrency.
6. Remove best-effort audit and workflow trigger behavior from controller code.

### `JurisFlow.Server/Controllers/MatterNotesController.cs`
Planned changes:
1. Replace `CanSeeMatterNotes` authorization on create and update.
2. Add paging for `GetNotes`.
3. Add concurrency token handling to update and delete.
4. Use note response DTOs that include visibility and row version if needed.
5. Move note normalization and sanitization policy out of the controller.

### `JurisFlow.Server/Controllers/TrustController.cs`
Planned changes:
1. Split broad `StaffOnly` read surface into narrower policies.
2. Standardize trust reads on projected DTOs and `AsNoTracking`.
3. Clamp list limits.
4. Replace direct entity returns where sensitive fields do not belong in API responses.

### `JurisFlow.Server/Models/Matter.cs`
Planned changes:
1. Add audit and concurrency fields.
2. Tighten text constraints.
3. Remove API-only `[NotMapped]` response fields.
4. Replace free-text responsible attorney with a stable reference.
5. Remove or demote trust balance to a derived projection concern.

### `JurisFlow.Server/Models/MatterNote.cs`
Planned changes:
1. Add length constraints and concurrency.
2. Add visibility and soft-delete metadata.
3. Prepare for revision history.

### `JurisFlow.Server/Models/MatterClientLink.cs`
Planned changes:
1. Keep the existing uniqueness rule.
2. Add relationship semantics and stronger metadata.

### Supporting Files
Expected changes will also be required in:

- `JurisFlow.Server/Services/MatterAccessService.cs`
- `JurisFlow.Server/Services/FirmStructureService.cs`
- `JurisFlow.Server/Data/JurisFlowDbContext.cs`
- migrations under `JurisFlow.Server/Migrations`
- new DTO and validator files
- likely one or more new application services for matter commands and lifecycle operations

## Test Plan

### Security and Authorization Tests
1. Over-posting test: blocked fields in matter payload do not persist.
2. IDOR test: user cannot bind a matter to an out-of-scope client.
3. Note permission test: note reader cannot create, edit, or delete unless explicitly allowed.
4. Trust read authorization test: generic staff roles cannot view restricted trust data after the policy split.

### Concurrency Tests
1. Two users update the same matter with stale row versions; second write returns `409`.
2. Two users edit the same note with stale row versions; second write returns `409`.

### Data Integrity Tests
1. Duplicate case numbers inside one tenant are rejected.
2. Invalid status and fee structure values are rejected.
3. Trust balance cannot be written through matter APIs.

### Lifecycle and Deletion Tests
1. Archive and restore preserve intended prior state.
2. Delete path behaves deterministically and logs the correct audit trail.
3. Soft-delete or tombstone flow is covered if hard delete remains disabled or rare.

### Reliability Tests
1. Audit failure path behaves consistently with the chosen fail-open or fail-closed strategy.
2. Workflow dispatch failure follows the outbox or transactional rule, not best effort.

### Performance and Abuse Tests
1. Paginated reads enforce max page size.
2. Trust transaction list clamps `limit`.
3. Rate limiting policies trigger on repeated writes.

## Recommended PR Split
Do not land this as one giant change set. Recommended sequence:

1. PR 1: tests that capture the current failures and target behavior
2. PR 2: matter DTOs, validation, and response shape separation
3. PR 3: note permission split, note constraints, and note concurrency
4. PR 4: matter model hardening, lifecycle cleanup, and database constraints
5. PR 5: trust read authorization tightening, projections, and pagination
6. PR 6: audit and outbox unification

## Suggested Go-Live Gate
Do not mark this surface production-ready until all items below are complete:

1. DTO whitelist for matters
2. note permission split
3. matter and note concurrency
4. trust read policy split
5. case-number uniqueness
6. removal of writable trust balance from matter writes
7. consistent audit delivery model
8. pagination and rate limiting on high-value endpoints

## Post-Release Backlog
These are valid improvements, but should not block the first remediation wave:

1. Full `string` to `Guid` id migration across the codebase
2. Explicit first-class `TenantId` properties on every relevant domain model instead of relying mainly on shadow properties
3. Larger domain-state redesign for matter lifecycle with a formal state machine
4. Full event-sourced or append-only revision history if that becomes a wider platform standard
