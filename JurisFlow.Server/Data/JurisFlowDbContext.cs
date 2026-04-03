using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Data
{
    public class JurisFlowDbContext : DbContext
    {
        private readonly DbEncryptionService? _dbEncryptionService;
        private readonly TenantContext? _tenantContext;

        public JurisFlowDbContext(
            DbContextOptions<JurisFlowDbContext> options,
            DbEncryptionService dbEncryptionService,
            TenantContext tenantContext)
            : base(options)
        {
            _dbEncryptionService = dbEncryptionService;
            _tenantContext = tenantContext;
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Matter> Matters { get; set; }
        public DbSet<MatterClientLink> MatterClientLinks { get; set; }
        public DbSet<MatterNote> MatterNotes { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<JurisFlow.Server.Models.Task> Tasks { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<CalendarEvent> CalendarEvents { get; set; }
        public DbSet<TrustTransaction> TrustTransactions { get; set; }
        public DbSet<TrustBankAccount> TrustBankAccounts { get; set; }
        public DbSet<ClientTrustLedger> ClientTrustLedgers { get; set; }
        public DbSet<ReconciliationRecord> ReconciliationRecords { get; set; }
        public DbSet<Lead> Leads { get; set; }
        public DbSet<TimeEntry> TimeEntries { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<OpposingParty> OpposingParties { get; set; }
        public DbSet<ConflictCheck> ConflictChecks { get; set; }
        public DbSet<ConflictResult> ConflictResults { get; set; }
        public DbSet<SignatureRequest> SignatureRequests { get; set; }
        public DbSet<SignatureAuditEntry> SignatureAuditEntries { get; set; }
        public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
        public DbSet<PaymentPlan> PaymentPlans { get; set; }
        public DbSet<CourtRule> CourtRules { get; set; }
        public DbSet<JurisdictionDefinition> JurisdictionDefinitions { get; set; }
        public DbSet<JurisdictionRulePack> JurisdictionRulePacks { get; set; }
        public DbSet<JurisdictionCoverageMatrixEntry> JurisdictionCoverageMatrixEntries { get; set; }
        public DbSet<JurisdictionRuleChangeRecord> JurisdictionRuleChangeRecords { get; set; }
        public DbSet<JurisdictionValidationTestCase> JurisdictionValidationTestCases { get; set; }
        public DbSet<JurisdictionValidationTestRun> JurisdictionValidationTestRuns { get; set; }
        public DbSet<Deadline> Deadlines { get; set; }
        public DbSet<EmailMessage> EmailMessages { get; set; }
        public DbSet<EmailAccount> EmailAccounts { get; set; }
        public DbSet<OutboundEmail> OutboundEmails { get; set; }
        public DbSet<SmsMessage> SmsMessages { get; set; }
        public DbSet<SmsTemplate> SmsTemplates { get; set; }
        public DbSet<SmsReminder> SmsReminders { get; set; }
        public DbSet<IntakeForm> IntakeForms { get; set; }
        public DbSet<IntakeSubmission> IntakeSubmissions { get; set; }
        public DbSet<ResearchSession> ResearchSessions { get; set; }
        public DbSet<ContractAnalysis> ContractAnalyses { get; set; }
        public DbSet<CasePrediction> CasePredictions { get; set; }
        public DbSet<StaffMessage> StaffMessages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<ClientMessage> ClientMessages { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<AuthSession> AuthSessions { get; set; }
        public DbSet<MfaChallenge> MfaChallenges { get; set; }
        public DbSet<RetentionPolicy> RetentionPolicies { get; set; }
        public DbSet<BillingLock> BillingLocks { get; set; }
        public DbSet<MatterBillingPolicy> MatterBillingPolicies { get; set; }
        public DbSet<BillingRateCard> BillingRateCards { get; set; }
        public DbSet<BillingRateCardEntry> BillingRateCardEntries { get; set; }
        public DbSet<BillingPrebillBatch> BillingPrebillBatches { get; set; }
        public DbSet<BillingPrebillLine> BillingPrebillLines { get; set; }
        public DbSet<BillingLedgerEntry> BillingLedgerEntries { get; set; }
        public DbSet<BillingPaymentAllocation> BillingPaymentAllocations { get; set; }
        public DbSet<InvoicePayorAllocation> InvoicePayorAllocations { get; set; }
        public DbSet<InvoiceLinePayorAllocation> InvoiceLinePayorAllocations { get; set; }
        public DbSet<BillingEbillingTransmission> BillingEbillingTransmissions { get; set; }
        public DbSet<BillingEbillingResultEvent> BillingEbillingResultEvents { get; set; }
        public DbSet<TrustReconciliationSnapshot> TrustReconciliationSnapshots { get; set; }
        public DbSet<TrustRiskPolicy> TrustRiskPolicies { get; set; }
        public DbSet<TrustRiskEvent> TrustRiskEvents { get; set; }
        public DbSet<TrustRiskAction> TrustRiskActions { get; set; }
        public DbSet<TrustRiskHold> TrustRiskHolds { get; set; }
        public DbSet<TrustRiskReviewLink> TrustRiskReviewLinks { get; set; }
        public DbSet<AiDraftSession> AiDraftSessions { get; set; }
        public DbSet<AiDraftOutput> AiDraftOutputs { get; set; }
        public DbSet<AiDraftClaim> AiDraftClaims { get; set; }
        public DbSet<AiDraftEvidenceLink> AiDraftEvidenceLinks { get; set; }
        public DbSet<AiDraftRuleCitation> AiDraftRuleCitations { get; set; }
        public DbSet<AiDraftVerificationRun> AiDraftVerificationRuns { get; set; }
        public DbSet<OutcomeFeePlan> OutcomeFeePlans { get; set; }
        public DbSet<OutcomeFeePlanVersion> OutcomeFeePlanVersions { get; set; }
        public DbSet<OutcomeFeeScenario> OutcomeFeeScenarios { get; set; }
        public DbSet<OutcomeFeePhaseForecast> OutcomeFeePhaseForecasts { get; set; }
        public DbSet<OutcomeFeeStaffingLine> OutcomeFeeStaffingLines { get; set; }
        public DbSet<OutcomeFeeAssumption> OutcomeFeeAssumptions { get; set; }
        public DbSet<OutcomeFeeCollectionsForecast> OutcomeFeeCollectionsForecasts { get; set; }
        public DbSet<OutcomeFeeUpdateEvent> OutcomeFeeUpdateEvents { get; set; }
        public DbSet<OutcomeFeeCalibrationSnapshot> OutcomeFeeCalibrationSnapshots { get; set; }
        public DbSet<ClientTransparencyProfile> ClientTransparencyProfiles { get; set; }
        public DbSet<ClientTransparencySnapshot> ClientTransparencySnapshots { get; set; }
        public DbSet<ClientTransparencyTimelineItem> ClientTransparencyTimelineItems { get; set; }
        public DbSet<ClientTransparencyDelayReason> ClientTransparencyDelayReasons { get; set; }
        public DbSet<ClientTransparencyNextStep> ClientTransparencyNextSteps { get; set; }
        public DbSet<ClientTransparencyCostImpact> ClientTransparencyCostImpacts { get; set; }
        public DbSet<ClientTransparencyUpdateEvent> ClientTransparencyUpdateEvents { get; set; }
        public DbSet<ClientTransparencyReviewAction> ClientTransparencyReviewActions { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceLineItem> InvoiceLineItems { get; set; }
        public DbSet<Holiday> Holidays { get; set; }
        public DbSet<DocumentVersion> DocumentVersions { get; set; }
        public DbSet<AppointmentRequest> AppointmentRequests { get; set; }
        public DbSet<BillingSettings> BillingSettings { get; set; }
        public DbSet<FirmSettings> FirmSettings { get; set; }
        public DbSet<IntegrationConnection> IntegrationConnections { get; set; }
        public DbSet<IntegrationSecret> IntegrationSecrets { get; set; }
        public DbSet<IntegrationRun> IntegrationRuns { get; set; }
        public DbSet<IntegrationEntityLink> IntegrationEntityLinks { get; set; }
        public DbSet<IntegrationMappingProfile> IntegrationMappingProfiles { get; set; }
        public DbSet<IntegrationConflictQueueItem> IntegrationConflictQueueItems { get; set; }
        public DbSet<IntegrationReviewQueueItem> IntegrationReviewQueueItems { get; set; }
        public DbSet<IntegrationInboxEvent> IntegrationInboxEvents { get; set; }
        public DbSet<IntegrationOutboxEvent> IntegrationOutboxEvents { get; set; }
        public DbSet<CourtDocketEntry> CourtDocketEntries { get; set; }
        public DbSet<EfilingSubmission> EfilingSubmissions { get; set; }
        public DbSet<ClientStatusHistory> ClientStatusHistories { get; set; }
        public DbSet<FirmEntity> FirmEntities { get; set; }
        public DbSet<Office> Offices { get; set; }
        public DbSet<DocumentContentIndex> DocumentContentIndexes { get; set; }
        public DbSet<DocumentContentToken> DocumentContentTokens { get; set; }
        public DbSet<DocumentShare> DocumentShares { get; set; }
        public DbSet<DocumentComment> DocumentComments { get; set; }
        public DbSet<AppDirectoryListing> AppDirectoryListings { get; set; }
        public DbSet<AppDirectorySubmission> AppDirectorySubmissions { get; set; }
        public DbSet<StripeWebhookEvent> StripeWebhookEvents { get; set; }

        public string? TenantId => _tenantContext?.TenantId;
        public bool RequireTenant => _tenantContext?.RequireTenant ?? false;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ApplyEncryption(modelBuilder);
            ApplyTenantFilters(modelBuilder);
            modelBuilder.Entity<Matter>().HasQueryFilter(m =>
                (!RequireTenant || EF.Property<string>(m, "TenantId") == TenantId) &&
                m.Status != "Deleted");
            var defaultFlagIndexFilter = Database.IsNpgsql() ? "\"IsDefault\" = TRUE" : "\"IsDefault\" = 1";

            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.Slug)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex("TenantId", nameof(User.NormalizedEmail))
                .IsUnique();

            modelBuilder.Entity<Client>()
                .HasIndex("TenantId", nameof(Client.NormalizedEmail))
                .IsUnique();
            modelBuilder.Entity<Client>()
                .HasIndex("TenantId", nameof(Client.CreatedAt));
            modelBuilder.Entity<MatterClientLink>()
                .HasIndex("TenantId", nameof(MatterClientLink.MatterId), nameof(MatterClientLink.ClientId))
                .IsUnique();
            modelBuilder.Entity<MatterClientLink>()
                .HasIndex("TenantId", nameof(MatterClientLink.ClientId), nameof(MatterClientLink.CreatedAt));
            modelBuilder.Entity<MatterNote>()
                .HasIndex("TenantId", nameof(MatterNote.MatterId), nameof(MatterNote.CreatedAt));
            modelBuilder.Entity<MatterNote>()
                .HasIndex("TenantId", nameof(MatterNote.MatterId), nameof(MatterNote.UpdatedAt));

            modelBuilder.Entity<Employee>()
                .HasIndex("TenantId", nameof(Employee.Email))
                .IsUnique();

            modelBuilder.Entity<JurisFlow.Server.Models.Task>()
                .HasIndex("TenantId", nameof(JurisFlow.Server.Models.Task.CreatedAt));
            modelBuilder.Entity<CalendarEvent>()
                .HasIndex("TenantId", nameof(CalendarEvent.Date));
            modelBuilder.Entity<Lead>()
                .HasIndex("TenantId", nameof(Lead.CreatedAt));
            modelBuilder.Entity<Document>()
                .HasIndex("TenantId", nameof(Document.CreatedAt));
            modelBuilder.Entity<Document>()
                .HasIndex("TenantId", nameof(Document.MatterId), nameof(Document.CreatedAt));

            modelBuilder.Entity<StaffMessage>()
                .HasIndex(m => new { m.SenderId, m.RecipientId, m.CreatedAt });

            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.ClientId, n.CreatedAt });

            modelBuilder.Entity<ClientMessage>()
                .HasIndex(m => new { m.ClientId, m.CreatedAt });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.CreatedAt, a.Action });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => a.Sequence);

            modelBuilder.Entity<AuditLog>()
                .HasIndex("TenantId", nameof(AuditLog.CreatedAt));

            modelBuilder.Entity<AuditLog>()
                .HasIndex("TenantId", nameof(AuditLog.Action), nameof(AuditLog.CreatedAt));

            modelBuilder.Entity<AuditLog>()
                .HasIndex("TenantId", nameof(AuditLog.Entity), nameof(AuditLog.CreatedAt));

            modelBuilder.Entity<AuditLog>()
                .HasIndex("TenantId", nameof(AuditLog.UserId), nameof(AuditLog.CreatedAt));

            modelBuilder.Entity<AuditLog>()
                .HasIndex("TenantId", nameof(AuditLog.ClientId), nameof(AuditLog.CreatedAt));

            modelBuilder.Entity<AuthSession>()
                .HasIndex(s => new { s.UserId, s.ClientId, s.ExpiresAt });

            modelBuilder.Entity<MfaChallenge>()
                .HasIndex(c => new { c.UserId, c.ExpiresAt });

            modelBuilder.Entity<RetentionPolicy>()
                .HasIndex("TenantId", nameof(RetentionPolicy.EntityName))
                .IsUnique();

            modelBuilder.Entity<BillingLock>()
                .HasIndex(b => new { b.PeriodStart, b.PeriodEnd });

            modelBuilder.Entity<MatterBillingPolicy>()
                .HasIndex("TenantId", nameof(MatterBillingPolicy.MatterId), nameof(MatterBillingPolicy.Status))
                .IsUnique();
            modelBuilder.Entity<MatterBillingPolicy>()
                .HasIndex("TenantId", nameof(MatterBillingPolicy.ClientId), nameof(MatterBillingPolicy.ArrangementType), nameof(MatterBillingPolicy.Status));

            modelBuilder.Entity<BillingRateCard>()
                .HasIndex("TenantId", nameof(BillingRateCard.Name), nameof(BillingRateCard.EffectiveFrom));
            modelBuilder.Entity<BillingRateCard>()
                .HasIndex("TenantId", nameof(BillingRateCard.Scope), nameof(BillingRateCard.ClientId), nameof(BillingRateCard.MatterId), nameof(BillingRateCard.Status));

            modelBuilder.Entity<BillingRateCardEntry>()
                .HasIndex("TenantId", nameof(BillingRateCardEntry.RateCardId), nameof(BillingRateCardEntry.EntryType), nameof(BillingRateCardEntry.Priority));
            modelBuilder.Entity<BillingRateCardEntry>()
                .HasIndex("TenantId", nameof(BillingRateCardEntry.Status), nameof(BillingRateCardEntry.TaskCode), nameof(BillingRateCardEntry.ActivityCode), nameof(BillingRateCardEntry.ExpenseCode));

            modelBuilder.Entity<BillingPrebillBatch>()
                .HasIndex("TenantId", nameof(BillingPrebillBatch.MatterId), nameof(BillingPrebillBatch.Status), nameof(BillingPrebillBatch.GeneratedAt));
            modelBuilder.Entity<BillingPrebillBatch>()
                .HasIndex("TenantId", nameof(BillingPrebillBatch.InvoiceId));

            modelBuilder.Entity<BillingPrebillLine>()
                .HasIndex("TenantId", nameof(BillingPrebillLine.PrebillBatchId), nameof(BillingPrebillLine.Status));
            modelBuilder.Entity<BillingPrebillLine>()
                .HasIndex("TenantId", nameof(BillingPrebillLine.SourceType), nameof(BillingPrebillLine.SourceId));
            modelBuilder.Entity<BillingPrebillLine>()
                .HasIndex("TenantId", nameof(BillingPrebillLine.MatterId), nameof(BillingPrebillLine.ClientId), nameof(BillingPrebillLine.ServiceDate));

            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.CorrelationKey))
                .IsUnique(false);
            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.LedgerDomain), nameof(BillingLedgerEntry.LedgerBucket), nameof(BillingLedgerEntry.PostedAt));
            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.InvoiceId), nameof(BillingLedgerEntry.PaymentTransactionId), nameof(BillingLedgerEntry.PostedAt));
            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.ReversalOfLedgerEntryId));
            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.InvoiceId), nameof(BillingLedgerEntry.PayorClientId), nameof(BillingLedgerEntry.PostedAt));
            modelBuilder.Entity<BillingLedgerEntry>()
                .HasIndex("TenantId", nameof(BillingLedgerEntry.InvoicePayorAllocationId), nameof(BillingLedgerEntry.PostedAt));

            modelBuilder.Entity<BillingPaymentAllocation>()
                .HasIndex("TenantId", nameof(BillingPaymentAllocation.PaymentTransactionId), nameof(BillingPaymentAllocation.Status), nameof(BillingPaymentAllocation.AppliedAt));
            modelBuilder.Entity<BillingPaymentAllocation>()
                .HasIndex("TenantId", nameof(BillingPaymentAllocation.InvoiceId), nameof(BillingPaymentAllocation.InvoiceLineItemId), nameof(BillingPaymentAllocation.Status));
            modelBuilder.Entity<BillingPaymentAllocation>()
                .HasIndex("TenantId", nameof(BillingPaymentAllocation.PayorClientId), nameof(BillingPaymentAllocation.Status), nameof(BillingPaymentAllocation.AppliedAt));
            modelBuilder.Entity<BillingPaymentAllocation>()
                .HasIndex("TenantId", nameof(BillingPaymentAllocation.InvoicePayorAllocationId), nameof(BillingPaymentAllocation.Status));

            modelBuilder.Entity<InvoicePayorAllocation>()
                .HasIndex("TenantId", nameof(InvoicePayorAllocation.InvoiceId), nameof(InvoicePayorAllocation.Status), nameof(InvoicePayorAllocation.Priority));
            modelBuilder.Entity<InvoicePayorAllocation>()
                .HasIndex("TenantId", nameof(InvoicePayorAllocation.InvoiceId), nameof(InvoicePayorAllocation.PayorClientId), nameof(InvoicePayorAllocation.IsPrimary));

            modelBuilder.Entity<InvoiceLinePayorAllocation>()
                .HasIndex("TenantId", nameof(InvoiceLinePayorAllocation.InvoiceId), nameof(InvoiceLinePayorAllocation.InvoiceLineItemId), nameof(InvoiceLinePayorAllocation.Status));
            modelBuilder.Entity<InvoiceLinePayorAllocation>()
                .HasIndex("TenantId", nameof(InvoiceLinePayorAllocation.InvoicePayorAllocationId));
            modelBuilder.Entity<InvoiceLinePayorAllocation>()
                .HasIndex("TenantId", nameof(InvoiceLinePayorAllocation.PayorClientId), nameof(InvoiceLinePayorAllocation.Status));

            modelBuilder.Entity<BillingEbillingTransmission>()
                .HasIndex("TenantId", nameof(BillingEbillingTransmission.ProviderKey), nameof(BillingEbillingTransmission.Status), nameof(BillingEbillingTransmission.SubmittedAt));
            modelBuilder.Entity<BillingEbillingTransmission>()
                .HasIndex("TenantId", nameof(BillingEbillingTransmission.InvoiceId), nameof(BillingEbillingTransmission.CreatedAt));
            modelBuilder.Entity<BillingEbillingTransmission>()
                .HasIndex("TenantId", nameof(BillingEbillingTransmission.ProviderKey), nameof(BillingEbillingTransmission.ExternalTransmissionId));

            modelBuilder.Entity<BillingEbillingResultEvent>()
                .HasIndex("TenantId", nameof(BillingEbillingResultEvent.ProviderKey), nameof(BillingEbillingResultEvent.Status), nameof(BillingEbillingResultEvent.OccurredAt));
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .HasIndex("TenantId", nameof(BillingEbillingResultEvent.TransmissionId), nameof(BillingEbillingResultEvent.OccurredAt));
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .HasIndex("TenantId", nameof(BillingEbillingResultEvent.InvoiceId), nameof(BillingEbillingResultEvent.OccurredAt));
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .HasIndex("TenantId", nameof(BillingEbillingResultEvent.ProviderKey), nameof(BillingEbillingResultEvent.ExternalEventId));

            modelBuilder.Entity<TrustReconciliationSnapshot>()
                .HasIndex("TenantId", nameof(TrustReconciliationSnapshot.AsOfUtc));
            modelBuilder.Entity<TrustReconciliationSnapshot>()
                .HasIndex("TenantId", nameof(TrustReconciliationSnapshot.TrustAccountId), nameof(TrustReconciliationSnapshot.AsOfUtc));

            modelBuilder.Entity<TrustRiskPolicy>()
                .HasIndex("TenantId", nameof(TrustRiskPolicy.PolicyKey), nameof(TrustRiskPolicy.VersionNumber))
                .IsUnique();
            modelBuilder.Entity<TrustRiskPolicy>()
                .HasIndex("TenantId", nameof(TrustRiskPolicy.IsActive), nameof(TrustRiskPolicy.Status), nameof(TrustRiskPolicy.UpdatedAt));

            modelBuilder.Entity<TrustRiskEvent>()
                .HasIndex("TenantId", nameof(TrustRiskEvent.Status), nameof(TrustRiskEvent.Severity), nameof(TrustRiskEvent.CreatedAt));
            modelBuilder.Entity<TrustRiskEvent>()
                .HasIndex("TenantId", nameof(TrustRiskEvent.SourceType), nameof(TrustRiskEvent.SourceId), nameof(TrustRiskEvent.CreatedAt));
            modelBuilder.Entity<TrustRiskEvent>()
                .HasIndex("TenantId", nameof(TrustRiskEvent.InvoiceId), nameof(TrustRiskEvent.MatterId), nameof(TrustRiskEvent.CreatedAt));
            modelBuilder.Entity<TrustRiskEvent>()
                .HasIndex("TenantId", nameof(TrustRiskEvent.TrustTransactionId), nameof(TrustRiskEvent.BillingLedgerEntryId), nameof(TrustRiskEvent.BillingPaymentAllocationId));
            modelBuilder.Entity<TrustRiskEvent>()
                .HasIndex("TenantId", nameof(TrustRiskEvent.CorrelationId));

            modelBuilder.Entity<TrustRiskAction>()
                .HasIndex("TenantId", nameof(TrustRiskAction.TrustRiskEventId), nameof(TrustRiskAction.CreatedAt));
            modelBuilder.Entity<TrustRiskAction>()
                .HasIndex("TenantId", nameof(TrustRiskAction.ActionType), nameof(TrustRiskAction.Status), nameof(TrustRiskAction.CreatedAt));

            modelBuilder.Entity<TrustRiskHold>()
                .HasIndex("TenantId", nameof(TrustRiskHold.Status), nameof(TrustRiskHold.HoldType), nameof(TrustRiskHold.PlacedAt));
            modelBuilder.Entity<TrustRiskHold>()
                .HasIndex("TenantId", nameof(TrustRiskHold.TargetType), nameof(TrustRiskHold.TargetId));
            modelBuilder.Entity<TrustRiskHold>()
                .HasIndex("TenantId", nameof(TrustRiskHold.TrustRiskEventId), nameof(TrustRiskHold.Status));

            modelBuilder.Entity<TrustRiskReviewLink>()
                .HasIndex("TenantId", nameof(TrustRiskReviewLink.TrustRiskEventId), nameof(TrustRiskReviewLink.Status));
            modelBuilder.Entity<TrustRiskReviewLink>()
                .HasIndex("TenantId", nameof(TrustRiskReviewLink.ReviewQueueItemId), nameof(TrustRiskReviewLink.ReviewQueueType));

            modelBuilder.Entity<AiDraftSession>()
                .HasIndex("TenantId", nameof(AiDraftSession.MatterId), nameof(AiDraftSession.Status), nameof(AiDraftSession.CreatedAt));
            modelBuilder.Entity<AiDraftSession>()
                .HasIndex("TenantId", nameof(AiDraftSession.UserId), nameof(AiDraftSession.CreatedAt));
            modelBuilder.Entity<AiDraftOutput>()
                .HasIndex("TenantId", nameof(AiDraftOutput.SessionId), nameof(AiDraftOutput.GeneratedAt));
            modelBuilder.Entity<AiDraftOutput>()
                .HasIndex("TenantId", nameof(AiDraftOutput.CorrelationId));
            modelBuilder.Entity<AiDraftClaim>()
                .HasIndex("TenantId", nameof(AiDraftClaim.DraftOutputId), nameof(AiDraftClaim.OrderIndex));
            modelBuilder.Entity<AiDraftClaim>()
                .HasIndex("TenantId", nameof(AiDraftClaim.IsCritical), nameof(AiDraftClaim.Status));
            modelBuilder.Entity<AiDraftEvidenceLink>()
                .HasIndex("TenantId", nameof(AiDraftEvidenceLink.ClaimId), nameof(AiDraftEvidenceLink.DocumentVersionId));
            modelBuilder.Entity<AiDraftEvidenceLink>()
                .HasIndex("TenantId", nameof(AiDraftEvidenceLink.DocumentId), nameof(AiDraftEvidenceLink.Sha256));
            modelBuilder.Entity<AiDraftRuleCitation>()
                .HasIndex("TenantId", nameof(AiDraftRuleCitation.ClaimId), nameof(AiDraftRuleCitation.RuleCode));
            modelBuilder.Entity<AiDraftRuleCitation>()
                .HasIndex("TenantId", nameof(AiDraftRuleCitation.JurisdictionRulePackId), nameof(AiDraftRuleCitation.RulePackVersion));
            modelBuilder.Entity<AiDraftVerificationRun>()
                .HasIndex("TenantId", nameof(AiDraftVerificationRun.DraftOutputId), nameof(AiDraftVerificationRun.CreatedAt));
            modelBuilder.Entity<AiDraftVerificationRun>()
                .HasIndex("TenantId", nameof(AiDraftVerificationRun.Status), nameof(AiDraftVerificationRun.CreatedAt));

            modelBuilder.Entity<OutcomeFeePlan>()
                .HasIndex("TenantId", nameof(OutcomeFeePlan.MatterId), nameof(OutcomeFeePlan.Status), nameof(OutcomeFeePlan.UpdatedAt));
            modelBuilder.Entity<OutcomeFeePlan>()
                .HasIndex("TenantId", nameof(OutcomeFeePlan.CurrentVersionId));
            modelBuilder.Entity<OutcomeFeePlan>()
                .HasIndex("TenantId", nameof(OutcomeFeePlan.CorrelationId));

            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .HasIndex("TenantId", nameof(OutcomeFeePlanVersion.PlanId), nameof(OutcomeFeePlanVersion.VersionNumber))
                .IsUnique();
            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .HasIndex("TenantId", nameof(OutcomeFeePlanVersion.GeneratedAt));
            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .HasIndex("TenantId", nameof(OutcomeFeePlanVersion.CorrelationId));

            modelBuilder.Entity<OutcomeFeeScenario>()
                .HasIndex("TenantId", nameof(OutcomeFeeScenario.PlanVersionId), nameof(OutcomeFeeScenario.ScenarioKey));
            modelBuilder.Entity<OutcomeFeeScenario>()
                .HasIndex("TenantId", nameof(OutcomeFeeScenario.Status), nameof(OutcomeFeeScenario.CreatedAt));

            modelBuilder.Entity<OutcomeFeePhaseForecast>()
                .HasIndex("TenantId", nameof(OutcomeFeePhaseForecast.ScenarioId), nameof(OutcomeFeePhaseForecast.PhaseOrder));

            modelBuilder.Entity<OutcomeFeeStaffingLine>()
                .HasIndex("TenantId", nameof(OutcomeFeeStaffingLine.ScenarioId), nameof(OutcomeFeeStaffingLine.PhaseForecastId), nameof(OutcomeFeeStaffingLine.Role));
            modelBuilder.Entity<OutcomeFeeStaffingLine>()
                .HasIndex("TenantId", nameof(OutcomeFeeStaffingLine.Role), nameof(OutcomeFeeStaffingLine.CreatedAt));

            modelBuilder.Entity<OutcomeFeeAssumption>()
                .HasIndex("TenantId", nameof(OutcomeFeeAssumption.PlanVersionId), nameof(OutcomeFeeAssumption.Category), nameof(OutcomeFeeAssumption.Key));

            modelBuilder.Entity<OutcomeFeeCollectionsForecast>()
                .HasIndex("TenantId", nameof(OutcomeFeeCollectionsForecast.ScenarioId), nameof(OutcomeFeeCollectionsForecast.PayorSegment), nameof(OutcomeFeeCollectionsForecast.BucketDays));

            modelBuilder.Entity<OutcomeFeeUpdateEvent>()
                .HasIndex("TenantId", nameof(OutcomeFeeUpdateEvent.PlanId), nameof(OutcomeFeeUpdateEvent.TriggerType), nameof(OutcomeFeeUpdateEvent.CreatedAt));
            modelBuilder.Entity<OutcomeFeeUpdateEvent>()
                .HasIndex("TenantId", nameof(OutcomeFeeUpdateEvent.AppliedVersionId), nameof(OutcomeFeeUpdateEvent.CreatedAt));

            modelBuilder.Entity<OutcomeFeeCalibrationSnapshot>()
                .HasIndex("TenantId", nameof(OutcomeFeeCalibrationSnapshot.CohortKey), nameof(OutcomeFeeCalibrationSnapshot.AsOfDate));
            modelBuilder.Entity<OutcomeFeeCalibrationSnapshot>()
                .HasIndex("TenantId", nameof(OutcomeFeeCalibrationSnapshot.Status), nameof(OutcomeFeeCalibrationSnapshot.CreatedAt));

            modelBuilder.Entity<ClientTransparencyProfile>()
                .HasIndex("TenantId", nameof(ClientTransparencyProfile.Scope), nameof(ClientTransparencyProfile.MatterId), nameof(ClientTransparencyProfile.Status));
            modelBuilder.Entity<ClientTransparencyProfile>()
                .HasIndex("TenantId", nameof(ClientTransparencyProfile.ProfileKey), nameof(ClientTransparencyProfile.Status));

            modelBuilder.Entity<ClientTransparencySnapshot>()
                .HasIndex("TenantId", nameof(ClientTransparencySnapshot.MatterId), nameof(ClientTransparencySnapshot.IsCurrent), nameof(ClientTransparencySnapshot.GeneratedAt));
            modelBuilder.Entity<ClientTransparencySnapshot>()
                .HasIndex("TenantId", nameof(ClientTransparencySnapshot.MatterId), nameof(ClientTransparencySnapshot.VersionNumber))
                .IsUnique();
            modelBuilder.Entity<ClientTransparencySnapshot>()
                .HasIndex("TenantId", nameof(ClientTransparencySnapshot.IsPublished), nameof(ClientTransparencySnapshot.PublishedAt));

            modelBuilder.Entity<ClientTransparencyTimelineItem>()
                .HasIndex("TenantId", nameof(ClientTransparencyTimelineItem.SnapshotId), nameof(ClientTransparencyTimelineItem.OrderIndex));
            modelBuilder.Entity<ClientTransparencyDelayReason>()
                .HasIndex("TenantId", nameof(ClientTransparencyDelayReason.SnapshotId), nameof(ClientTransparencyDelayReason.IsActive), nameof(ClientTransparencyDelayReason.Severity));
            modelBuilder.Entity<ClientTransparencyNextStep>()
                .HasIndex("TenantId", nameof(ClientTransparencyNextStep.SnapshotId), nameof(ClientTransparencyNextStep.Status), nameof(ClientTransparencyNextStep.EtaAtUtc));
            modelBuilder.Entity<ClientTransparencyCostImpact>()
                .HasIndex("TenantId", nameof(ClientTransparencyCostImpact.SnapshotId));
            modelBuilder.Entity<ClientTransparencyUpdateEvent>()
                .HasIndex("TenantId", nameof(ClientTransparencyUpdateEvent.MatterId), nameof(ClientTransparencyUpdateEvent.CreatedAt));
            modelBuilder.Entity<ClientTransparencyUpdateEvent>()
                .HasIndex("TenantId", nameof(ClientTransparencyUpdateEvent.SnapshotId), nameof(ClientTransparencyUpdateEvent.CreatedAt));
            modelBuilder.Entity<ClientTransparencyReviewAction>()
                .HasIndex("TenantId", nameof(ClientTransparencyReviewAction.SnapshotId), nameof(ClientTransparencyReviewAction.CreatedAt));

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => i.Number);
            modelBuilder.Entity<Invoice>()
                .HasIndex("TenantId", nameof(Invoice.CreatedAt));

            modelBuilder.Entity<InvoiceLineItem>()
                .HasIndex(li => li.InvoiceId);

            modelBuilder.Entity<TimeEntry>()
                .HasIndex(t => new { t.MatterId, t.Date });
            modelBuilder.Entity<TimeEntry>()
                .HasIndex("TenantId", nameof(TimeEntry.Date));
            modelBuilder.Entity<TimeEntry>()
                .HasIndex("TenantId", nameof(TimeEntry.MatterId), nameof(TimeEntry.Date));

            modelBuilder.Entity<Expense>()
                .HasIndex(e => new { e.MatterId, e.Date });
            modelBuilder.Entity<Expense>()
                .HasIndex("TenantId", nameof(Expense.Date));
            modelBuilder.Entity<Expense>()
                .HasIndex("TenantId", nameof(Expense.MatterId), nameof(Expense.Date));

            modelBuilder.Entity<Holiday>()
                .HasIndex(h => new { h.Date, h.Jurisdiction });

            modelBuilder.Entity<DocumentVersion>()
                .HasIndex(v => v.DocumentId);

            modelBuilder.Entity<AppointmentRequest>()
                .HasIndex(a => new { a.ClientId, a.RequestedDate });

            modelBuilder.Entity<BillingSettings>()
                .HasIndex("TenantId")
                .IsUnique();

            modelBuilder.Entity<FirmSettings>()
                .HasIndex("TenantId")
                .IsUnique();

            modelBuilder.Entity<IntegrationConnection>()
                .HasIndex("TenantId", nameof(IntegrationConnection.ProviderKey), nameof(IntegrationConnection.Category))
                .IsUnique();

            modelBuilder.Entity<IntegrationConnection>()
                .HasIndex(c => c.Status);

            modelBuilder.Entity<IntegrationConnection>()
                .HasIndex("TenantId", nameof(IntegrationConnection.SyncEnabled), nameof(IntegrationConnection.LastWebhookAt));

            modelBuilder.Entity<IntegrationSecret>()
                .HasIndex("TenantId", nameof(IntegrationSecret.ConnectionId))
                .IsUnique();

            modelBuilder.Entity<IntegrationSecret>()
                .HasIndex(s => s.ProviderKey);

            modelBuilder.Entity<IntegrationSecret>()
                .HasIndex(s => s.EncryptionKeyId);

            modelBuilder.Entity<IntegrationRun>()
                .HasIndex("TenantId", nameof(IntegrationRun.ConnectionId), nameof(IntegrationRun.CreatedAt));

            modelBuilder.Entity<IntegrationRun>()
                .HasIndex("TenantId", nameof(IntegrationRun.ConnectionId), nameof(IntegrationRun.IdempotencyKey))
                .IsUnique();

            modelBuilder.Entity<IntegrationRun>()
                .HasIndex("TenantId", nameof(IntegrationRun.Status), nameof(IntegrationRun.CreatedAt));

            modelBuilder.Entity<IntegrationEntityLink>()
                .HasIndex("TenantId", nameof(IntegrationEntityLink.ConnectionId), nameof(IntegrationEntityLink.LocalEntityType), nameof(IntegrationEntityLink.LocalEntityId))
                .IsUnique();

            modelBuilder.Entity<IntegrationEntityLink>()
                .HasIndex("TenantId", nameof(IntegrationEntityLink.ConnectionId), nameof(IntegrationEntityLink.ExternalEntityType), nameof(IntegrationEntityLink.ExternalEntityId))
                .IsUnique();

            modelBuilder.Entity<IntegrationEntityLink>()
                .HasIndex("TenantId", nameof(IntegrationEntityLink.ProviderKey), nameof(IntegrationEntityLink.LastSyncedAt));

            modelBuilder.Entity<IntegrationMappingProfile>()
                .HasIndex("TenantId", nameof(IntegrationMappingProfile.ConnectionId), nameof(IntegrationMappingProfile.ProfileKey))
                .IsUnique();
            modelBuilder.Entity<IntegrationMappingProfile>()
                .HasIndex("TenantId", nameof(IntegrationMappingProfile.ProviderKey), nameof(IntegrationMappingProfile.EntityType), nameof(IntegrationMappingProfile.Direction));
            modelBuilder.Entity<IntegrationMappingProfile>()
                .HasIndex("TenantId", nameof(IntegrationMappingProfile.Status), nameof(IntegrationMappingProfile.UpdatedAt));

            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationConflictQueueItem.ProviderKey), nameof(IntegrationConflictQueueItem.Status), nameof(IntegrationConflictQueueItem.CreatedAt));
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationConflictQueueItem.ConnectionId), nameof(IntegrationConflictQueueItem.Status), nameof(IntegrationConflictQueueItem.CreatedAt));
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationConflictQueueItem.Fingerprint));

            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationReviewQueueItem.ProviderKey), nameof(IntegrationReviewQueueItem.Status), nameof(IntegrationReviewQueueItem.CreatedAt));
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationReviewQueueItem.ConnectionId), nameof(IntegrationReviewQueueItem.Status), nameof(IntegrationReviewQueueItem.Priority));
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .HasIndex("TenantId", nameof(IntegrationReviewQueueItem.SourceType), nameof(IntegrationReviewQueueItem.SourceId));
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .HasIndex(
                    "TenantId",
                    nameof(IntegrationReviewQueueItem.ProviderKey),
                    nameof(IntegrationReviewQueueItem.ItemType),
                    nameof(IntegrationReviewQueueItem.SourceType),
                    nameof(IntegrationReviewQueueItem.SourceId))
                .IsUnique();

            modelBuilder.Entity<IntegrationInboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationInboxEvent.ProviderKey), nameof(IntegrationInboxEvent.ExternalEventId))
                .IsUnique();
            modelBuilder.Entity<IntegrationInboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationInboxEvent.ConnectionId), nameof(IntegrationInboxEvent.Status), nameof(IntegrationInboxEvent.ReceivedAt));
            modelBuilder.Entity<IntegrationInboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationInboxEvent.RunId), nameof(IntegrationInboxEvent.ReceivedAt));

            modelBuilder.Entity<IntegrationOutboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationOutboxEvent.ConnectionId), nameof(IntegrationOutboxEvent.IdempotencyKey))
                .IsUnique();
            modelBuilder.Entity<IntegrationOutboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationOutboxEvent.ProviderKey), nameof(IntegrationOutboxEvent.Status), nameof(IntegrationOutboxEvent.NextAttemptAt));
            modelBuilder.Entity<IntegrationOutboxEvent>()
                .HasIndex("TenantId", nameof(IntegrationOutboxEvent.RunId), nameof(IntegrationOutboxEvent.CreatedAt));

            modelBuilder.Entity<CourtDocketEntry>()
                .HasIndex("TenantId", nameof(CourtDocketEntry.ProviderKey), nameof(CourtDocketEntry.ExternalDocketId))
                .IsUnique();

            modelBuilder.Entity<CourtDocketEntry>()
                .HasIndex("TenantId", nameof(CourtDocketEntry.MatterId), nameof(CourtDocketEntry.ModifiedAt));

            modelBuilder.Entity<EfilingSubmission>()
                .HasIndex("TenantId", nameof(EfilingSubmission.ProviderKey), nameof(EfilingSubmission.ExternalSubmissionId))
                .IsUnique();

            modelBuilder.Entity<EfilingSubmission>()
                .HasIndex("TenantId", nameof(EfilingSubmission.MatterId), nameof(EfilingSubmission.Status), nameof(EfilingSubmission.LastSeenAt));

            modelBuilder.Entity<JurisdictionDefinition>()
                .HasIndex("TenantId", nameof(JurisdictionDefinition.JurisdictionCode))
                .IsUnique();
            modelBuilder.Entity<JurisdictionDefinition>()
                .HasIndex("TenantId", nameof(JurisdictionDefinition.Scope), nameof(JurisdictionDefinition.IsActive));

            modelBuilder.Entity<JurisdictionRulePack>()
                .HasIndex("TenantId", nameof(JurisdictionRulePack.ScopeKey), nameof(JurisdictionRulePack.Version))
                .IsUnique();
            modelBuilder.Entity<JurisdictionRulePack>()
                .HasIndex("TenantId", nameof(JurisdictionRulePack.JurisdictionCode), nameof(JurisdictionRulePack.Status), nameof(JurisdictionRulePack.EffectiveFrom));
            modelBuilder.Entity<JurisdictionRulePack>()
                .HasIndex("TenantId", nameof(JurisdictionRulePack.CaseType), nameof(JurisdictionRulePack.FilingMethod), nameof(JurisdictionRulePack.Status));

            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .HasIndex("TenantId", nameof(JurisdictionCoverageMatrixEntry.CoverageKey), nameof(JurisdictionCoverageMatrixEntry.Version))
                .IsUnique();
            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .HasIndex("TenantId", nameof(JurisdictionCoverageMatrixEntry.JurisdictionCode), nameof(JurisdictionCoverageMatrixEntry.SupportLevel), nameof(JurisdictionCoverageMatrixEntry.Status));
            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .HasIndex("TenantId", nameof(JurisdictionCoverageMatrixEntry.RulePackId), nameof(JurisdictionCoverageMatrixEntry.Status));

            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .HasIndex("TenantId", nameof(JurisdictionRuleChangeRecord.JurisdictionCode), nameof(JurisdictionRuleChangeRecord.Status), nameof(JurisdictionRuleChangeRecord.CreatedAt));
            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .HasIndex("TenantId", nameof(JurisdictionRuleChangeRecord.RulePackId), nameof(JurisdictionRuleChangeRecord.ChangeType));

            modelBuilder.Entity<JurisdictionValidationTestCase>()
                .HasIndex("TenantId", nameof(JurisdictionValidationTestCase.JurisdictionCode), nameof(JurisdictionValidationTestCase.Status));
            modelBuilder.Entity<JurisdictionValidationTestCase>()
                .HasIndex("TenantId", nameof(JurisdictionValidationTestCase.RulePackId), nameof(JurisdictionValidationTestCase.Status));

            modelBuilder.Entity<JurisdictionValidationTestRun>()
                .HasIndex("TenantId", nameof(JurisdictionValidationTestRun.CreatedAt));
            modelBuilder.Entity<JurisdictionValidationTestRun>()
                .HasIndex("TenantId", nameof(JurisdictionValidationTestRun.JurisdictionCode), nameof(JurisdictionValidationTestRun.Status), nameof(JurisdictionValidationTestRun.CreatedAt));

            modelBuilder.Entity<AppDirectoryListing>()
                .HasIndex("TenantId", nameof(AppDirectoryListing.ProviderKey))
                .IsUnique();

            modelBuilder.Entity<AppDirectoryListing>()
                .HasIndex("TenantId", nameof(AppDirectoryListing.Status), nameof(AppDirectoryListing.UpdatedAt));

            modelBuilder.Entity<AppDirectorySubmission>()
                .HasIndex("TenantId", nameof(AppDirectorySubmission.ListingId), nameof(AppDirectorySubmission.CreatedAt));

            modelBuilder.Entity<AppDirectorySubmission>()
                .HasIndex("TenantId", nameof(AppDirectorySubmission.Status), nameof(AppDirectorySubmission.CreatedAt));

            modelBuilder.Entity<StripeWebhookEvent>()
                .HasIndex("TenantId", nameof(StripeWebhookEvent.EventId))
                .IsUnique();

            modelBuilder.Entity<StripeWebhookEvent>()
                .HasIndex("TenantId", nameof(StripeWebhookEvent.CreatedAt));

            modelBuilder.Entity<ClientStatusHistory>()
                .HasIndex(h => new { h.ClientId, h.CreatedAt });

            modelBuilder.Entity<Matter>()
                .HasIndex(m => new { m.EntityId, m.OfficeId });
            modelBuilder.Entity<Matter>()
                .HasIndex("TenantId", nameof(Matter.OpenDate));
            modelBuilder.Entity<Matter>()
                .HasIndex("TenantId", nameof(Matter.CreatedByUserId));
            modelBuilder.Entity<Matter>()
                .HasIndex("TenantId", nameof(Matter.ShareWithFirm));

            modelBuilder.Entity<Invoice>()
                .HasIndex(i => new { i.EntityId, i.OfficeId });
            modelBuilder.Entity<Invoice>()
                .HasIndex("TenantId", nameof(Invoice.MatterId), nameof(Invoice.IssueDate));

            modelBuilder.Entity<Employee>()
                .HasIndex(e => new { e.EntityId, e.OfficeId });

            modelBuilder.Entity<TrustBankAccount>()
                .HasIndex(a => new { a.EntityId, a.OfficeId });

            modelBuilder.Entity<ClientTrustLedger>()
                .HasIndex(l => new { l.EntityId, l.OfficeId });

            modelBuilder.Entity<TrustTransaction>()
                .HasIndex(t => new { t.EntityId, t.OfficeId });

            modelBuilder.Entity<PaymentTransaction>()
                .HasIndex(p => p.PaymentPlanId);

            modelBuilder.Entity<PaymentPlan>()
                .HasIndex(p => new { p.ClientId, p.Status });

            modelBuilder.Entity<PaymentPlan>()
                .HasIndex(p => p.NextRunDate);

            modelBuilder.Entity<SignatureAuditEntry>()
                .HasIndex(a => new { a.SignatureRequestId, a.CreatedAt });

            modelBuilder.Entity<OutboundEmail>()
                .HasIndex(e => new { e.Status, e.ScheduledFor });

            modelBuilder.Entity<FirmEntity>()
                .HasIndex(e => e.Name);

            modelBuilder.Entity<FirmEntity>()
                .HasIndex(e => e.IsDefault);

            modelBuilder.Entity<FirmEntity>()
                .HasIndex("TenantId", nameof(FirmEntity.IsDefault))
                .HasFilter(defaultFlagIndexFilter)
                .IsUnique();

            modelBuilder.Entity<Office>()
                .HasIndex(o => new { o.EntityId, o.Name });

            modelBuilder.Entity<Office>()
                .HasIndex("TenantId", nameof(Office.EntityId), nameof(Office.IsDefault))
                .HasFilter(defaultFlagIndexFilter)
                .IsUnique();

            modelBuilder.Entity<DocumentContentIndex>()
                .HasIndex(i => i.ContentHash);

            modelBuilder.Entity<DocumentContentToken>()
                .HasKey(t => new { t.DocumentId, t.Token });

            modelBuilder.Entity<DocumentContentToken>()
                .HasIndex(t => t.Token);

            modelBuilder.Entity<DocumentShare>()
                .HasIndex(s => new { s.DocumentId, s.ClientId })
                .IsUnique();

            modelBuilder.Entity<DocumentShare>()
                .HasIndex(s => new { s.ClientId, s.ExpiresAt });

            modelBuilder.Entity<DocumentComment>()
                .HasIndex(c => new { c.DocumentId, c.CreatedAt });

            modelBuilder.Entity<Office>()
                .HasOne(o => o.Entity)
                .WithMany(e => e.Offices)
                .HasForeignKey(o => o.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        public override int SaveChanges()
        {
            ApplyEmailNormalizationRules();
            ApplyTenantRules();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyEmailNormalizationRules();
            ApplyTenantRules();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyEmailNormalizationRules();
            ApplyTenantRules();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplyEmailNormalizationRules();
            ApplyTenantRules();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyTenantFilters(ModelBuilder modelBuilder)
        {
            var method = typeof(JurisFlowDbContext).GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null)
            {
                return;
            }

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.ClrType == typeof(Tenant) || entityType.IsOwned())
                {
                    continue;
                }

                var generic = method.MakeGenericMethod(entityType.ClrType);
                generic.Invoke(this, new object[] { modelBuilder });
            }
        }

        private void SetTenantFilter<T>(ModelBuilder modelBuilder) where T : class
        {
            modelBuilder.Entity<T>().Property<string>("TenantId").HasMaxLength(64);
            modelBuilder.Entity<T>().HasIndex("TenantId");
            modelBuilder.Entity<T>().HasQueryFilter(e => !RequireTenant || EF.Property<string>(e, "TenantId") == TenantId);
        }

        private void ApplyTenantRules()
        {
            var tenantId = _tenantContext?.TenantId;
            var hasTenant = !string.IsNullOrWhiteSpace(tenantId);

            foreach (var entry in ChangeTracker.Entries())
            {
                var tenantProperty = entry.Metadata.FindProperty("TenantId");
                if (tenantProperty == null)
                {
                    continue;
                }

                if (!hasTenant)
                {
                    throw new InvalidOperationException("TenantId is required for data changes.");
                }

                if (entry.State == EntityState.Added)
                {
                    entry.Property("TenantId").CurrentValue = tenantId;
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var persistedTenantId = entry.Property("TenantId").OriginalValue?.ToString();
                    if (string.IsNullOrWhiteSpace(persistedTenantId))
                    {
                        var dbValues = entry.GetDatabaseValues();
                        persistedTenantId = dbValues?["TenantId"]?.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(persistedTenantId))
                    {
                        persistedTenantId = entry.Property("TenantId").CurrentValue?.ToString();
                    }

                    if (!string.Equals(persistedTenantId, tenantId, StringComparison.Ordinal))
                    {
                        throw entry.State == EntityState.Modified
                            ? new InvalidOperationException("Cross-tenant data modification is not allowed.")
                            : new InvalidOperationException("Cross-tenant data deletion is not allowed.");
                    }

                    if (entry.State == EntityState.Modified)
                    {
                        entry.Property("TenantId").CurrentValue = persistedTenantId;
                        entry.Property("TenantId").IsModified = false;
                    }
                }
            }
        }

        private void ApplyEmailNormalizationRules()
        {
            foreach (var entry in ChangeTracker.Entries<User>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var normalized = EmailAddressNormalizer.Normalize(entry.Entity.Email);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    entry.Entity.Email = entry.Entity.Email?.Trim() ?? string.Empty;
                    entry.Entity.NormalizedEmail = normalized;
                }
            }

            foreach (var entry in ChangeTracker.Entries<Client>())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified))
                {
                    continue;
                }

                var normalized = EmailAddressNormalizer.Normalize(entry.Entity.Email);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    entry.Entity.Email = entry.Entity.Email?.Trim() ?? string.Empty;
                    entry.Entity.NormalizedEmail = normalized;
                }
            }
        }

        private void ApplyEncryption(ModelBuilder modelBuilder)
        {
            if (_dbEncryptionService == null)
            {
                return;
            }

            var converter = new ValueConverter<string?, string?>(
                v => _dbEncryptionService.EncryptString(v),
                v => _dbEncryptionService.DecryptString(v));

            var requiredConverter = new ValueConverter<string, string>(
                v => _dbEncryptionService.EncryptString(v) ?? string.Empty,
                v => _dbEncryptionService.DecryptString(v) ?? string.Empty);

            modelBuilder.Entity<Client>()
                .Property(c => c.Phone)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.Mobile)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.Address)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.City)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.State)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.ZipCode)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.Country)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.TaxId)
                .HasConversion(converter);
            modelBuilder.Entity<Client>()
                .Property(c => c.Notes)
                .HasConversion(converter);
            modelBuilder.Entity<MatterNote>()
                .Property(n => n.Title)
                .HasConversion(converter);
            modelBuilder.Entity<MatterNote>()
                .Property(n => n.Body)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<Employee>()
                .Property(e => e.Phone)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.Mobile)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.Notes)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.Address)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.EmergencyContact)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.EmergencyPhone)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.BarNumber)
                .HasConversion(converter);
            modelBuilder.Entity<Employee>()
                .Property(e => e.BarJurisdiction)
                .HasConversion(converter);

            modelBuilder.Entity<ClientMessage>()
                .Property(m => m.Subject)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<ClientMessage>()
                .Property(m => m.Body)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<ClientMessage>()
                .Property(m => m.AttachmentsJson)
                .HasConversion(converter);

            modelBuilder.Entity<StaffMessage>()
                .Property(m => m.Body)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<StaffMessage>()
                .Property(m => m.AttachmentsJson)
                .HasConversion(converter);

            modelBuilder.Entity<SmsMessage>()
                .Property(s => s.FromNumber)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<SmsMessage>()
                .Property(s => s.ToNumber)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<SmsMessage>()
                .Property(s => s.Body)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<SmsMessage>()
                .Property(s => s.ErrorMessage)
                .HasConversion(converter);

            modelBuilder.Entity<SmsTemplate>()
                .Property(t => t.Body)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<SmsReminder>()
                .Property(r => r.ToNumber)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<SmsReminder>()
                .Property(r => r.Message)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.Subject)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.FromAddress)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.FromName)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.ToAddresses)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.CcAddresses)
                .HasConversion(converter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.BccAddresses)
                .HasConversion(converter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.BodyText)
                .HasConversion(converter);
            modelBuilder.Entity<EmailMessage>()
                .Property(e => e.BodyHtml)
                .HasConversion(converter);

            modelBuilder.Entity<EmailAccount>()
                .Property(a => a.AccessToken)
                .HasConversion(converter);
            modelBuilder.Entity<EmailAccount>()
                .Property(a => a.RefreshToken)
                .HasConversion(converter);

            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.ToAddress)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.FromAddress)
                .HasConversion(converter);
            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.Subject)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.BodyText)
                .HasConversion(converter);
            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.BodyHtml)
                .HasConversion(converter);
            modelBuilder.Entity<OutboundEmail>()
                .Property(e => e.ErrorMessage)
                .HasConversion(converter);

            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.ExternalTransactionId)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.FailureReason)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.RefundReason)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.ReceiptUrl)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.PayerEmail)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.PayerName)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.CardLast4)
                .HasConversion(converter);
            modelBuilder.Entity<PaymentTransaction>()
                .Property(p => p.CardBrand)
                .HasConversion(converter);

            modelBuilder.Entity<PaymentPlan>()
                .Property(p => p.AutoPayReference)
                .HasConversion(converter);

            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.TaxPolicyJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.SplitBillingJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.EbillingProfileJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.CollectionPolicyJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.TrustPolicyJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<MatterBillingPolicy>()
                .Property(p => p.Notes)
                .HasConversion(converter);

            modelBuilder.Entity<BillingRateCard>()
                .Property(r => r.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingRateCardEntry>()
                .Property(r => r.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingPrebillBatch>()
                .Property(b => b.ReviewNotes)
                .HasConversion(converter);
            modelBuilder.Entity<BillingPrebillBatch>()
                .Property(b => b.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingPrebillLine>()
                .Property(l => l.ReviewerNotes)
                .HasConversion(converter);
            modelBuilder.Entity<BillingPrebillLine>()
                .Property(l => l.SplitAllocationJson)
                .HasConversion(converter);
            modelBuilder.Entity<BillingPrebillLine>()
                .Property(l => l.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingLedgerEntry>()
                .Property(l => l.Description)
                .HasConversion(converter);
            modelBuilder.Entity<BillingLedgerEntry>()
                .Property(l => l.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingPaymentAllocation>()
                .Property(a => a.Notes)
                .HasConversion(converter);
            modelBuilder.Entity<BillingPaymentAllocation>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<InvoicePayorAllocation>()
                .Property(a => a.Terms)
                .HasConversion(converter);
            modelBuilder.Entity<InvoicePayorAllocation>()
                .Property(a => a.Reference)
                .HasConversion(converter);
            modelBuilder.Entity<InvoicePayorAllocation>()
                .Property(a => a.PurchaseOrder)
                .HasConversion(converter);
            modelBuilder.Entity<InvoicePayorAllocation>()
                .Property(a => a.EbillingProfileJson)
                .HasConversion(converter);
            modelBuilder.Entity<InvoicePayorAllocation>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<InvoiceLinePayorAllocation>()
                .Property(a => a.EbillingProfileJson)
                .HasConversion(converter);
            modelBuilder.Entity<InvoiceLinePayorAllocation>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingEbillingTransmission>()
                .Property(t => t.ErrorMessage)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingTransmission>()
                .Property(t => t.RequestPayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingTransmission>()
                .Property(t => t.ResponsePayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingTransmission>()
                .Property(t => t.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<BillingEbillingResultEvent>()
                .Property(e => e.ResultMessage)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .Property(e => e.ErrorMessage)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .Property(e => e.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<BillingEbillingResultEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustRiskPolicy>()
                .Property(p => p.Description)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskPolicy>()
                .Property(p => p.EnabledRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskPolicy>()
                .Property(p => p.RuleWeightsJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskPolicy>()
                .Property(p => p.ActionMapJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskPolicy>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustRiskEvent>()
                .Property(e => e.RiskReasonsJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskEvent>()
                .Property(e => e.EvidenceJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskEvent>()
                .Property(e => e.FeaturesJson)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustRiskAction>()
                .Property(a => a.Notes)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskAction>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustRiskHold>()
                .Property(h => h.Reason)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskHold>()
                .Property(h => h.ReleaseReason)
                .HasConversion(converter);
            modelBuilder.Entity<TrustRiskHold>()
                .Property(h => h.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustRiskReviewLink>()
                .Property(l => l.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftSession>()
                .Property(s => s.Title)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftSession>()
                .Property(s => s.JurisdictionContextJson)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftSession>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftOutput>()
                .Property(o => o.RenderedText)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<AiDraftOutput>()
                .Property(o => o.RetrievalBundleJson)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftOutput>()
                .Property(o => o.StructuredClaimsJson)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftOutput>()
                .Property(o => o.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftClaim>()
                .Property(c => c.ClaimText)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<AiDraftClaim>()
                .Property(c => c.SupportSummary)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftClaim>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftEvidenceLink>()
                .Property(e => e.Excerpt)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftEvidenceLink>()
                .Property(e => e.WhySupports)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftEvidenceLink>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftRuleCitation>()
                .Property(c => c.SourceCitation)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftRuleCitation>()
                .Property(c => c.CitationText)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftRuleCitation>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<AiDraftVerificationRun>()
                .Property(v => v.ResultJson)
                .HasConversion(converter);
            modelBuilder.Entity<AiDraftVerificationRun>()
                .Property(v => v.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeePlan>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .Property(v => v.SourceSignalsJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .Property(v => v.InputSnapshotJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .Property(v => v.SummaryJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeePlanVersion>()
                .Property(v => v.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeScenario>()
                .Property(s => s.DriverSummary)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeScenario>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeePhaseForecast>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeStaffingLine>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeAssumption>()
                .Property(a => a.ValueJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeAssumption>()
                .Property(a => a.Notes)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeAssumption>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeCollectionsForecast>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeUpdateEvent>()
                .Property(e => e.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeUpdateEvent>()
                .Property(e => e.ResultJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeUpdateEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<OutcomeFeeCalibrationSnapshot>()
                .Property(s => s.MetricsJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeCalibrationSnapshot>()
                .Property(s => s.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<OutcomeFeeCalibrationSnapshot>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyProfile>()
                .Property(p => p.VisibilityRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyProfile>()
                .Property(p => p.RedactionRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyProfile>()
                .Property(p => p.SourceWhitelistJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyProfile>()
                .Property(p => p.DelayTaxonomyJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyProfile>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencySnapshot>()
                .Property(s => s.SnapshotSummary)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencySnapshot>()
                .Property(s => s.WhatChangedSummary)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencySnapshot>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyTimelineItem>()
                .Property(i => i.ClientSafeText)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyTimelineItem>()
                .Property(i => i.SourceRefsJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyTimelineItem>()
                .Property(i => i.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyDelayReason>()
                .Property(r => r.ClientSafeText)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyDelayReason>()
                .Property(r => r.SourceRefsJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyDelayReason>()
                .Property(r => r.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyNextStep>()
                .Property(s => s.ActionText)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyNextStep>()
                .Property(s => s.BlockedByText)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyNextStep>()
                .Property(s => s.SourceRefsJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyNextStep>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyCostImpact>()
                .Property(c => c.DriverSummary)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyCostImpact>()
                .Property(c => c.DriversJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyCostImpact>()
                .Property(c => c.SourceRefsJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyCostImpact>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyUpdateEvent>()
                .Property(e => e.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyUpdateEvent>()
                .Property(e => e.DiffJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyUpdateEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<ClientTransparencyReviewAction>()
                .Property(a => a.Reason)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyReviewAction>()
                .Property(a => a.BeforeJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyReviewAction>()
                .Property(a => a.AfterJson)
                .HasConversion(converter);
            modelBuilder.Entity<ClientTransparencyReviewAction>()
                .Property(a => a.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<TrustBankAccount>()
                .Property(t => t.AccountNumberEnc)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<TrustBankAccount>()
                .Property(t => t.RoutingNumber)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<ClientStatusHistory>()
                .Property(h => h.Notes)
                .HasConversion(converter);

            modelBuilder.Entity<AuditLog>()
                .Property(a => a.Details)
                .HasConversion(converter);

            modelBuilder.Entity<DocumentContentIndex>()
                .Property(i => i.Content)
                .HasConversion(converter);
            modelBuilder.Entity<DocumentContentIndex>()
                .Property(i => i.NormalizedContent)
                .HasConversion(converter);

            modelBuilder.Entity<DocumentShare>()
                .Property(s => s.Note)
                .HasConversion(converter);

            modelBuilder.Entity<DocumentComment>()
                .Property(c => c.Body)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<IntegrationConnection>()
                .Property(c => c.AccountEmail)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConnection>()
                .Property(c => c.ExternalAccountId)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConnection>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationSecret>()
                .Property(s => s.SecretJson)
                .HasConversion(requiredConverter);

            modelBuilder.Entity<IntegrationEntityLink>()
                .Property(l => l.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.FieldMappingsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.EnumMappingsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.TaxMappingsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.AccountMappingsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.DefaultsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationMappingProfile>()
                .Property(p => p.ValidationSummary)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.Summary)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.LocalSnapshotJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.ExternalSnapshotJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.SuggestedResolutionJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.ResolutionJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationConflictQueueItem>()
                .Property(c => c.ReviewNotes)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .Property(c => c.Title)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .Property(c => c.Summary)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .Property(c => c.ContextJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .Property(c => c.SuggestedActionsJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationReviewQueueItem>()
                .Property(c => c.DecisionNotes)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationInboxEvent>()
                .Property(e => e.HeadersJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationInboxEvent>()
                .Property(e => e.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationInboxEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationInboxEvent>()
                .Property(e => e.ErrorMessage)
                .HasConversion(converter);

            modelBuilder.Entity<IntegrationOutboxEvent>()
                .Property(e => e.HeadersJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationOutboxEvent>()
                .Property(e => e.PayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationOutboxEvent>()
                .Property(e => e.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<IntegrationOutboxEvent>()
                .Property(e => e.ErrorMessage)
                .HasConversion(converter);

            modelBuilder.Entity<CourtDocketEntry>()
                .Property(d => d.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<EfilingSubmission>()
                .Property(s => s.RejectionReason)
                .HasConversion(converter);
            modelBuilder.Entity<EfilingSubmission>()
                .Property(s => s.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionDefinition>()
                .Property(j => j.MetadataJson)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.SourceCitation)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.DocumentRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.FeeRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.ServiceRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.DeadlineRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.LocalOverridesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.ValidationRulesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRulePack>()
                .Property(r => r.ReviewNotes)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .Property(c => c.CapabilitiesJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .Property(c => c.ConstraintsJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .Property(c => c.MetadataJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionCoverageMatrixEntry>()
                .Property(c => c.SourceCitation)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .Property(c => c.Summary)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .Property(c => c.SourceCitation)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .Property(c => c.DiffJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .Property(c => c.SourcePayloadJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionRuleChangeRecord>()
                .Property(c => c.ReviewNotes)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionValidationTestCase>()
                .Property(t => t.PacketInputJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionValidationTestCase>()
                .Property(t => t.ExpectedOutputJson)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionValidationTestCase>()
                .Property(t => t.Notes)
                .HasConversion(converter);

            modelBuilder.Entity<JurisdictionValidationTestRun>()
                .Property(t => t.Summary)
                .HasConversion(converter);
            modelBuilder.Entity<JurisdictionValidationTestRun>()
                .Property(t => t.ResultJson)
                .HasConversion(converter);

            modelBuilder.Entity<AppDirectoryListing>()
                .Property(l => l.ManifestJson)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<AppDirectoryListing>()
                .Property(l => l.LastTestReportJson)
                .HasConversion(converter);
            modelBuilder.Entity<AppDirectoryListing>()
                .Property(l => l.ReviewNotes)
                .HasConversion(converter);

            modelBuilder.Entity<AppDirectorySubmission>()
                .Property(s => s.ManifestJson)
                .HasConversion(requiredConverter);
            modelBuilder.Entity<AppDirectorySubmission>()
                .Property(s => s.ValidationErrorsJson)
                .HasConversion(converter);
            modelBuilder.Entity<AppDirectorySubmission>()
                .Property(s => s.TestReportJson)
                .HasConversion(converter);
        }
    }
}
