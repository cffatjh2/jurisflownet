namespace JurisFlow.Server.Contracts
{
    public class DepositRequest
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string PayorPayee { get; set; } = string.Empty;
        public string? CheckNumber { get; set; }
        public string? IdempotencyKey { get; set; }
        public List<AllocationDto> Allocations { get; set; } = new();
    }

    public class AllocationDto
    {
        public string LedgerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Description { get; set; }
    }

    public class WithdrawalRequest
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string LedgerId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string PayorPayee { get; set; } = string.Empty;
        public string? CheckNumber { get; set; }
        public string? DisbursementClass { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class ReconcileRequest
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string? PeriodStart { get; set; }
        public string PeriodEnd { get; set; } = string.Empty;
        public decimal BankStatementBalance { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public class TrustStatementImportRequest
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal StatementEndingBalance { get; set; }
        public string? Source { get; set; }
        public string? SourceFileName { get; set; }
        public string? SourceFileHash { get; set; }
        public string? SourceEvidenceKey { get; set; }
        public long? SourceFileSizeBytes { get; set; }
        public bool AllowDuplicateImport { get; set; }
        public string? Notes { get; set; }
        public List<TrustStatementLineDto> Lines { get; set; } = new();
    }

    public class TrustEvidenceFileRegisterRequest
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string Source { get; set; } = "manual_manifest";
        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string? EvidenceKey { get; set; }
        public long? FileSizeBytes { get; set; }
        public bool AllowDuplicateRegistration { get; set; }
        public string? Notes { get; set; }
    }

    public class TrustStatementParserRunCreateDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string TrustEvidenceFileId { get; set; } = string.Empty;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public string? ParserKey { get; set; }
        public decimal StatementEndingBalance { get; set; }
        public string? Source { get; set; }
        public bool AllowDuplicateImport { get; set; }
        public string? Notes { get; set; }
        public List<TrustStatementLineDto> Lines { get; set; } = new();
    }

    public class TrustStatementLineDto
    {
        public DateTime PostedAt { get; set; }
        public DateTime? EffectiveAt { get; set; }
        public decimal Amount { get; set; }
        public decimal? BalanceAfter { get; set; }
        public string? Reference { get; set; }
        public string? CheckNumber { get; set; }
        public string? Description { get; set; }
        public string? Counterparty { get; set; }
        public string? ExternalLineId { get; set; }
    }

    public class TrustStatementLineMatchDto
    {
        public string Action { get; set; } = "match"; // match | ignore | reject | unmatch
        public string? TrustTransactionId { get; set; }
        public string? Notes { get; set; }
    }

    public class TrustStatementMatchingRunResultDto
    {
        public string TrustStatementImportId { get; set; } = string.Empty;
        public int TotalLineCount { get; set; }
        public int MatchedLineCount { get; set; }
        public int UnmatchedLineCount { get; set; }
        public int IgnoredLineCount { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    public class TrustOutstandingItemCreateDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustTransactionId { get; set; }
        public string? ClientTrustLedgerId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime? OccurredAt { get; set; }
        public string ItemType { get; set; } = "other_adjustment";
        public string ImpactDirection { get; set; } = "decrease_bank";
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
        public string? Description { get; set; }
        public string? ReasonCode { get; set; }
        public string? AttachmentEvidenceKey { get; set; }
    }

    public class TrustReconciliationPacketCreateDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string? StatementImportId { get; set; }
        public decimal? StatementEndingBalance { get; set; }
        public string? Notes { get; set; }
        public bool ForceNewVersion { get; set; }
        public string? SupersedePacketId { get; set; }
        public string? SupersedeReason { get; set; }
    }

    public class TrustReconciliationPacketSignoffDto
    {
        public string? Notes { get; set; }
    }

    public class TrustReconciliationPacketSupersedeDto
    {
        public string Reason { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? StatementImportId { get; set; }
        public decimal? StatementEndingBalance { get; set; }
    }

    public class TrustRejectDto
    {
        public string? Reason { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustClearDepositDto
    {
        public DateTime? ClearedAt { get; set; }
        public string? Notes { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustReturnDepositDto
    {
        public string? Reason { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustVoidDto
    {
        public string? Reason { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustApproveStepDto
    {
        public string? RequirementType { get; set; }
        public string? Notes { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustOverrideDto
    {
        public string Reason { get; set; } = string.Empty;
        public string? RequirementType { get; set; }
        public string? MetadataJson { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class TrustEarnedFeeTransferCommand
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string LedgerId { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? PayorPayee { get; set; }
        public string? Reference { get; set; }
        public string? BillingPaymentAllocationId { get; set; }
        public string? PaymentTransactionId { get; set; }
        public string? InvoiceId { get; set; }
        public DateTime? EffectiveAt { get; set; }
        public bool ShadowPolicyOnly { get; set; } = true;
    }

    public class TrustProjectionRebuildRequest
    {
        public string? TrustAccountId { get; set; }
        public bool OnlyIfDrifted { get; set; } = true;
    }

    public class TrustProjectionRebuildResult
    {
        public DateTime RebuiltAt { get; set; }
        public int AccountCount { get; set; }
        public int LedgerCount { get; set; }
        public int DriftedAccountCount { get; set; }
        public int DriftedLedgerCount { get; set; }
        public List<string> TrustAccountIds { get; set; } = new();
    }

    public class TrustProjectionHealthResponse
    {
        public DateTime GeneratedAt { get; set; }
        public List<TrustProjectionHealthAccountDto> Accounts { get; set; } = new();
    }

    public class TrustProjectionHealthAccountDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public decimal ProjectedCurrentBalance { get; set; }
        public decimal ProjectedClearedBalance { get; set; }
        public decimal ProjectedUnclearedBalance { get; set; }
        public decimal ProjectedAvailableDisbursementCapacity { get; set; }
        public decimal JournalCurrentBalance { get; set; }
        public decimal JournalClearedBalance { get; set; }
        public decimal JournalUnclearedBalance { get; set; }
        public decimal ExpectedAvailableDisbursementCapacity { get; set; }
        public bool HasDrift { get; set; }
        public List<TrustProjectionHealthLedgerDto> DriftedLedgers { get; set; } = new();
    }

    public class TrustProjectionHealthLedgerDto
    {
        public string LedgerId { get; set; } = string.Empty;
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public decimal HoldAmount { get; set; }
        public decimal ProjectedRunningBalance { get; set; }
        public decimal ProjectedClearedBalance { get; set; }
        public decimal ProjectedUnclearedBalance { get; set; }
        public decimal ProjectedAvailableToDisburse { get; set; }
        public decimal JournalRunningBalance { get; set; }
        public decimal JournalClearedBalance { get; set; }
        public decimal JournalUnclearedBalance { get; set; }
        public decimal ExpectedAvailableToDisburse { get; set; }
        public bool HasDrift { get; set; }
    }

    public class CreateTrustAccountRequest
    {
        public string Name { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string RoutingNumber { get; set; } = string.Empty;
        public string? AccountNumber { get; set; }
        public string? AccountNumberEnc { get; set; }
        public string Jurisdiction { get; set; } = string.Empty;
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
    }

    public class TrustAccountGovernanceDto
    {
        public string AccountType { get; set; } = "iolta";
        public string? ResponsibleLawyerUserId { get; set; }
        public List<string> AllowedSignatories { get; set; } = new();
        public string? JurisdictionPolicyKey { get; set; }
        public string StatementCadence { get; set; } = "monthly";
        public bool OverdraftNotificationEnabled { get; set; } = true;
        public string? BankReferenceMetadataJson { get; set; }
    }

    public class TrustJurisdictionPolicyUpsertDto
    {
        public string PolicyKey { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = "DEFAULT";
        public string? Name { get; set; }
        public string AccountType { get; set; } = "all";
        public int VersionNumber { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public bool IsSystemBaseline { get; set; }
        public bool RequireMakerChecker { get; set; } = true;
        public bool RequireOverrideReason { get; set; } = true;
        public decimal DualApprovalThreshold { get; set; } = 10000m;
        public decimal ResponsibleLawyerApprovalThreshold { get; set; } = 25000m;
        public decimal SignatoryApprovalThreshold { get; set; } = 5000m;
        public int MonthlyCloseCadenceDays { get; set; } = 30;
        public int ExceptionAgingSlaHours { get; set; } = 48;
        public int RetentionPeriodMonths { get; set; } = 60;
        public bool RequireMonthlyThreeWayReconciliation { get; set; } = true;
        public bool RequireResponsibleLawyerAssignment { get; set; } = true;
        public List<string> DisbursementClassesRequiringSignatory { get; set; } = new();
        public List<string> OperationalApproverRoles { get; set; } = new();
        public List<string> OverrideApproverRoles { get; set; } = new();
        public string? MetadataJson { get; set; }
    }

    public class TrustApprovalRequirementDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrustTransactionId { get; set; } = string.Empty;
        public string RequirementType { get; set; } = string.Empty;
        public int RequiredCount { get; set; }
        public int SatisfiedCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Summary { get; set; }
    }

    public class TrustApprovalDecisionDto
    {
        public string Id { get; set; } = string.Empty;
        public string RequirementId { get; set; } = string.Empty;
        public string ActorUserId { get; set; } = string.Empty;
        public string? ActorRole { get; set; }
        public string DecisionType { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TrustTransactionApprovalStateDto
    {
        public string TrustTransactionId { get; set; } = string.Empty;
        public string TransactionStatus { get; set; } = string.Empty;
        public string ApprovalStatus { get; set; } = string.Empty;
        public bool IsReadyToPost { get; set; }
        public bool HasOverride { get; set; }
        public List<TrustApprovalRequirementDto> Requirements { get; set; } = new();
        public List<TrustApprovalDecisionDto> Decisions { get; set; } = new();
    }

    public class TrustApprovalQueueItemDto
    {
        public string TrustTransactionId { get; set; } = string.Empty;
        public string TrustAccountId { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string? DisbursementClass { get; set; }
        public decimal Amount { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? MatterId { get; set; }
        public string? PolicySummary { get; set; }
        public List<TrustApprovalRequirementDto> Requirements { get; set; } = new();
    }

    public class TrustMonthClosePrepareDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string? ReconciliationPacketId { get; set; }
        public bool AutoGeneratePacket { get; set; } = true;
        public decimal? StatementEndingBalance { get; set; }
        public string? Notes { get; set; }
    }

    public class TrustMonthCloseSignoffDto
    {
        public string Role { get; set; } = string.Empty; // reviewer | responsible_lawyer
        public string? Notes { get; set; }
        public List<TrustMonthCloseAttestationDto> Attestations { get; set; } = new();
    }

    public class TrustMonthCloseReopenDto
    {
        public string Reason { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? ReconciliationPacketId { get; set; }
    }

    public class TrustMonthCloseStepDto
    {
        public string StepKey { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? CompletedBy { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class TrustPacketTemplateAttestationDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Role { get; set; } = "responsible_lawyer";
        public string? HelpText { get; set; }
        public bool Required { get; set; } = true;
    }

    public class TrustJurisdictionPacketTemplateUpsertDto
    {
        public string PolicyKey { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = "DEFAULT";
        public string AccountType { get; set; } = "all";
        public string TemplateKey { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int VersionNumber { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public List<string> RequiredSections { get; set; } = new();
        public List<TrustPacketTemplateAttestationDto> RequiredAttestations { get; set; } = new();
        public List<string> DisclosureBlocks { get; set; } = new();
        public string? RenderingProfileJson { get; set; }
        public string? MetadataJson { get; set; }
    }

    public class TrustMonthCloseAttestationDto
    {
        public string Key { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Role { get; set; }
        public bool Accepted { get; set; }
        public string? Notes { get; set; }
        public string? SignedBy { get; set; }
        public DateTime? SignedAt { get; set; }
    }

    public class TrustMonthCloseDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrustAccountId { get; set; } = string.Empty;
        public string PolicyKey { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string? ReconciliationPacketId { get; set; }
        public int VersionNumber { get; set; }
        public bool IsCanonical { get; set; }
        public string Status { get; set; } = string.Empty;
        public int OpenExceptionCount { get; set; }
        public string? PreparedBy { get; set; }
        public DateTime PreparedAt { get; set; }
        public string? ReviewerSignedBy { get; set; }
        public DateTime? ReviewerSignedAt { get; set; }
        public string? ResponsibleLawyerSignedBy { get; set; }
        public DateTime? ResponsibleLawyerSignedAt { get; set; }
        public string? ReopenedFromMonthCloseId { get; set; }
        public string? SupersededByMonthCloseId { get; set; }
        public string? ReopenedBy { get; set; }
        public DateTime? ReopenedAt { get; set; }
        public string? ReopenReason { get; set; }
        public string? SupersededBy { get; set; }
        public DateTime? SupersededAt { get; set; }
        public string? SupersedeReason { get; set; }
        public string? PacketTemplateKey { get; set; }
        public string? PacketTemplateName { get; set; }
        public int? PacketTemplateVersionNumber { get; set; }
        public List<string> MissingRequiredSections { get; set; } = new();
        public List<string> DisclosureBlocks { get; set; } = new();
        public List<TrustPacketTemplateAttestationDto> RequiredAttestations { get; set; } = new();
        public List<TrustMonthCloseAttestationDto> CompletedAttestations { get; set; } = new();
        public List<TrustMonthCloseStepDto> Steps { get; set; } = new();
    }

    public class TrustComplianceExportRequest
    {
        public string ExportType { get; set; } = string.Empty;
        public string Format { get; set; } = "json";
        public string? TrustAccountId { get; set; }
        public string? ClientTrustLedgerId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public string? TrustReconciliationPacketId { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
    }

    public class TrustComplianceExportListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string ExportType { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? TrustAccountId { get; set; }
        public string? ClientTrustLedgerId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public string? TrustReconciliationPacketId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string? SummaryJson { get; set; }
        public string? GeneratedBy { get; set; }
        public string? ParentExportId { get; set; }
        public string IntegrityStatus { get; set; } = "unsigned";
        public string? RetentionPolicyTag { get; set; }
        public string? RedactionProfile { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class TrustComplianceExportDto : TrustComplianceExportListItemDto
    {
        public string? PayloadJson { get; set; }
        public string? ProvenanceJson { get; set; }
    }

    public class TrustOperationalAlertActionDto
    {
        public string? Notes { get; set; }
    }

    public class TrustOperationalAlertAssignDto
    {
        public string AssigneeUserId { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class TrustOpsInboxAssignDto
    {
        public string AssigneeUserId { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class TrustOpsInboxDeferDto
    {
        public DateTime DeferredUntilUtc { get; set; }
        public string? Notes { get; set; }
    }

    public class TrustOperationalAlertRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string AlertKey { get; set; } = string.Empty;
        public string AlertType { get; set; } = string.Empty;
        public string Severity { get; set; } = "warning";
        public string? TrustAccountId { get; set; }
        public string? TrustAccountName { get; set; }
        public string? RelatedEntityType { get; set; }
        public string? RelatedEntityId { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string SourceStatus { get; set; } = "open";
        public string WorkflowStatus { get; set; } = "open";
        public string? AssignedUserId { get; set; }
        public string? ActionHint { get; set; }
        public DateTime OpenedAt { get; set; }
        public int AgeDays { get; set; }
        public DateTime FirstDetectedAt { get; set; }
        public DateTime LastDetectedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? EscalatedBy { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public int NotificationCount { get; set; }
    }

    public class TrustOpsInboxItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? TrustOperationalAlertId { get; set; }
        public string? TrustCloseForecastSnapshotId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string BlockerGroup { get; set; } = string.Empty;
        public string Severity { get; set; } = "warning";
        public string? TrustAccountId { get; set; }
        public string? TrustAccountName { get; set; }
        public string? Jurisdiction { get; set; }
        public string? OfficeId { get; set; }
        public string? AssignedUserId { get; set; }
        public string WorkflowStatus { get; set; } = "open";
        public DateTime OpenedAt { get; set; }
        public DateTime LastDetectedAt { get; set; }
        public DateTime? DueAt { get; set; }
        public DateTime? DeferredUntil { get; set; }
        public bool IsSlaBreached { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string? ActionHint { get; set; }
        public string? SuggestedExportType { get; set; }
        public string? SuggestedRoute { get; set; }
        public int AgeDays { get; set; }
        public string? LinkedAlertWorkflowStatus { get; set; }
    }

    public class TrustOpsInboxEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string? ActorUserId { get; set; }
        public string? Notes { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TrustOpsInboxSummaryDto
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int TotalCount { get; set; }
        public int BreachedCount { get; set; }
        public int CloseBlockerCount { get; set; }
        public int StatementBlockerCount { get; set; }
        public int ExceptionBlockerCount { get; set; }
        public int ApprovalBlockerCount { get; set; }
        public List<TrustOpsInboxItemDto> Items { get; set; } = new();
    }

    public class TrustOperationalAlertEventDto
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string? ActorUserId { get; set; }
        public string? Notes { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TrustOperationalAlertSyncResultDto
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int ActiveAlertCount { get; set; }
        public int CreatedCount { get; set; }
        public int ReopenedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int AutoResolvedCount { get; set; }
        public int NotificationCount { get; set; }
    }

    public class TrustAsOfProjectionRecoveryRequest
    {
        public string? TrustAccountId { get; set; }
        public DateTime? AsOfUtc { get; set; }
        public bool CommitProjectionRepair { get; set; }
        public bool OnlyIfDrifted { get; set; } = true;
    }

    public class TrustAsOfProjectionRecoveryLedgerDto
    {
        public string LedgerId { get; set; } = string.Empty;
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public decimal PersistedRunningBalance { get; set; }
        public decimal PersistedClearedBalance { get; set; }
        public decimal PersistedUnclearedBalance { get; set; }
        public decimal PersistedAvailableToDisburse { get; set; }
        public decimal AsOfRunningBalance { get; set; }
        public decimal AsOfClearedBalance { get; set; }
        public decimal AsOfUnclearedBalance { get; set; }
        public decimal AsOfAvailableToDisburse { get; set; }
        public bool HasCurrentProjectionDrift { get; set; }
    }

    public class TrustAsOfProjectionRecoveryAccountDto
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public decimal PersistedCurrentBalance { get; set; }
        public decimal PersistedClearedBalance { get; set; }
        public decimal PersistedUnclearedBalance { get; set; }
        public decimal PersistedAvailableDisbursementCapacity { get; set; }
        public decimal AsOfCurrentBalance { get; set; }
        public decimal AsOfClearedBalance { get; set; }
        public decimal AsOfUnclearedBalance { get; set; }
        public decimal AsOfAvailableDisbursementCapacity { get; set; }
        public bool HasCurrentProjectionDrift { get; set; }
        public List<TrustAsOfProjectionRecoveryLedgerDto> Ledgers { get; set; } = new();
    }

    public class TrustAsOfProjectionRecoveryResult
    {
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime EffectiveAsOfUtc { get; set; }
        public bool CommitProjectionRepair { get; set; }
        public bool HistoricalPreviewOnly { get; set; }
        public int AccountCount { get; set; }
        public int LedgerCount { get; set; }
        public int DriftedAccountCount { get; set; }
        public int DriftedLedgerCount { get; set; }
        public List<string> RepairedTrustAccountIds { get; set; } = new();
        public List<TrustAsOfProjectionRecoveryAccountDto> Accounts { get; set; } = new();
    }

    public class TrustPacketRegenerationRequest
    {
        public string? TrustAccountId { get; set; }
        public string? TrustReconciliationPacketId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public string? StatementImportId { get; set; }
        public decimal? StatementEndingBalance { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool AutoPrepareMonthClose { get; set; } = true;
    }

    public class TrustPacketRegenerationResult
    {
        public string? SourcePacketId { get; set; }
        public string PacketId { get; set; } = string.Empty;
        public int PacketVersionNumber { get; set; }
        public string PacketStatus { get; set; } = string.Empty;
        public string? TrustAccountId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public string? TrustMonthCloseStatus { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
    }

    public class TrustComplianceBundleRequest
    {
        public string? TrustAccountId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public string? TrustReconciliationPacketId { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public bool IncludeJsonPacket { get; set; } = true;
        public bool IncludeAccountJournalCsv { get; set; } = true;
        public bool IncludeApprovalRegisterCsv { get; set; } = true;
        public bool IncludeClientLedgerCards { get; set; } = true;
        public string? Notes { get; set; }
    }

    public class TrustComplianceBundleResult
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string ManifestExportId { get; set; } = string.Empty;
        public string ManifestFileName { get; set; } = string.Empty;
        public string? TrustAccountId { get; set; }
        public string? TrustMonthCloseId { get; set; }
        public string? TrustReconciliationPacketId { get; set; }
        public int ExportCount { get; set; }
        public List<TrustComplianceExportListItemDto> Exports { get; set; } = new();
        public TrustBundleIntegrityDto? Integrity { get; set; }
    }

    public class TrustCloseForecastSnapshotDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public string? Jurisdiction { get; set; }
        public string? OfficeId { get; set; }
        public string StatementCadence { get; set; } = "monthly";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime CloseDueAt { get; set; }
        public string ReadinessStatus { get; set; } = "blocked";
        public string Severity { get; set; } = "warning";
        public bool MissingStatementImport { get; set; }
        public string? LatestStatementImportId { get; set; }
        public DateTime? StatementImportedAt { get; set; }
        public bool HasCanonicalPacket { get; set; }
        public string? CanonicalPacketId { get; set; }
        public string? PacketStatus { get; set; }
        public bool HasCanonicalMonthClose { get; set; }
        public string? CanonicalMonthCloseId { get; set; }
        public string? MonthCloseStatus { get; set; }
        public int OpenExceptionCount { get; set; }
        public int OutstandingItemCount { get; set; }
        public int MissingRequiredSectionCount { get; set; }
        public int MissingAttestationCount { get; set; }
        public decimal UnclearedBalance { get; set; }
        public int UnclearedEntryCount { get; set; }
        public int? OldestOutstandingAgeDays { get; set; }
        public int? OldestUnclearedAgeDays { get; set; }
        public bool DraftBundleEligible { get; set; }
        public string? DraftBundleManifestExportId { get; set; }
        public DateTime? DraftBundleGeneratedAt { get; set; }
        public string? RecommendedAction { get; set; }
        public int ReminderCount { get; set; }
        public DateTime? LastReminderAt { get; set; }
        public DateTime? NextReminderAt { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public DateTime? LastAutomationRunAt { get; set; }
        public int DaysUntilDue { get; set; }
        public bool IsOverdue { get; set; }
    }

    public class TrustCloseForecastSummaryDto
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int TotalCount { get; set; }
        public int ReadyCount { get; set; }
        public int AtRiskCount { get; set; }
        public int BlockedCount { get; set; }
        public int OverdueCount { get; set; }
        public int DraftBundleEligibleCount { get; set; }
        public int ReminderDueCount { get; set; }
        public List<TrustCloseForecastSnapshotDto> Snapshots { get; set; } = new();
    }

    public class TrustCloseForecastSyncResultDto
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int SnapshotCount { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int ReminderCount { get; set; }
        public int EscalatedCount { get; set; }
        public int DraftBundleCount { get; set; }
        public int ResolvedInboxCount { get; set; }
    }

    public class TrustBundleSignRequest
    {
        public string? RetentionPolicyTag { get; set; }
        public string? RedactionProfile { get; set; }
        public string? Notes { get; set; }
    }

    public class TrustBundleIntegrityDto
    {
        public string ManifestExportId { get; set; } = string.Empty;
        public string ManifestFileName { get; set; } = string.Empty;
        public string IntegrityStatus { get; set; } = "unsigned";
        public string SignatureAlgorithm { get; set; } = "hmac-sha256";
        public string? SignatureDigest { get; set; }
        public string? SignedBy { get; set; }
        public DateTime? SignedAt { get; set; }
        public string VerificationStatus { get; set; } = "unsigned";
        public DateTime? VerifiedAt { get; set; }
        public string? RetentionPolicyTag { get; set; }
        public string? RedactionProfile { get; set; }
        public string? ParentManifestExportId { get; set; }
        public int EvidenceReferenceCount { get; set; }
        public int ExportCount { get; set; }
        public string? ProvenanceJson { get; set; }
    }
}
