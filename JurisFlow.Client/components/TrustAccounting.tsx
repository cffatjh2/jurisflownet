/**
 * IOLTA Trust Accounting Dashboard
 * ABA Model Rule 1.15 Compliant
 */

import React, { useState, useEffect, useMemo, useRef } from 'react';
import jsPDF from 'jspdf';
import {
    AlertTriangle, DollarSign, Scale, RefreshCw,
    Users, Plus, Eye, Check, X, FileText, XCircle
} from './Icons';
import { useData } from '../contexts/DataContext';
import { useAuth } from '../contexts/AuthContext';
import {
    TrustBankAccount, ClientTrustLedger, TrustTransactionV2,
    ReconciliationRecord, AuditLogEntry,
    TrustStatementImport, TrustOutstandingItem, TrustReconciliationPacket, TrustRiskHold,
    TrustApprovalQueueItemDto, TrustApprovalRequirementDto, TrustMonthCloseDto,
    TrustComplianceExportDto, TrustComplianceExportListItemDto, TrustComplianceExportRequest,
    TrustStatementLine, TrustStatementMatchingRunResult, TrustReconciliationPacketDetail,
    TrustOperationalAlertSummary, TrustOperationalAlertDto, TrustOperationalAlertEventDto,
    TrustAsOfProjectionRecoveryResult, TrustPacketRegenerationResult, TrustComplianceBundleResult,
    TrustEvidenceFile, TrustStatementParserRun, TrustJurisdictionPacketTemplateUpsertDto, TrustPacketTemplateAttestationDto,
    TrustOpsInboxSummaryDto, TrustOpsInboxEventDto, TrustOpsInboxItemDto, TrustBundleIntegrityDto,
    TrustCloseForecastSummaryDto, TrustCloseForecastSyncResultDto, TrustCloseForecastSnapshotDto
} from '../types';
import { api as apiClient } from '../services/api';
import EntityOfficeFilter from './common/EntityOfficeFilter';

// Simple icons for Trust-specific actions (inline SVG)
const ArrowDownCircle = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <circle cx="12" cy="12" r="10" /><path d="M12 8v8" /><path d="m8 12 4 4 4-4" />
    </svg>
);

const ArrowUpCircle = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <circle cx="12" cy="12" r="10" /><path d="M12 16V8" /><path d="m8 12 4-4 4 4" />
    </svg>
);

const Calculator = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <rect width="16" height="20" x="4" y="2" rx="2" /><line x1="8" x2="16" y1="6" y2="6" /><line x1="16" x2="16" y1="14" y2="18" /><path d="M8 10h.01" /><path d="M12 10h.01" /><path d="M16 10h.01" /><path d="M8 14h.01" /><path d="M12 14h.01" /><path d="M8 18h.01" /><path d="M12 18h.01" />
    </svg>
);

const CheckCircle2 = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <circle cx="12" cy="12" r="10" /><path d="m9 12 2 2 4-4" />
    </svg>
);

const Ban = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <circle cx="12" cy="12" r="10" /><path d="m4.9 4.9 14.2 14.2" />
    </svg>
);

const FileCheck = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z" /><polyline points="14 2 14 8 20 8" /><path d="m9 15 2 2 4-4" />
    </svg>
);

const History = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" /><path d="M3 3v5h5" /><path d="M12 7v5l4 2" />
    </svg>
);

const Building2 = ({ className }: { className?: string }) => (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className}>
        <path d="M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z" /><path d="M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2" /><path d="M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2" /><path d="M10 6h4" /><path d="M10 10h4" /><path d="M10 14h4" /><path d="M10 18h4" />
    </svg>
);

import { toast } from './Toast';

const trustApi = {
    get: (url: string) => apiClient.get(url),
    post: (url: string, data: any) => apiClient.post(url, data)
};

export default function TrustAccounting() {
    const { clients, matters } = useData();
    const { can, user } = useAuth();
    const canApprove = can('trust.approve');
    const canVoid = can('trust.void');
    const canDeposit = can('trust.deposit');
    const canWithdraw = can('trust.withdraw');
    const canReconcile = can('trust.reconcile');
    const canSignoff = canApprove;
    const canExport = can('trust.export');

    // State
    const [activeTab, setActiveTab] = useState<'overview' | 'accounts' | 'ledgers' | 'transactions' | 'reconciliation' | 'audit'>('overview');
    const [accounts, setAccounts] = useState<TrustBankAccount[]>([]);
    const [ledgers, setLedgers] = useState<ClientTrustLedger[]>([]);
    const [transactions, setTransactions] = useState<TrustTransactionV2[]>([]);
    const [reconciliations, setReconciliations] = useState<ReconciliationRecord[]>([]);
    const [statementImports, setStatementImports] = useState<TrustStatementImport[]>([]);
    const [evidenceFiles, setEvidenceFiles] = useState<TrustEvidenceFile[]>([]);
    const [parserRuns, setParserRuns] = useState<TrustStatementParserRun[]>([]);
    const [outstandingItems, setOutstandingItems] = useState<TrustOutstandingItem[]>([]);
    const [reconciliationPackets, setReconciliationPackets] = useState<TrustReconciliationPacket[]>([]);
    const [packetTemplates, setPacketTemplates] = useState<TrustJurisdictionPacketTemplateUpsertDto[]>([]);
    const [approvalQueue, setApprovalQueue] = useState<TrustApprovalQueueItemDto[]>([]);
    const [monthCloses, setMonthCloses] = useState<TrustMonthCloseDto[]>([]);
    const [exportHistory, setExportHistory] = useState<TrustComplianceExportListItemDto[]>([]);
    const [projectionRecoveryResult, setProjectionRecoveryResult] = useState<TrustAsOfProjectionRecoveryResult | null>(null);
    const [packetRegenerationResult, setPacketRegenerationResult] = useState<TrustPacketRegenerationResult | null>(null);
    const [complianceBundleResult, setComplianceBundleResult] = useState<TrustComplianceBundleResult | null>(null);
    const [projectionRecoveryBusy, setProjectionRecoveryBusy] = useState(false);
    const [packetRegenerationBusy, setPacketRegenerationBusy] = useState(false);
    const [complianceBundleBusy, setComplianceBundleBusy] = useState(false);
    const [bundleIntegrityBusy, setBundleIntegrityBusy] = useState(false);
    const [bundleIntegrity, setBundleIntegrity] = useState<TrustBundleIntegrityDto | null>(null);
    const [closeForecastSummary, setCloseForecastSummary] = useState<TrustCloseForecastSummaryDto | null>(null);
    const [closeForecastSyncResult, setCloseForecastSyncResult] = useState<TrustCloseForecastSyncResultDto | null>(null);
    const [closeForecastBusy, setCloseForecastBusy] = useState(false);
    const [openHolds, setOpenHolds] = useState<TrustRiskHold[]>([]);
    const [operationalAlertSummary, setOperationalAlertSummary] = useState<TrustOperationalAlertSummary | null>(null);
    const [operationalAlertBusyId, setOperationalAlertBusyId] = useState<string | null>(null);
    const [selectedOperationalAlertId, setSelectedOperationalAlertId] = useState<string | null>(null);
    const [selectedOperationalAlertHistory, setSelectedOperationalAlertHistory] = useState<TrustOperationalAlertEventDto[]>([]);
    const [operationalAlertHistoryLoading, setOperationalAlertHistoryLoading] = useState(false);
    const [opsInboxSummary, setOpsInboxSummary] = useState<TrustOpsInboxSummaryDto | null>(null);
    const [opsInboxBusyId, setOpsInboxBusyId] = useState<string | null>(null);
    const [selectedOpsInboxId, setSelectedOpsInboxId] = useState<string | null>(null);
    const [selectedOpsInboxHistory, setSelectedOpsInboxHistory] = useState<TrustOpsInboxEventDto[]>([]);
    const [opsInboxHistoryLoading, setOpsInboxHistoryLoading] = useState(false);
    const [auditLogs, setAuditLogs] = useState<AuditLogEntry[]>([]);
    const [auditLoading, setAuditLoading] = useState(false);
    const [selectedPacketId, setSelectedPacketId] = useState('');
    const [selectedStatementImportId, setSelectedStatementImportId] = useState('');
    const [packetDetail, setPacketDetail] = useState<TrustReconciliationPacketDetail | null>(null);
    const [statementLines, setStatementLines] = useState<TrustStatementLine[]>([]);
    const [packetDetailLoading, setPacketDetailLoading] = useState(false);
    const [statementLinesLoading, setStatementLinesLoading] = useState(false);
    const [matchingRunLoading, setMatchingRunLoading] = useState(false);
    const [lineResolutionLoadingId, setLineResolutionLoadingId] = useState<string | null>(null);
    const [lineMatchSelections, setLineMatchSelections] = useState<Record<string, string>>({});
    const [auditFilters, setAuditFilters] = useState({ entityType: 'all', query: '' });
    const [loading, setLoading] = useState(true);
    const [secondaryDataLoading, setSecondaryDataLoading] = useState(false);
    const [entityFilter, setEntityFilter] = useState('');
    const [officeFilter, setOfficeFilter] = useState('');
    const [showPacketTemplateAdvanced, setShowPacketTemplateAdvanced] = useState(false);
    const [auditAccessDenied, setAuditAccessDenied] = useState(false);
    const backgroundTrustRefreshTimerRef = useRef<number | null>(null);
    const backgroundTrustRefreshStartedRef = useRef(false);

    // Modal states
    const [showDepositForm, setShowDepositForm] = useState(false);
    const [showWithdrawalForm, setShowWithdrawalForm] = useState(false);
    const [showReconcileForm, setShowReconcileForm] = useState(false);
    const [showCreateLedger, setShowCreateLedger] = useState(false);
    const [showCreateAccount, setShowCreateAccount] = useState(false);
    const [selectedAccount, setSelectedAccount] = useState<string>('');

    // Form states
    const [depositForm, setDepositForm] = useState({
        trustAccountId: '',
        amount: '',
        payorPayee: '',
        description: '',
        checkNumber: '',
        allocations: [{ ledgerId: '', amount: '', description: '' }]
    });

    const [withdrawalForm, setWithdrawalForm] = useState({
        trustAccountId: '',
        ledgerId: '',
        amount: '',
        payorPayee: '',
        description: '',
        checkNumber: ''
    });

    const [reconcileForm, setReconcileForm] = useState({
        trustAccountId: '',
        periodEnd: new Date().toISOString().split('T')[0],
        bankStatementBalance: '',
        notes: ''
    });

    const [statementImportForm, setStatementImportForm] = useState({
        trustAccountId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        statementEndingBalance: '',
        source: 'manual',
        sourceFileName: '',
        sourceFileHash: '',
        sourceEvidenceKey: '',
        allowDuplicateImport: false,
        notes: '',
        linesJson: ''
    });

    const [evidenceFileForm, setEvidenceFileForm] = useState({
        trustAccountId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        source: 'bank_portal_upload',
        fileName: '',
        contentType: 'text/csv',
        fileHash: '',
        evidenceKey: '',
        fileSizeBytes: '',
        allowDuplicateRegistration: false,
        notes: ''
    });

    const [parserRunForm, setParserRunForm] = useState({
        trustAccountId: '',
        trustEvidenceFileId: '',
        parserKey: 'manual_manifest_v1',
        statementEndingBalance: '',
        source: 'evidence_registry',
        allowDuplicateImport: false,
        notes: '',
        linesJson: ''
    });

    const [packetTemplateForm, setPacketTemplateForm] = useState({
        policyKey: 'jurisdiction:default',
        jurisdiction: 'DEFAULT',
        accountType: 'all',
        templateKey: 'baseline-close-packet',
        name: 'Baseline Close Packet',
        versionNumber: '1',
        requiredSectionsText: 'statement_summary, three_way_summary, outstanding_schedule, signoff_chain, responsible_lawyer_block',
        disclosureBlocksText: '',
        requiredAttestationsText: JSON.stringify([
            { key: 'reviewed_three_way_reconciliation', label: 'Reviewer confirms the three-way reconciliation and exceptions queue were reviewed.', role: 'reviewer', required: true },
            { key: 'responsible_lawyer_certification', label: 'Responsible lawyer certifies the packet is complete and ready for retention/export.', role: 'responsible_lawyer', required: true }
        ], null, 2),
        renderingProfileJson: '',
        metadataJson: ''
    });

    const [outstandingItemForm, setOutstandingItemForm] = useState({
        trustAccountId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        itemType: 'other_adjustment',
        impactDirection: 'decrease_bank',
        amount: '',
        reference: '',
        description: '',
        reasonCode: '',
        attachmentEvidenceKey: ''
    });

    const [packetForm, setPacketForm] = useState({
        trustAccountId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        statementImportId: '',
        statementEndingBalance: '',
        notes: ''
    });

    const [projectionRecoveryForm, setProjectionRecoveryForm] = useState({
        trustAccountId: '',
        asOfUtc: '',
        commitProjectionRepair: false,
        onlyIfDrifted: true
    });

    const [packetRecoveryForm, setPacketRecoveryForm] = useState({
        trustAccountId: '',
        trustReconciliationPacketId: '',
        trustMonthCloseId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        statementImportId: '',
        statementEndingBalance: '',
        reason: '',
        notes: '',
        autoPrepareMonthClose: true
    });

    const [bundleForm, setBundleForm] = useState({
        trustAccountId: '',
        trustMonthCloseId: '',
        trustReconciliationPacketId: '',
        periodStart: new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().split('T')[0],
        periodEnd: new Date().toISOString().split('T')[0],
        includeJsonPacket: true,
        includeAccountJournalCsv: true,
        includeApprovalRegisterCsv: true,
        includeClientLedgerCards: true,
        notes: ''
    });
    const [bundleSignForm, setBundleSignForm] = useState({
        retentionPolicyTag: 'trust_default',
        redactionProfile: 'internal_unredacted',
        notes: ''
    });

    const [ledgerForm, setLedgerForm] = useState({
        clientId: '',
        matterId: '',
        trustAccountId: '',
        notes: ''
    });

    const [accountForm, setAccountForm] = useState({
        name: '',
        bankName: '',
        routingNumber: '',
        accountNumber: '',
        jurisdiction: 'CA',  // Default to California
        entityId: '',
        officeId: ''
    });

    // US States for IOLTA jurisdiction
    const usStates = [
        'AL', 'AK', 'AZ', 'AR', 'CA', 'CO', 'CT', 'DE', 'FL', 'GA',
        'HI', 'ID', 'IL', 'IN', 'IA', 'KS', 'KY', 'LA', 'ME', 'MD',
        'MA', 'MI', 'MN', 'MS', 'MO', 'MT', 'NE', 'NV', 'NH', 'NJ',
        'NM', 'NY', 'NC', 'ND', 'OH', 'OK', 'OR', 'PA', 'RI', 'SC',
        'SD', 'TN', 'TX', 'UT', 'VT', 'VA', 'WA', 'WV', 'WI', 'WY', 'DC'
    ];

    const canViewAdminAuditLogs = useMemo(() => {
        const normalizedRole = String(user?.role || '').replace(/\s+/g, '').toLowerCase();
        return normalizedRole === 'admin' || normalizedRole === 'securityadmin';
    }, [user?.role]);

    const canLoadAdvancedTrustWorkspace = canReconcile || canApprove || canExport;

    const requiredAttestationPreview = useMemo(() => {
        const rawValue = packetTemplateForm.requiredAttestationsText.trim();
        if (!rawValue) {
            return { items: [] as TrustPacketTemplateAttestationDto[], error: '' };
        }

        try {
            const parsed = JSON.parse(rawValue);
            if (!Array.isArray(parsed)) {
                return { items: [] as TrustPacketTemplateAttestationDto[], error: 'Required attestations JSON must be an array.' };
            }

            return { items: parsed as TrustPacketTemplateAttestationDto[], error: '' };
        } catch {
            return { items: [] as TrustPacketTemplateAttestationDto[], error: 'Required attestations JSON is invalid.' };
        }
    }, [packetTemplateForm.requiredAttestationsText]);

    // Load data
    useEffect(() => {
        void loadData();

        return () => {
            if (backgroundTrustRefreshTimerRef.current !== null) {
                window.clearTimeout(backgroundTrustRefreshTimerRef.current);
            }
        };
    }, []);

    const loadOperationalAlerts = async (sync = false) => {
        if (sync) {
            await trustApi.post('/api/trust/operational-alerts/sync', {}).catch(() => null);
        }
        const summary = await trustApi.get('/api/trust/operational-alerts').catch(() => null);
        setOperationalAlertSummary(summary || null);
    };

    const loadOpsInbox = async (sync = false) => {
        if (sync) {
            await trustApi.post('/api/trust/ops-inbox/sync', {}).catch(() => null);
        }
        const summary = await trustApi.get('/api/trust/ops-inbox').catch(() => null);
        setOpsInboxSummary(summary || null);
    };

    const loadCloseForecast = async (sync = false) => {
        setCloseForecastBusy(sync);
        try {
            if (sync) {
                const syncResult = await trustApi.post('/api/trust/close-forecast/sync?generateDraftBundles=true', {}).catch(() => null);
                setCloseForecastSyncResult(syncResult || null);
            }

            const summary = await trustApi.get('/api/trust/close-forecast?actionableOnly=false').catch(() => null);
            setCloseForecastSummary(summary || null);
        } finally {
            setCloseForecastBusy(false);
        }
    };

    const applyDefaultTrustAccount = (accountsData: TrustBankAccount[]) => {
        if (!accountsData || accountsData.length === 0) {
            return;
        }

        setSelectedAccount(accountsData[0].id);
        setDepositForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setWithdrawalForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setReconcileForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setStatementImportForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setEvidenceFileForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setParserRunForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setOutstandingItemForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setPacketForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setProjectionRecoveryForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setPacketRecoveryForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
        setBundleForm(f => ({ ...f, trustAccountId: accountsData[0].id }));
    };

    const loadSecondaryTrustData = async () => {
        if (!canLoadAdvancedTrustWorkspace) {
            setSecondaryDataLoading(false);
            return;
        }

        setSecondaryDataLoading(true);
        try {
            const [statementsData, evidenceData, parserRunData, outstandingData, packetData, approvalData, monthCloseData, packetTemplateData, exportData] = await Promise.all([
                trustApi.get('/api/trust/statements').catch(() => []),
                trustApi.get('/api/trust/evidence-files').catch(() => []),
                trustApi.get('/api/trust/parser-runs').catch(() => []),
                trustApi.get('/api/trust/outstanding-items').catch(() => []),
                trustApi.get('/api/trust/reconciliation-packets').catch(() => []),
                canApprove ? trustApi.get('/api/trust/approvals').catch(() => []) : Promise.resolve([]),
                trustApi.get('/api/trust/month-close').catch(() => []),
                trustApi.get('/api/trust/packet-templates').catch(() => []),
                canExport ? trustApi.get('/api/trust/exports').catch(() => []) : Promise.resolve([])
            ]);

            setStatementImports(statementsData || []);
            setEvidenceFiles(evidenceData || []);
            setParserRuns(parserRunData || []);
            setOutstandingItems(outstandingData || []);
            setReconciliationPackets(packetData || []);
            setApprovalQueue(approvalData || []);
            setMonthCloses(monthCloseData || []);
            setPacketTemplates(packetTemplateData || []);
            setExportHistory(exportData || []);
        } finally {
            setSecondaryDataLoading(false);
        }
    };

    const scheduleBackgroundTrustRefresh = () => {
        if (backgroundTrustRefreshStartedRef.current || typeof window === 'undefined') {
            return;
        }

        backgroundTrustRefreshStartedRef.current = true;
        backgroundTrustRefreshTimerRef.current = window.setTimeout(() => {
            void Promise.all([
                loadOperationalAlerts(true),
                loadOpsInbox(true),
                loadCloseForecast(true)
            ]);
        }, 1200);
    };

    const loadData = async () => {
        setLoading(true);
        try {
            const [accountsData, ledgersData, txData, reconData, holdsData] = await Promise.all([
                trustApi.get('/api/trust/accounts').catch(() => []),
                trustApi.get('/api/trust/ledgers').catch(() => []),
                trustApi.get('/api/trust/transactions?limit=50').catch(() => []),
                trustApi.get('/api/trust/reconciliations').catch(() => []),
                trustApi.get('/api/legal-billing/trust-risk/holds?limit=50').catch(() => [])
            ]);
            setAccounts(accountsData || []);
            setLedgers(ledgersData || []);
            const normalizedTx = (txData || []).map((tx: any) => ({
                ...tx,
                status: tx.status || 'POSTED',
                isVoided: Boolean(tx.isVoided)
            }));
            setTransactions(normalizedTx);
            setReconciliations(reconData || []);
            setOpenHolds((holdsData || []).filter((hold: TrustRiskHold) =>
                ['placed', 'under_review', 'escalated'].includes((hold.status || '').toLowerCase())
            ));
            applyDefaultTrustAccount(accountsData || []);
        } catch (err: any) {
            console.error('Failed to load trust data:', err);
            // Only show error toast for unexpected errors
            if (err.message && !err.message.includes('401')) {
                toast.error('Failed to load trust data');
            }
        } finally {
            setLoading(false);
        }

        void Promise.all([
            loadOperationalAlerts(false),
            loadOpsInbox(false),
            loadCloseForecast(false)
        ]);

        void loadSecondaryTrustData();
        scheduleBackgroundTrustRefresh();
    };

    const loadOperationalAlertHistory = async (alertId: string) => {
        setSelectedOperationalAlertId(alertId);
        setOperationalAlertHistoryLoading(true);
        try {
            const history = await trustApi.get(`/api/trust/operational-alerts/${alertId}/history`).catch(() => []);
            setSelectedOperationalAlertHistory(history || []);
        } finally {
            setOperationalAlertHistoryLoading(false);
        }
    };

    const loadOpsInboxHistory = async (itemId: string) => {
        setSelectedOpsInboxId(itemId);
        setOpsInboxHistoryLoading(true);
        try {
            const history = await trustApi.get(`/api/trust/ops-inbox/${itemId}/history`).catch(() => []);
            setSelectedOpsInboxHistory(history || []);
        } finally {
            setOpsInboxHistoryLoading(false);
        }
    };

    const handleOperationalAlertAction = async (
        alert: TrustOperationalAlertDto,
        action: 'ack' | 'assign' | 'escalate' | 'resolve'
    ) => {
        if (!alert.alertId) {
            toast.error('Alert sync is still pending. Refresh trust alerts.');
            return;
        }

        setOperationalAlertBusyId(alert.alertId);
        try {
            if (action === 'assign') {
                if (!user?.id) {
                    throw new Error('Current user is not available.');
                }
                await trustApi.post(`/api/trust/operational-alerts/${alert.alertId}/assign`, {
                    assigneeUserId: user.id,
                    notes: 'Assigned from trust dashboard'
                });
                toast.success('Alert assigned to you');
            } else {
                const route = action === 'ack' ? 'ack' : action;
                await trustApi.post(`/api/trust/operational-alerts/${alert.alertId}/${route}`, {
                    notes: `Updated from trust dashboard: ${action}`
                });
                toast.success(
                    action === 'ack'
                        ? 'Alert acknowledged'
                        : action === 'escalate'
                            ? 'Alert escalated'
                            : 'Alert resolved'
                );
            }

            await loadOperationalAlerts(true);
            if (selectedOperationalAlertId === alert.alertId) {
                await loadOperationalAlertHistory(alert.alertId);
            }
        } catch (error: any) {
            toast.error(error?.message || 'Trust operational alert action failed');
        } finally {
            setOperationalAlertBusyId(null);
        }
    };

    const handleOpsInboxAction = async (
        item: TrustOpsInboxItemDto,
        action: 'claim' | 'assign' | 'defer' | 'escalate' | 'resolve'
    ) => {
        setOpsInboxBusyId(item.id);
        try {
            if (action === 'claim') {
                await trustApi.post(`/api/trust/ops-inbox/${item.id}/claim`, {
                    notes: 'Claimed from trust ops inbox'
                });
                toast.success('Inbox item claimed');
            } else if (action === 'assign') {
                if (!user?.id) {
                    throw new Error('Current user is not available.');
                }
                await trustApi.post(`/api/trust/ops-inbox/${item.id}/assign`, {
                    assigneeUserId: user.id,
                    notes: 'Assigned from trust ops inbox'
                });
                toast.success('Inbox item assigned to you');
            } else if (action === 'defer') {
                const deferredUntil = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
                await trustApi.post(`/api/trust/ops-inbox/${item.id}/defer`, {
                    deferredUntilUtc: deferredUntil,
                    notes: 'Deferred 24 hours from trust ops inbox'
                });
                toast.success('Inbox item deferred');
            } else {
                await trustApi.post(`/api/trust/ops-inbox/${item.id}/${action}`, {
                    notes: `Updated from trust ops inbox: ${action}`
                });
                toast.success(action === 'escalate' ? 'Inbox item escalated' : 'Inbox item resolved');
            }

            await loadOpsInbox(true);
            await loadOperationalAlerts(false);
            if (selectedOpsInboxId === item.id) {
                await loadOpsInboxHistory(item.id);
            }
        } catch (error: any) {
            toast.error(error?.message || 'Trust ops inbox action failed');
        } finally {
            setOpsInboxBusyId(null);
        }
    };

    const loadAuditLogs = async () => {
        if (!canViewAdminAuditLogs) {
            setAuditAccessDenied(true);
            setAuditLogs([]);
            setAuditLoading(false);
            return;
        }

        setAuditLoading(true);
        setAuditAccessDenied(false);
        try {
            const params: any = { limit: 100 };
            if (auditFilters.entityType !== 'all') params.entityType = auditFilters.entityType;
            if (auditFilters.query.trim()) params.q = auditFilters.query.trim();
            const result = await apiClient.admin.getAuditLogs(params);
            setAuditLogs(result?.items || result?.logs || []);
        } catch (err: any) {
            console.error('Failed to load audit logs:', err);
            const message = String(err?.message || '').toLowerCase();
            if (message.includes('403') || message.includes('forbidden')) {
                setAuditAccessDenied(true);
                setAuditLogs([]);
                return;
            }
            toast.error('Failed to load audit logs');
        } finally {
            setAuditLoading(false);
        }
    };

    useEffect(() => {
        if (activeTab !== 'audit') return;
        loadAuditLogs();
    }, [activeTab, auditFilters.entityType, auditFilters.query, canViewAdminAuditLogs]);

    useEffect(() => {
        if (activeTab === 'audit' && !canViewAdminAuditLogs) {
            setActiveTab('overview');
        }
    }, [activeTab, canViewAdminAuditLogs]);

    const accountMap = useMemo(() => {
        return new Map(accounts.map(a => [a.id, a]));
    }, [accounts]);

    const clientMap = useMemo(() => {
        return new Map(clients.map(client => [client.id, client]));
    }, [clients]);

    const matterMap = useMemo(() => {
        return new Map(matters.map(matter => [matter.id, matter]));
    }, [matters]);

    const maskAccountNumber = (value?: string | null) => {
        if (!value) return 'Pending setup';
        const digits = value.replace(/\s+/g, '');
        if (digits.length <= 4) return digits;
        return `â€¢â€¢â€¢â€¢ ${digits.slice(-4)}`;
    };

    const getAccountLabel = (accountId?: string | null) => {
        if (!accountId) return 'Unknown account';
        return accountMap.get(accountId)?.name || accountId;
    };

    const getClientLabel = (clientId?: string | null) => {
        if (!clientId) return 'Client';
        return clientMap.get(clientId)?.name || clientId;
    };

    const getMatterLabel = (matterId?: string | null) => {
        if (!matterId) return 'General Ledger';
        const matter = matterMap.get(matterId);
        if (!matter) return matterId;
        return matter.caseNumber ? `${matter.name} â€¢ ${matter.caseNumber}` : matter.name;
    };

    const getLedgerLabel = (ledger: ClientTrustLedger) => {
        return `${getClientLabel(ledger.clientId)} â€¢ ${getMatterLabel(ledger.matterId)}`;
    };

    const filteredAccounts = useMemo(() => {
        return accounts.filter(a => {
            if (entityFilter && a.entityId !== entityFilter) return false;
            if (officeFilter && a.officeId !== officeFilter) return false;
            return true;
        });
    }, [accounts, entityFilter, officeFilter]);

    const filteredLedgers = useMemo(() => {
        return ledgers.filter(l => {
            const account = accountMap.get(l.trustAccountId);
            const ledgerEntity = l.entityId || account?.entityId;
            const ledgerOffice = l.officeId || account?.officeId;
            if (entityFilter && ledgerEntity !== entityFilter) return false;
            if (officeFilter && ledgerOffice !== officeFilter) return false;
            return true;
        });
    }, [ledgers, accountMap, entityFilter, officeFilter]);

    const filteredTransactions = useMemo(() => {
        return transactions.filter(t => {
            const account = accountMap.get(t.trustAccountId);
            const txEntity = t.entityId || account?.entityId;
            const txOffice = t.officeId || account?.officeId;
            if (entityFilter && txEntity !== entityFilter) return false;
            if (officeFilter && txOffice !== officeFilter) return false;
            return true;
        });
    }, [transactions, accountMap, entityFilter, officeFilter]);

    const filteredReconciliations = useMemo(() => {
        return reconciliations.filter(r => {
            const account = accountMap.get(r.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [reconciliations, accountMap, entityFilter, officeFilter]);

    const filteredStatementImports = useMemo(() => {
        return statementImports.filter(statement => {
            const account = accountMap.get(statement.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [statementImports, accountMap, entityFilter, officeFilter]);

    const filteredEvidenceFiles = useMemo(() => {
        return evidenceFiles.filter(evidence => {
            const account = accountMap.get(evidence.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [evidenceFiles, accountMap, entityFilter, officeFilter]);

    const filteredParserRuns = useMemo(() => {
        return parserRuns.filter(run => {
            const account = accountMap.get(run.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [parserRuns, accountMap, entityFilter, officeFilter]);

    const filteredOutstandingItems = useMemo(() => {
        return outstandingItems.filter(item => {
            const account = accountMap.get(item.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [outstandingItems, accountMap, entityFilter, officeFilter]);

    const filteredPackets = useMemo(() => {
        return reconciliationPackets.filter(packet => {
            const account = accountMap.get(packet.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [reconciliationPackets, accountMap, entityFilter, officeFilter]);

    const filteredApprovalQueue = useMemo(() => {
        return approvalQueue.filter(item => {
            const account = accountMap.get(item.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [approvalQueue, accountMap, entityFilter, officeFilter]);

    const filteredMonthCloses = useMemo(() => {
        return monthCloses.filter(close => {
            const account = accountMap.get(close.trustAccountId);
            if (!account) return false;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [monthCloses, accountMap, entityFilter, officeFilter]);
    const filteredOperationalAlerts = useMemo(() => {
        return (operationalAlertSummary?.alerts || []).filter(alert => {
            if (!alert.trustAccountId) return true;
            const account = accountMap.get(alert.trustAccountId);
            if (!account) return true;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [operationalAlertSummary, accountMap, entityFilter, officeFilter]);

    const filteredOpsInboxItems = useMemo(() => {
        return (opsInboxSummary?.items || []).filter(item => {
            if (!item.trustAccountId) return true;
            const account = accountMap.get(item.trustAccountId);
            if (!account) return true;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [opsInboxSummary, accountMap, entityFilter, officeFilter]);
    const filteredCloseForecasts = useMemo(() => {
        return (closeForecastSummary?.snapshots || []).filter((snapshot: TrustCloseForecastSnapshotDto) => {
            const account = accountMap.get(snapshot.trustAccountId);
            if (!account) return true;
            if (entityFilter && account.entityId !== entityFilter) return false;
            if (officeFilter && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [closeForecastSummary, accountMap, entityFilter, officeFilter]);

    const availableDepositLedgers = useMemo(() => {
        return filteredLedgers.filter(ledger =>
            ledger.status === 'ACTIVE' &&
            (!depositForm.trustAccountId || ledger.trustAccountId === depositForm.trustAccountId)
        );
    }, [filteredLedgers, depositForm.trustAccountId]);

    const availableWithdrawalLedgers = useMemo(() => {
        return filteredLedgers.filter(ledger =>
            ledger.status === 'ACTIVE' &&
            Number(ledger.availableToDisburse ?? ledger.runningBalance) > 0 &&
            (!withdrawalForm.trustAccountId || ledger.trustAccountId === withdrawalForm.trustAccountId)
        );
    }, [filteredLedgers, withdrawalForm.trustAccountId]);

    const availableMattersForLedger = useMemo(() => {
        return matters.filter(matter => {
            if (!ledgerForm.clientId) return true;
            return matter.clientId === ledgerForm.clientId;
        });
    }, [matters, ledgerForm.clientId]);

    useEffect(() => {
        if (filteredAccounts.length === 0) {
            setSelectedAccount('');
            return;
        }
        if (!filteredAccounts.some(a => a.id === selectedAccount)) {
            const nextAccount = filteredAccounts[0];
            setSelectedAccount(nextAccount.id);
            setDepositForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setWithdrawalForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setReconcileForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setLedgerForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setStatementImportForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setOutstandingItemForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setPacketForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setProjectionRecoveryForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setPacketRecoveryForm(f => ({ ...f, trustAccountId: nextAccount.id }));
            setBundleForm(f => ({ ...f, trustAccountId: nextAccount.id }));
        }
    }, [filteredAccounts, selectedAccount]);

    // Calculate totals
    const totalTrustBalance = filteredAccounts.reduce((sum, a) => sum + Number(a.currentBalance), 0);
    const totalClientLedgers = filteredLedgers.reduce((sum, l) => sum + Number(l.runningBalance), 0);
    const totalAvailableToDisburse = filteredLedgers.reduce((sum, l) => sum + Number(l.availableToDisburse ?? 0), 0);
    const totalUnclearedFunds = filteredAccounts.reduce((sum, a) => sum + Number(a.unclearedBalance ?? 0), 0);
    const pendingTransactions = filteredTransactions.filter(t => t.status === 'PENDING' && !t.isVoided).length;
    const approvalQueueCount = filteredApprovalQueue.length;
    const pendingClearanceCount = filteredTransactions.filter(t => t.clearingStatus === 'pending_clearance' && t.status === 'APPROVED' && !t.isVoided).length;
    const unreconciledAccounts = filteredAccounts.filter(a => {
        const lastPacket = reconciliationPackets.find(r => r.trustAccountId === a.id) || filteredReconciliations.find(r => r.trustAccountId === a.id);
        if (!lastPacket) return true;
        const lastReconDate = new Date(lastPacket.periodEnd);
        const monthAgo = new Date();
        monthAgo.setMonth(monthAgo.getMonth() - 1);
        return lastReconDate < monthAgo;
    }).length;
    const openOutstandingItems = filteredOutstandingItems.filter(item => item.status === 'open');
    const unresolvedPackets = filteredPackets.filter(packet => packet.status !== 'signed_off').length;
    const openMonthCloseCount = filteredMonthCloses.filter(close => !['closed', 'signed_off'].includes((close.status || '').toLowerCase())).length;
    const operationalCriticalCount = filteredOperationalAlerts.filter(alert => alert.severity === 'critical').length;
    const operationalWarningCount = filteredOperationalAlerts.filter(alert => alert.severity === 'warning').length;
    const opsInboxBreachedCount = filteredOpsInboxItems.filter(item => item.isSlaBreached).length;
    const visibleOpenHolds = useMemo(() => {
        const visibleIds = new Set([
            ...filteredAccounts.map(account => account.id),
            ...filteredLedgers.map(ledger => ledger.id),
            ...filteredTransactions.map(transaction => transaction.id),
            ...filteredPackets.map(packet => packet.id)
        ]);

        return [...openHolds]
            .filter(hold => !hold.targetId || visibleIds.has(hold.targetId))
            .sort((a, b) => new Date(b.placedAt || b.createdAt).getTime() - new Date(a.placedAt || a.createdAt).getTime());
    }, [openHolds, filteredAccounts, filteredLedgers, filteredTransactions, filteredPackets]);
    const pendingClearanceTransactions = useMemo(() => {
        return [...filteredTransactions]
            .filter(transaction =>
                transaction.type === 'DEPOSIT' &&
                transaction.status === 'APPROVED' &&
                !transaction.isVoided &&
                transaction.clearingStatus === 'pending_clearance'
            )
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    }, [filteredTransactions]);
    const recentTransactions = useMemo(() => {
        return [...filteredTransactions]
            .filter(transaction => !transaction.isVoided)
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
            .slice(0, 6);
    }, [filteredTransactions]);
    const latestPackets = useMemo(() => {
        return [...filteredPackets]
            .sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime())
            .slice(0, 6);
    }, [filteredPackets]);
    const latestApprovalQueue = useMemo(() => {
        return [...filteredApprovalQueue]
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
            .slice(0, 6);
    }, [filteredApprovalQueue]);
    const latestMonthCloses = useMemo(() => {
        return [...filteredMonthCloses]
            .sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime())
            .slice(0, 6);
    }, [filteredMonthCloses]);
    const latestStatementImports = useMemo(() => {
        return [...filteredStatementImports]
            .sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime())
            .slice(0, 6);
    }, [filteredStatementImports]);
    const latestOutstandingItems = useMemo(() => {
        return [...filteredOutstandingItems]
            .sort((a, b) => new Date(b.occurredAt || b.createdAt).getTime() - new Date(a.occurredAt || a.createdAt).getTime())
            .slice(0, 8);
    }, [filteredOutstandingItems]);
    const latestOperationalAlerts = useMemo(() => {
        return [...filteredOperationalAlerts]
            .sort((a, b) => {
                const severityA = a.severity === 'critical' ? 2 : a.severity === 'warning' ? 1 : 0;
                const severityB = b.severity === 'critical' ? 2 : b.severity === 'warning' ? 1 : 0;
                if (severityA !== severityB) return severityB - severityA;
                return new Date(b.openedAt).getTime() - new Date(a.openedAt).getTime();
            })
            .slice(0, 6);
    }, [filteredOperationalAlerts]);
    const latestOpsInboxItems = useMemo(() => {
        return [...filteredOpsInboxItems]
            .sort((a, b) => {
                const breachedA = a.isSlaBreached ? 1 : 0;
                const breachedB = b.isSlaBreached ? 1 : 0;
                if (breachedA !== breachedB) return breachedB - breachedA;
                const severityA = a.severity === 'critical' ? 2 : a.severity === 'warning' ? 1 : 0;
                const severityB = b.severity === 'critical' ? 2 : b.severity === 'warning' ? 1 : 0;
                if (severityA !== severityB) return severityB - severityA;
                return new Date(a.dueAt || a.openedAt).getTime() - new Date(b.dueAt || b.openedAt).getTime();
            })
            .slice(0, 8);
    }, [filteredOpsInboxItems]);
    const latestPacketByAccount = useMemo(() => {
        const map = new Map<string, TrustReconciliationPacket>();
        filteredPackets.forEach(packet => {
            const current = map.get(packet.trustAccountId);
            if (!current || new Date(packet.periodEnd).getTime() > new Date(current.periodEnd).getTime()) {
                map.set(packet.trustAccountId, packet);
            }
        });
        return map;
    }, [filteredPackets]);
    const canonicalPackets = useMemo(() => {
        return filteredPackets.filter(packet => packet.isCanonical !== false);
    }, [filteredPackets]);
    const latestLegacyReconciliationByAccount = useMemo(() => {
        const map = new Map<string, ReconciliationRecord>();
        filteredReconciliations.forEach(record => {
            const current = map.get(record.trustAccountId);
            if (!current || new Date(record.periodEnd).getTime() > new Date(current.periodEnd).getTime()) {
                map.set(record.trustAccountId, record);
            }
        });
        return map;
    }, [filteredReconciliations]);
    const monthCloseByPacketId = useMemo(() => {
        const map = new Map<string, TrustMonthCloseDto>();
        filteredMonthCloses.forEach(close => {
            if (!close.reconciliationPacketId) return;
            const current = map.get(close.reconciliationPacketId);
            if (!current || new Date(close.preparedAt).getTime() > new Date(current.preparedAt).getTime()) {
                map.set(close.reconciliationPacketId, close);
            }
        });
        return map;
    }, [filteredMonthCloses]);
    const approvalQueueByTransactionId = useMemo(() => {
        const map = new Map<string, TrustApprovalQueueItemDto>();
        filteredApprovalQueue.forEach(item => {
            map.set(item.trustTransactionId, item);
        });
        return map;
    }, [filteredApprovalQueue]);
    const selectedPacket = useMemo(() => {
        return filteredPackets.find(packet => packet.id === selectedPacketId) || null;
    }, [filteredPackets, selectedPacketId]);
    const selectedStatementImport = useMemo(() => {
        return filteredStatementImports.find(statement => statement.id === selectedStatementImportId) || null;
    }, [filteredStatementImports, selectedStatementImportId]);
    const selectedStatementEvidence = useMemo(() => {
        if (packetDetail?.statementImport && packetDetail.statementImport.id === selectedStatementImportId) {
            return packetDetail.statementImport;
        }

        return selectedStatementImport;
    }, [packetDetail, selectedStatementImport, selectedStatementImportId]);
    const selectedEvidenceFile = useMemo(() => {
        if (!selectedStatementEvidence) return null;
        return filteredEvidenceFiles.find(file =>
            file.canonicalStatementImportId === selectedStatementEvidence.id ||
            (!!selectedStatementEvidence.sourceEvidenceKey && file.evidenceKey === selectedStatementEvidence.sourceEvidenceKey)
        ) || null;
    }, [filteredEvidenceFiles, selectedStatementEvidence]);
    const selectedParserRun = useMemo(() => {
        if (!selectedStatementEvidence) return null;
        return filteredParserRuns.find(run => run.trustStatementImportId === selectedStatementEvidence.id) || null;
    }, [filteredParserRuns, selectedStatementEvidence]);
    const reconciliationFocusAccountId = selectedPacket?.trustAccountId || selectedStatementImport?.trustAccountId || selectedAccount || '';
    const reconciliationFocusAccount = reconciliationFocusAccountId ? accountMap.get(reconciliationFocusAccountId) || null : null;
    const activePacketTemplate = useMemo(() => {
        const close = selectedPacketId ? monthCloses.find(item => item.reconciliationPacketId === selectedPacketId) : null;
        if (close?.packetTemplateKey) {
            return packetTemplates.find(template =>
                template.templateKey === close.packetTemplateKey &&
                (!close.packetTemplateVersionNumber || template.versionNumber === close.packetTemplateVersionNumber)
            ) || null;
        }

        if (!reconciliationFocusAccount) return null;
        return packetTemplates.find(template => template.isActive && template.jurisdiction === (reconciliationFocusAccount.jurisdiction || 'DEFAULT')) || null;
    }, [monthCloses, packetTemplates, selectedPacketId, reconciliationFocusAccount]);
    useEffect(() => {
        if (!reconciliationFocusAccount && !activePacketTemplate) {
            return;
        }

        setPacketTemplateForm(prev => {
            const nextJurisdiction = activePacketTemplate?.jurisdiction || reconciliationFocusAccount?.jurisdiction || prev.jurisdiction;
            const nextPolicyKey = activePacketTemplate?.policyKey || prev.policyKey;
            const nextAccountType = activePacketTemplate?.accountType || prev.accountType;
            if (
                prev.jurisdiction === nextJurisdiction &&
                prev.policyKey === nextPolicyKey &&
                prev.accountType === nextAccountType
            ) {
                return prev;
            }

            return {
                ...prev,
                jurisdiction: nextJurisdiction,
                policyKey: nextPolicyKey,
                accountType: nextAccountType
            };
        });
    }, [activePacketTemplate, reconciliationFocusAccount]);
    const reconciliationOperationalAlerts = useMemo(() => {
        return filteredOperationalAlerts
            .filter(alert => !reconciliationFocusAccountId || alert.trustAccountId === reconciliationFocusAccountId)
            .sort((a, b) => {
                const severityA = a.severity === 'critical' ? 2 : a.severity === 'warning' ? 1 : 0;
                const severityB = b.severity === 'critical' ? 2 : b.severity === 'warning' ? 1 : 0;
                if (severityA !== severityB) return severityB - severityA;
                return new Date(b.openedAt).getTime() - new Date(a.openedAt).getTime();
            })
            .slice(0, 4);
    }, [filteredOperationalAlerts, reconciliationFocusAccountId]);
    const reconciliationPendingClearanceCount = useMemo(() => {
        if (!reconciliationFocusAccountId) return 0;
        return pendingClearanceTransactions.filter(tx => tx.trustAccountId === reconciliationFocusAccountId).length;
    }, [pendingClearanceTransactions, reconciliationFocusAccountId]);
    const activeStatementLines = useMemo(() => {
        if (
            packetDetail &&
            packetDetail.packet.statementImportId &&
            packetDetail.packet.statementImportId === selectedStatementImportId &&
            packetDetail.statementLines.length > 0
        ) {
            return packetDetail.statementLines;
        }
        return statementLines;
    }, [packetDetail, selectedStatementImportId, statementLines]);
    useEffect(() => {
        if (!selectedPacket) {
            return;
        }

        const relatedClose = monthCloseByPacketId.get(selectedPacket.id);
        setPacketRecoveryForm(form => ({
            ...form,
            trustAccountId: selectedPacket.trustAccountId,
            trustReconciliationPacketId: selectedPacket.id,
            trustMonthCloseId: relatedClose?.id || '',
            periodStart: selectedPacket.periodStart?.split('T')[0] || form.periodStart,
            periodEnd: selectedPacket.periodEnd?.split('T')[0] || form.periodEnd,
            statementImportId: selectedPacket.statementImportId || '',
            statementEndingBalance: selectedPacket.statementEndingBalance != null ? String(selectedPacket.statementEndingBalance) : '',
            notes: form.notes || selectedPacket.notes || ''
        }));
        setBundleForm(form => ({
            ...form,
            trustAccountId: selectedPacket.trustAccountId,
            trustReconciliationPacketId: selectedPacket.id,
            trustMonthCloseId: relatedClose?.id || form.trustMonthCloseId,
            periodStart: selectedPacket.periodStart?.split('T')[0] || form.periodStart,
            periodEnd: selectedPacket.periodEnd?.split('T')[0] || form.periodEnd
        }));
    }, [selectedPacket, monthCloseByPacketId]);
    const activePacketOutstandingItems = useMemo(() => {
        if (packetDetail?.packet?.id === selectedPacketId) {
            return packetDetail.outstandingItems;
        }
        return filteredOutstandingItems.filter(item => item.trustReconciliationPacketId === selectedPacketId);
    }, [packetDetail, selectedPacketId, filteredOutstandingItems]);
    const selectedPacketSignoffs = packetDetail?.packet?.id === selectedPacketId ? packetDetail.signoffs : [];
    const statementMatchingCandidates = useMemo(() => {
        if (!selectedStatementImport) return [] as TrustTransactionV2[];
        return filteredTransactions
            .filter(tx =>
                tx.trustAccountId === selectedStatementImport.trustAccountId &&
                !tx.isVoided &&
                ['APPROVED', 'POSTED'].includes((tx.status || '').toUpperCase())
            )
            .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    }, [filteredTransactions, selectedStatementImport]);
    const filteredExportHistory = useMemo(() => {
        return exportHistory.filter(item => {
            const account = item.trustAccountId ? accountMap.get(item.trustAccountId) : null;
            if (entityFilter && account && account.entityId !== entityFilter) return false;
            if (officeFilter && account && account.officeId !== officeFilter) return false;
            return true;
        });
    }, [exportHistory, accountMap, entityFilter, officeFilter]);

    useEffect(() => {
        if (canonicalPackets.length === 0) {
            setSelectedPacketId('');
            setPacketDetail(null);
            return;
        }

        if (!selectedPacketId || !canonicalPackets.some(packet => packet.id === selectedPacketId)) {
            setSelectedPacketId(canonicalPackets[0].id);
        }
    }, [canonicalPackets, selectedPacketId]);

    useEffect(() => {
        if (filteredStatementImports.length === 0) {
            setSelectedStatementImportId('');
            setStatementLines([]);
            return;
        }

        if (!selectedStatementImportId || !filteredStatementImports.some(statement => statement.id === selectedStatementImportId)) {
            setSelectedStatementImportId(filteredStatementImports[0].id);
        }
    }, [filteredStatementImports, selectedStatementImportId]);

    useEffect(() => {
        setLineMatchSelections({});
    }, [selectedStatementImportId]);

    const saveBlob = (blob: Blob, filename: string) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        anchor.click();
        URL.revokeObjectURL(url);
    };

    const parseJsonSafely = <T,>(value?: string): T | null => {
        if (!value) return null;
        try {
            return JSON.parse(value) as T;
        } catch {
            return null;
        }
    };

    const loadPacketDetail = async (packetId: string, silent = false) => {
        if (!packetId) {
            setPacketDetail(null);
            return;
        }

        if (!silent) {
            setPacketDetailLoading(true);
        }

        try {
            const result = await trustApi.get(`/api/trust/reconciliation-packets/${packetId}`) as TrustReconciliationPacketDetail;
            setPacketDetail(result);
            if (result.packet.statementImportId) {
                setSelectedStatementImportId(result.packet.statementImportId);
                setStatementLines(result.statementLines || []);
            }
        } catch (err: any) {
            if (!silent) {
                toast.error(err.message || 'Failed to load reconciliation packet detail');
            }
        } finally {
            if (!silent) {
                setPacketDetailLoading(false);
            }
        }
    };

    const loadStatementLineWorkspace = async (statementImportId: string, silent = false) => {
        if (!statementImportId) {
            setStatementLines([]);
            return;
        }

        if (
            packetDetail &&
            packetDetail.packet.statementImportId === statementImportId &&
            packetDetail.statementLines.length > 0
        ) {
            setStatementLines(packetDetail.statementLines);
            return;
        }

        if (!silent) {
            setStatementLinesLoading(true);
        }

        try {
            const result = await trustApi.get(`/api/trust/statements/${statementImportId}/lines`) as TrustStatementLine[];
            setStatementLines(result || []);
        } catch (err: any) {
            if (!silent) {
                toast.error(err.message || 'Failed to load statement lines');
            }
        } finally {
            if (!silent) {
                setStatementLinesLoading(false);
            }
        }
    };

    useEffect(() => {
        if (activeTab !== 'reconciliation' || !selectedPacketId) return;
        loadPacketDetail(selectedPacketId, true);
    }, [activeTab, selectedPacketId]);

    useEffect(() => {
        if (activeTab !== 'reconciliation' || !selectedStatementImportId) return;
        loadStatementLineWorkspace(selectedStatementImportId, true);
    }, [activeTab, selectedStatementImportId]);

    const escapeCsvCell = (value: unknown) => {
        if (value === null || value === undefined) return '';
        const normalized = String(value).replace(/"/g, '""');
        return /[",\n]/.test(normalized) ? `"${normalized}"` : normalized;
    };

    const buildCsv = (rows: Record<string, unknown>[]) => {
        if (rows.length === 0) return '';
        const headers = Array.from(rows.reduce((set, row) => {
            Object.keys(row).forEach(key => set.add(key));
            return set;
        }, new Set<string>()));
        const lines = [
            headers.join(','),
            ...rows.map(row => headers.map(header => escapeCsvCell(row[header])).join(','))
        ];
        return lines.join('\n');
    };

    const createTrustExportPdf = (fileName: string, payload: any) => {
        const pdf = new jsPDF('p', 'pt', 'a4');
        const pageWidth = pdf.internal.pageSize.getWidth();
        const pageHeight = pdf.internal.pageSize.getHeight();
        const margin = 42;
        let y = 44;

        const ensureSpace = (required = 24) => {
            if (y + required < pageHeight - margin) return;
            pdf.addPage();
            y = 44;
        };

        const writeLine = (label: string, value?: string | number | null, accent = false) => {
            if (value === undefined || value === null || value === '') return;
            ensureSpace();
            pdf.setFont('helvetica', 'bold');
            pdf.setFontSize(10);
            pdf.setTextColor(55, 65, 81);
            pdf.text(label, margin, y);
            pdf.setFont('helvetica', accent ? 'bold' : 'normal');
            pdf.setFontSize(accent ? 11 : 10);
            pdf.setTextColor(accent ? 15 : 31, accent ? 23 : 41, accent ? 42 : 55);
            pdf.text(String(value), margin + 148, y);
            y += 16;
        };

        const writeSectionTitle = (title: string) => {
            ensureSpace(24);
            pdf.setFont('helvetica', 'bold');
            pdf.setFontSize(12);
            pdf.setTextColor(15, 23, 42);
            pdf.text(title, margin, y);
            y += 18;
        };

        const writeBullet = (text: string) => {
            ensureSpace();
            pdf.setFont('helvetica', 'normal');
            pdf.setFontSize(10);
            pdf.setTextColor(31, 41, 55);
            pdf.text(`- ${text}`, margin + 6, y);
            y += 16;
        };

        pdf.setFillColor(15, 23, 42);
        pdf.rect(0, 0, pageWidth, 86, 'F');
        pdf.setTextColor(255, 255, 255);
        pdf.setFont('helvetica', 'bold');
        pdf.setFontSize(22);
        pdf.text('JurisFlow', margin, 36);
        pdf.setFontSize(16);
        pdf.text('Trust Compliance Export', margin, 60);
        pdf.setFont('helvetica', 'normal');
        pdf.setFontSize(10);
        pdf.text(fileName, pageWidth - margin, 36, { align: 'right' });
        pdf.text(new Date().toLocaleString('en-US'), pageWidth - margin, 56, { align: 'right' });

        y = 114;
        writeLine('Export', payload?.metadata?.exportLabel || 'Trust export', true);
        writeLine('Firm', payload?.metadata?.firm?.firmName || 'JurisFlow');
        writeLine('Entity', payload?.metadata?.entity?.legalName || payload?.metadata?.entity?.name || 'Firm-wide');
        writeLine('Office', payload?.metadata?.office?.name || 'All offices');
        writeLine('Responsible Lawyer', payload?.metadata?.responsibleLawyer?.name || 'Not assigned');
        writeLine('Trust Account', payload?.account?.name || 'N/A');
        writeLine('Period Start', payload?.packet?.periodStart || payload?.period?.periodStart || 'N/A');
        writeLine('Period End', payload?.packet?.periodEnd || payload?.period?.periodEnd || 'N/A');
        writeLine('Packet Status', payload?.packet?.status || payload?.monthClose?.status || 'N/A');

        y += 8;
        ensureSpace(96);
        pdf.setDrawColor(226, 232, 240);
        pdf.roundedRect(margin, y, pageWidth - (margin * 2), 98, 12, 12);
        pdf.setFont('helvetica', 'bold');
        pdf.setFontSize(12);
        pdf.setTextColor(15, 23, 42);
        pdf.text('Packet Snapshot', margin + 18, y + 24);
        pdf.setFont('helvetica', 'normal');
        pdf.setFontSize(10);
        pdf.text(`Statement ending balance: ${formatCurrency(Number(payload?.packet?.statementEndingBalance || 0))}`, margin + 18, y + 46);
        pdf.text(`Adjusted bank balance: ${formatCurrency(Number(payload?.packet?.adjustedBankBalance || 0))}`, margin + 18, y + 64);
        pdf.text(`Journal balance: ${formatCurrency(Number(payload?.packet?.journalBalance || 0))}`, margin + 280, y + 46);
        pdf.text(`Client ledger balance: ${formatCurrency(Number(payload?.packet?.clientLedgerBalance || 0))}`, margin + 280, y + 64);
        pdf.text(`Outstanding deposits: ${formatCurrency(Number(payload?.packet?.outstandingDepositsTotal || 0))}`, margin + 18, y + 82);
        pdf.text(`Outstanding checks: ${formatCurrency(Number(payload?.packet?.outstandingChecksTotal || 0))}`, margin + 280, y + 82);
        y += 126;

        if (payload?.statementSummary) {
            writeSectionTitle('Statement Summary');
            writeBullet(`Source: ${payload.statementSummary.source || 'manual'}`);
            writeBullet(`Ending balance: ${formatCurrency(Number(payload.statementSummary.statementEndingBalance || 0))}`);
            writeBullet(`Rows: ${payload.statementSummary.lineCount || 0}`);
            writeBullet(`Matched / Unmatched / Ignored: ${payload.statementSummary.matchedLineCount || 0} / ${payload.statementSummary.unmatchedLineCount || 0} / ${payload.statementSummary.ignoredLineCount || 0}`);
            y += 6;
        }

        const stepLines = Array.isArray(payload?.steps) ? payload.steps : [];
        if (stepLines.length > 0) {
            writeSectionTitle('Month-Close Checklist');
            stepLines.forEach((step: any) => {
                writeBullet(`${String(step.stepKey || '').replace(/_/g, ' ')}: ${String(step.status || '').replace(/_/g, ' ')}${step.notes ? ` (${step.notes})` : ''}`);
            });
            y += 8;
        }

        const signoffLines = Array.isArray(payload?.signoffChain?.packetSignoffs)
            ? payload.signoffChain.packetSignoffs
            : Array.isArray(payload?.packetSignoffs)
                ? payload.packetSignoffs
                : [];
        if (signoffLines.length > 0) {
            writeSectionTitle('Sign-Off Chain');
            if (payload?.signoffChain?.reviewer?.reviewerSignedBy || payload?.signoffChain?.reviewer?.reviewerSignedAt) {
                writeBullet(`Reviewer: ${payload.signoffChain.reviewer.reviewerSignedBy || 'pending'} / ${payload.signoffChain.reviewer.reviewerSignedAt || 'not signed'}`);
            }
            if (payload?.signoffChain?.responsibleLawyer?.responsibleLawyerSignedBy || payload?.signoffChain?.responsibleLawyer?.responsibleLawyerSignedAt) {
                writeBullet(`Responsible lawyer: ${payload.signoffChain.responsibleLawyer.responsibleLawyerSignedBy || 'pending'} / ${payload.signoffChain.responsibleLawyer.responsibleLawyerSignedAt || 'not signed'}`);
            }
            signoffLines.forEach((signoff: any) => {
                writeBullet(`${signoff.signerRole || 'reviewer'} / ${signoff.signedBy || 'unknown'} / ${signoff.status || 'recorded'}`);
            });
            y += 8;
        }

        const scheduleSections = [
            { title: 'Outstanding Checks', rows: Array.isArray(payload?.outstandingChecks) ? payload.outstandingChecks : [] },
            { title: 'Deposits In Transit', rows: Array.isArray(payload?.depositsInTransit) ? payload.depositsInTransit : [] },
            { title: 'Manual Adjustments', rows: Array.isArray(payload?.manualAdjustments) ? payload.manualAdjustments : [] }
        ];
        scheduleSections.forEach(section => {
            if (section.rows.length === 0) return;
            writeSectionTitle(section.title);
            section.rows.slice(0, 14).forEach((item: any) => {
                const amount = Number(item.amount || 0);
                const label = item.itemType
                    ? `${String(item.itemType).replace(/_/g, ' ')} / `
                    : '';
                writeBullet(`${label}${item.reference || 'no ref'} / ${formatCurrency(amount)} / ${item.status || 'open'}`);
            });
            y += 8;
        });

        const itemLines = Array.isArray(payload?.outstandingItems) ? payload.outstandingItems : [];
        if (itemLines.length > 0) {
            writeSectionTitle('Exception Summary');
            writeBullet(`Total outstanding items: ${itemLines.length}`);
            writeBullet(`Packet exceptions: ${payload?.exceptionSummary?.exceptionCount ?? payload?.packet?.exceptionCount ?? itemLines.length}`);
            writeBullet(`Open exceptions: ${payload?.exceptionSummary?.openCount ?? itemLines.length}`);
        }

        pdf.save(fileName);
    };

    const downloadExportRecord = async (exportItem: TrustComplianceExportListItemDto | TrustComplianceExportDto) => {
        try {
            const exportDetail = 'payloadJson' in exportItem && exportItem.payloadJson
                ? exportItem as TrustComplianceExportDto
                : await trustApi.get(`/api/trust/exports/${exportItem.id}`) as TrustComplianceExportDto;
            const payload = parseJsonSafely<any>(exportDetail.payloadJson);

            if (exportDetail.format === 'json') {
                saveBlob(new Blob([exportDetail.payloadJson || '{}'], { type: 'application/json' }), exportDetail.fileName);
                return;
            }

            if (exportDetail.format === 'csv') {
                const csvRows = Array.isArray(payload?.csvRows) ? payload.csvRows : [];
                const csv = buildCsv(csvRows);
                saveBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), exportDetail.fileName);
                return;
            }

            if (exportDetail.format === 'pdf') {
                createTrustExportPdf(exportDetail.fileName, payload);
                return;
            }

            throw new Error('Unsupported export format');
        } catch (err: any) {
            toast.error(err.message || 'Export download failed');
        }
    };

    const handleGenerateExport = async (request: TrustComplianceExportRequest) => {
        try {
            const result = await trustApi.post('/api/trust/exports', request) as TrustComplianceExportDto;
            setExportHistory(prev => [result, ...prev].slice(0, 50));
            await downloadExportRecord(result);
            toast.success('Trust export generated');
        } catch (err: any) {
            toast.error(err.message || 'Trust export failed');
        }
    };

    const handleProjectionRecovery = async (commitProjectionRepair: boolean) => {
        if (!projectionRecoveryForm.trustAccountId) {
            toast.error('Select a trust account for recovery.');
            return;
        }

        setProjectionRecoveryBusy(true);
        try {
            const payload = {
                trustAccountId: projectionRecoveryForm.trustAccountId,
                asOfUtc: commitProjectionRepair
                    ? undefined
                    : (projectionRecoveryForm.asOfUtc ? new Date(`${projectionRecoveryForm.asOfUtc}T23:59:59Z`).toISOString() : undefined),
                commitProjectionRepair,
                onlyIfDrifted: projectionRecoveryForm.onlyIfDrifted
            };
            const result = await trustApi.post('/api/trust/recovery/as-of-rebuild', payload) as TrustAsOfProjectionRecoveryResult;
            setProjectionRecoveryResult(result);
            toast.success(commitProjectionRepair ? 'Current trust projections repaired' : 'As-of trust snapshot generated');
            await loadData();
        } catch (err: any) {
            toast.error(err.message || 'Trust recovery failed');
        } finally {
            setProjectionRecoveryBusy(false);
        }
    };

    const handlePacketRegeneration = async () => {
        const trustAccountId = packetRecoveryForm.trustAccountId || selectedAccount;
        if (!trustAccountId) {
            toast.error('Select a trust account before regenerating a packet.');
            return;
        }
        if (!packetRecoveryForm.reason.trim()) {
            toast.error('Packet regeneration reason is required.');
            return;
        }

        setPacketRegenerationBusy(true);
        try {
            const result = await trustApi.post('/api/trust/recovery/packet-regeneration', {
                trustAccountId,
                trustReconciliationPacketId: packetRecoveryForm.trustReconciliationPacketId || undefined,
                trustMonthCloseId: packetRecoveryForm.trustMonthCloseId || undefined,
                periodStart: packetRecoveryForm.periodStart ? new Date(`${packetRecoveryForm.periodStart}T00:00:00Z`).toISOString() : undefined,
                periodEnd: packetRecoveryForm.periodEnd ? new Date(`${packetRecoveryForm.periodEnd}T00:00:00Z`).toISOString() : undefined,
                statementImportId: packetRecoveryForm.statementImportId || undefined,
                statementEndingBalance: packetRecoveryForm.statementEndingBalance ? Number(packetRecoveryForm.statementEndingBalance) : undefined,
                reason: packetRecoveryForm.reason,
                notes: packetRecoveryForm.notes || undefined,
                autoPrepareMonthClose: packetRecoveryForm.autoPrepareMonthClose
            }) as TrustPacketRegenerationResult;
            setPacketRegenerationResult(result);
            toast.success('Canonical packet regenerated');
            await loadData();
        } catch (err: any) {
            toast.error(err.message || 'Packet regeneration failed');
        } finally {
            setPacketRegenerationBusy(false);
        }
    };

    const handleComplianceBundle = async () => {
        const trustAccountId = bundleForm.trustAccountId || selectedAccount;
        if (!trustAccountId) {
            toast.error('Select a trust account before generating a bundle.');
            return;
        }

        setComplianceBundleBusy(true);
        try {
            const result = await trustApi.post('/api/trust/recovery/compliance-bundle', {
                trustAccountId,
                trustMonthCloseId: bundleForm.trustMonthCloseId || undefined,
                trustReconciliationPacketId: bundleForm.trustReconciliationPacketId || undefined,
                periodStart: bundleForm.periodStart ? new Date(`${bundleForm.periodStart}T00:00:00Z`).toISOString() : undefined,
                periodEnd: bundleForm.periodEnd ? new Date(`${bundleForm.periodEnd}T00:00:00Z`).toISOString() : undefined,
                includeJsonPacket: bundleForm.includeJsonPacket,
                includeAccountJournalCsv: bundleForm.includeAccountJournalCsv,
                includeApprovalRegisterCsv: bundleForm.includeApprovalRegisterCsv,
                includeClientLedgerCards: bundleForm.includeClientLedgerCards,
                notes: bundleForm.notes || undefined
            }) as TrustComplianceBundleResult;
            setComplianceBundleResult(result);
            setBundleIntegrity(result.integrity || null);
            setExportHistory(prev => [...result.exports, ...prev].slice(0, 50));
            const manifest = await trustApi.get(`/api/trust/exports/${result.manifestExportId}`) as TrustComplianceExportDto;
            setExportHistory(prev => [manifest, ...prev.filter(item => item.id !== manifest.id)].slice(0, 50));
            await downloadExportRecord(manifest);
            toast.success(`Compliance bundle generated (${result.exportCount} artifact${result.exportCount === 1 ? '' : 's'})`);
            await loadData();
        } catch (err: any) {
            toast.error(err.message || 'Compliance bundle generation failed');
        } finally {
            setComplianceBundleBusy(false);
        }
    };

    const refreshBundleIntegrity = async (manifestExportId?: string) => {
        const targetManifestId = manifestExportId || complianceBundleResult?.manifestExportId;
        if (!targetManifestId) {
            toast.error('Generate a compliance bundle first.');
            return;
        }

        setBundleIntegrityBusy(true);
        try {
            const result = await trustApi.get(`/api/trust/recovery/compliance-bundle/${targetManifestId}/integrity`) as TrustBundleIntegrityDto;
            setBundleIntegrity(result);
        } catch (err: any) {
            toast.error(err.message || 'Failed to refresh bundle integrity');
        } finally {
            setBundleIntegrityBusy(false);
        }
    };

    const applyCloseForecastScope = (snapshot: TrustCloseForecastSnapshotDto) => {
        setBundleForm(prev => ({
            ...prev,
            trustAccountId: snapshot.trustAccountId,
            trustMonthCloseId: snapshot.canonicalMonthCloseId || '',
            trustReconciliationPacketId: snapshot.canonicalPacketId || '',
            periodStart: snapshot.periodStart?.split('T')[0] || prev.periodStart,
            periodEnd: snapshot.periodEnd?.split('T')[0] || prev.periodEnd
        }));
        setPacketRecoveryForm(prev => ({
            ...prev,
            trustAccountId: snapshot.trustAccountId,
            trustReconciliationPacketId: snapshot.canonicalPacketId || '',
            trustMonthCloseId: snapshot.canonicalMonthCloseId || '',
            periodStart: snapshot.periodStart?.split('T')[0] || prev.periodStart,
            periodEnd: snapshot.periodEnd?.split('T')[0] || prev.periodEnd
        }));
    };

    const handleBundleSign = async () => {
        const manifestExportId = complianceBundleResult?.manifestExportId;
        if (!manifestExportId) {
            toast.error('Generate a compliance bundle first.');
            return;
        }

        setBundleIntegrityBusy(true);
        try {
            const result = await trustApi.post(`/api/trust/recovery/compliance-bundle/${manifestExportId}/sign`, {
                retentionPolicyTag: bundleSignForm.retentionPolicyTag || undefined,
                redactionProfile: bundleSignForm.redactionProfile || undefined,
                notes: bundleSignForm.notes || undefined
            }) as TrustBundleIntegrityDto;
            setBundleIntegrity(result);
            setComplianceBundleResult(prev => prev ? { ...prev, integrity: result } : prev);
            toast.success('Compliance bundle signed');
            await loadData();
        } catch (err: any) {
            toast.error(err.message || 'Failed to sign compliance bundle');
        } finally {
            setBundleIntegrityBusy(false);
        }
    };

    // Handle deposit
    const handleDeposit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const allocations = depositForm.allocations
                .filter(a => a.ledgerId && a.amount)
                .map(a => ({
                    ledgerId: a.ledgerId,
                    amount: parseFloat(a.amount),
                    description: a.description
                }));

            if (allocations.length === 0) {
                toast.error('You must select at least one client ledger');
                return;
            }

            const totalAlloc = allocations.reduce((sum, a) => sum + a.amount, 0);
            if (Math.abs(totalAlloc - parseFloat(depositForm.amount)) > 0.01) {
                toast.error('Allocation total must equal deposit amount');
                return;
            }

            await trustApi.post('/api/trust/deposit', {
                trustAccountId: depositForm.trustAccountId,
                amount: parseFloat(depositForm.amount),
                payorPayee: depositForm.payorPayee,
                description: depositForm.description,
                checkNumber: depositForm.checkNumber || undefined,
                allocations
            });

            toast.success('Deposit recorded');
            setShowDepositForm(false);
            setDepositForm({
                trustAccountId: selectedAccount,
                amount: '',
                payorPayee: '',
                description: '',
                checkNumber: '',
                allocations: [{ ledgerId: '', amount: '', description: '' }]
            });
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Deposit failed');
        }
    };

    // Handle withdrawal
    const handleWithdrawal = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            await trustApi.post('/api/trust/withdrawal', {
                trustAccountId: withdrawalForm.trustAccountId,
                ledgerId: withdrawalForm.ledgerId,
                amount: parseFloat(withdrawalForm.amount),
                payorPayee: withdrawalForm.payorPayee,
                description: withdrawalForm.description,
                checkNumber: withdrawalForm.checkNumber || undefined
            });

            toast.success('Withdrawal recorded');
            setShowWithdrawalForm(false);
            setWithdrawalForm({
                trustAccountId: selectedAccount,
                ledgerId: '',
                amount: '',
                payorPayee: '',
                description: '',
                checkNumber: ''
            });
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Withdrawal failed');
        }
    };

    // Handle reconciliation
    const handleReconcile = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const result = await trustApi.post('/api/trust/reconcile', {
                trustAccountId: reconcileForm.trustAccountId,
                periodEnd: reconcileForm.periodEnd,
                bankStatementBalance: parseFloat(reconcileForm.bankStatementBalance),
                notes: reconcileForm.notes
            });

            if (result.isReconciled) {
                toast.success('Reconciliation successful. Three-way match confirmed.');
            } else {
                toast.warning(`Reconciliation discrepancy: $${result.discrepancy.toFixed(2)} - Review needed`);
            }

            setShowReconcileForm(false);
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Reconciliation failed');
        }
    };

    // Handle create ledger
    const handleCreateLedger = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!ledgerForm.clientId || !ledgerForm.trustAccountId) {
            toast.error('Client and Trust Account are required');
            return;
        }
        try {
            await trustApi.post('/api/trust/ledgers', {
                clientId: ledgerForm.clientId,
                matterId: ledgerForm.matterId || null,
                trustAccountId: ledgerForm.trustAccountId,
                notes: ledgerForm.notes || null
            });
            toast.success('Client ledger created successfully');
            setShowCreateLedger(false);
            setLedgerForm({ clientId: '', matterId: '', trustAccountId: selectedAccount, notes: '' });
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Failed to create ledger');
        }
    };

    // Handle create trust account
    const handleCreateAccount = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!accountForm.name || !accountForm.bankName || !accountForm.routingNumber || !accountForm.accountNumber) {
            toast.error('All fields are required');
            return;
        }
        // Validate routing number (9 digits for US)
        if (!/^\d{9}$/.test(accountForm.routingNumber)) {
            toast.error('Routing/ABA number must be exactly 9 digits');
            return;
        }
        try {
            await trustApi.post('/api/trust/accounts', {
                name: accountForm.name,
                bankName: accountForm.bankName,
                routingNumber: accountForm.routingNumber,
                accountNumber: accountForm.accountNumber,
                accountNumberEnc: accountForm.accountNumber,
                jurisdiction: accountForm.jurisdiction,
                entityId: accountForm.entityId || undefined,
                officeId: accountForm.officeId || undefined
            });
            toast.success('Trust account created successfully');
            setShowCreateAccount(false);
            setAccountForm({ name: '', bankName: '', routingNumber: '', accountNumber: '', jurisdiction: 'CA', entityId: '', officeId: '' });
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Failed to create trust account');
        }
    };

    // Handle void transaction
    const handleVoidTransaction = async (txId: string) => {
        const reason = prompt('Void reason:');
        if (!reason) return;

        try {
            await trustApi.post(`/api/trust/transactions/${txId}/void`, { reason });
            toast.success('Transaction voided');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Void failed');
        }
    };

    // Handle approve transaction
    const handleApproveTransaction = async (txId: string) => {
        try {
            await trustApi.post(`/api/trust/transactions/${txId}/approve`, {});
            toast.success('Transaction approved');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Approval failed');
        }
    };

    const handleRejectTransaction = async (txId: string) => {
        const reason = prompt('Rejection reason (optional):') || undefined;
        try {
            await trustApi.post(`/api/trust/transactions/${txId}/reject`, { reason });
            toast.success('Transaction rejected');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Rejection failed');
        }
    };

    const handleClearDeposit = async (txId: string) => {
        try {
            await trustApi.post(`/api/trust/transactions/${txId}/clear`, { notes: 'Cleared from trust operations dashboard' });
            toast.success('Deposit marked as cleared');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Clearance failed');
        }
    };

    const handleReturnDeposit = async (txId: string) => {
        const reason = prompt('Return reason:');
        if (!reason) return;

        try {
            await trustApi.post(`/api/trust/transactions/${txId}/return`, { reason });
            toast.success('Deposit marked as returned');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Return failed');
        }
    };

    const handleImportStatement = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const lines = statementImportForm.linesJson.trim()
                ? JSON.parse(statementImportForm.linesJson)
                : [];

            const result = await trustApi.post('/api/trust/statements/import', {
                trustAccountId: statementImportForm.trustAccountId,
                periodStart: statementImportForm.periodStart,
                periodEnd: statementImportForm.periodEnd,
                statementEndingBalance: parseFloat(statementImportForm.statementEndingBalance || '0'),
                source: statementImportForm.source,
                sourceFileName: statementImportForm.sourceFileName || undefined,
                sourceFileHash: statementImportForm.sourceFileHash || undefined,
                sourceEvidenceKey: statementImportForm.sourceEvidenceKey || undefined,
                allowDuplicateImport: statementImportForm.allowDuplicateImport,
                notes: statementImportForm.notes,
                lines
            }) as TrustStatementImport;

            toast.success(
                result.status === 'duplicate'
                    ? 'Duplicate statement captured as evidence only'
                    : 'Statement imported'
            );
            setStatementImportForm(prev => ({
                ...prev,
                statementEndingBalance: '',
                sourceFileName: '',
                sourceFileHash: '',
                sourceEvidenceKey: '',
                allowDuplicateImport: false,
                notes: '',
                linesJson: ''
            }));
            setSelectedStatementImportId(result.id);
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Statement import failed');
        }
    };

    const handleRegisterEvidenceFile = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const result = await trustApi.post('/api/trust/evidence-files/register', {
                trustAccountId: evidenceFileForm.trustAccountId,
                periodStart: evidenceFileForm.periodStart,
                periodEnd: evidenceFileForm.periodEnd,
                source: evidenceFileForm.source,
                fileName: evidenceFileForm.fileName,
                contentType: evidenceFileForm.contentType || undefined,
                fileHash: evidenceFileForm.fileHash,
                evidenceKey: evidenceFileForm.evidenceKey || undefined,
                fileSizeBytes: evidenceFileForm.fileSizeBytes ? parseInt(evidenceFileForm.fileSizeBytes, 10) : undefined,
                allowDuplicateRegistration: evidenceFileForm.allowDuplicateRegistration,
                notes: evidenceFileForm.notes || undefined
            }) as TrustEvidenceFile;

            toast.success(result.status === 'duplicate' ? 'Duplicate evidence registered as history only' : 'Evidence file registered');
            setEvidenceFileForm(prev => ({
                ...prev,
                fileName: '',
                fileHash: '',
                evidenceKey: '',
                fileSizeBytes: '',
                allowDuplicateRegistration: false,
                notes: ''
            }));
            setParserRunForm(prev => ({
                ...prev,
                trustAccountId: result.trustAccountId,
                trustEvidenceFileId: result.id
            }));
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Evidence file registration failed');
        }
    };

    const handleCreateParserRun = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const lines = parserRunForm.linesJson.trim()
                ? JSON.parse(parserRunForm.linesJson)
                : [];

            const result = await trustApi.post('/api/trust/parser-runs', {
                trustAccountId: parserRunForm.trustAccountId,
                trustEvidenceFileId: parserRunForm.trustEvidenceFileId,
                parserKey: parserRunForm.parserKey,
                statementEndingBalance: parseFloat(parserRunForm.statementEndingBalance || '0'),
                source: parserRunForm.source,
                allowDuplicateImport: parserRunForm.allowDuplicateImport,
                notes: parserRunForm.notes || undefined,
                lines
            }) as TrustStatementParserRun;

            toast.success(result.status === 'completed_duplicate' ? 'Parser run completed with duplicate import evidence' : 'Parser run completed');
            setParserRunForm(prev => ({
                ...prev,
                statementEndingBalance: '',
                allowDuplicateImport: false,
                notes: '',
                linesJson: ''
            }));
            if (result.trustStatementImportId) {
                setSelectedStatementImportId(result.trustStatementImportId);
            }
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Parser run failed');
        }
    };

    const parseCommaSeparatedValues = (value: string) =>
        value
            .split(',')
            .map(item => item.trim())
            .filter(Boolean);

    const handleUpsertPacketTemplate = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const requiredAttestations = packetTemplateForm.requiredAttestationsText.trim()
                ? JSON.parse(packetTemplateForm.requiredAttestationsText) as TrustPacketTemplateAttestationDto[]
                : [];

            const payload: TrustJurisdictionPacketTemplateUpsertDto = {
                policyKey: packetTemplateForm.policyKey.trim(),
                jurisdiction: packetTemplateForm.jurisdiction.trim() || 'DEFAULT',
                accountType: packetTemplateForm.accountType.trim() || 'all',
                templateKey: packetTemplateForm.templateKey.trim(),
                name: packetTemplateForm.name.trim() || undefined,
                versionNumber: parseInt(packetTemplateForm.versionNumber || '1', 10),
                isActive: true,
                requiredSections: parseCommaSeparatedValues(packetTemplateForm.requiredSectionsText),
                requiredAttestations,
                disclosureBlocks: parseCommaSeparatedValues(packetTemplateForm.disclosureBlocksText),
                renderingProfileJson: packetTemplateForm.renderingProfileJson.trim() || undefined,
                metadataJson: packetTemplateForm.metadataJson.trim() || undefined
            };

            const result = await trustApi.post('/api/trust/packet-templates', payload) as TrustJurisdictionPacketTemplateUpsertDto;
            toast.success(`Packet template ${result.templateKey} saved`);
            setPacketTemplateForm(prev => ({
                ...prev,
                policyKey: result.policyKey,
                jurisdiction: result.jurisdiction,
                accountType: result.accountType,
                templateKey: result.templateKey,
                name: result.name || '',
                versionNumber: String(result.versionNumber),
                requiredSectionsText: result.requiredSections.join(', '),
                disclosureBlocksText: result.disclosureBlocks.join(', '),
                requiredAttestationsText: JSON.stringify(result.requiredAttestations, null, 2),
                renderingProfileJson: result.renderingProfileJson || '',
                metadataJson: result.metadataJson || ''
            }));
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Packet template save failed');
        }
    };

    const handleSelectStatementImport = async (statementId: string) => {
        setSelectedStatementImportId(statementId);
        await loadStatementLineWorkspace(statementId);
    };

    const handleCreateOutstandingItem = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            await trustApi.post('/api/trust/outstanding-items', {
                trustAccountId: outstandingItemForm.trustAccountId,
                periodStart: outstandingItemForm.periodStart,
                periodEnd: outstandingItemForm.periodEnd,
                itemType: outstandingItemForm.itemType,
                impactDirection: outstandingItemForm.impactDirection,
                amount: parseFloat(outstandingItemForm.amount),
                reference: outstandingItemForm.reference || undefined,
                description: outstandingItemForm.description || undefined,
                reasonCode: outstandingItemForm.reasonCode || undefined,
                attachmentEvidenceKey: outstandingItemForm.attachmentEvidenceKey || undefined
            });

            toast.success('Outstanding item added');
            setOutstandingItemForm(prev => ({
                ...prev,
                amount: '',
                reference: '',
                description: '',
                reasonCode: '',
                attachmentEvidenceKey: ''
            }));
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Outstanding item creation failed');
        }
    };

    const handleGeneratePacket = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            await trustApi.post('/api/trust/reconciliation-packets', {
                trustAccountId: packetForm.trustAccountId,
                periodStart: packetForm.periodStart,
                periodEnd: packetForm.periodEnd,
                statementImportId: packetForm.statementImportId || undefined,
                statementEndingBalance: packetForm.statementImportId ? undefined : parseFloat(packetForm.statementEndingBalance || '0'),
                notes: packetForm.notes || undefined
            });

            toast.success('Reconciliation packet prepared');
            await loadData();
        } catch (err: any) {
            toast.error(err.message || 'Packet generation failed');
        }
    };

    const handleInspectPacket = async (packet: TrustReconciliationPacket) => {
        setSelectedPacketId(packet.id);
        if (packet.statementImportId) {
            setSelectedStatementImportId(packet.statementImportId);
        }
        await loadPacketDetail(packet.id);
    };

    const handleRunStatementMatching = async () => {
        if (!selectedStatementImportId) {
            toast.error('Select a statement import first');
            return;
        }

        setMatchingRunLoading(true);
        try {
            const result = await trustApi.post(`/api/trust/statements/${selectedStatementImportId}/match-run`, {}) as TrustStatementMatchingRunResult;
            await loadData();
            await loadStatementLineWorkspace(selectedStatementImportId, true);
            if (selectedPacketId) {
                await loadPacketDetail(selectedPacketId, true);
            }
            toast.success(`Auto-match complete: ${result.matchedLineCount}/${result.totalLineCount} lines matched`);
        } catch (err: any) {
            toast.error(err.message || 'Auto-match failed');
        } finally {
            setMatchingRunLoading(false);
        }
    };

    const handleResolveStatementLine = async (line: TrustStatementLine, action: 'match' | 'ignore' | 'reject' | 'unmatch') => {
        const trustTransactionId = action === 'match' ? lineMatchSelections[line.id] : undefined;
        if (action === 'match' && !trustTransactionId) {
            toast.error('Select a trust transaction before matching');
            return;
        }

        setLineResolutionLoadingId(line.id);
        try {
            await trustApi.post(`/api/trust/statement-lines/${line.id}/resolve`, {
                action,
                trustTransactionId,
                notes: action === 'ignore'
                    ? 'Ignored in reconciliation workspace'
                    : action === 'reject'
                        ? 'Rejected in reconciliation workspace'
                    : action === 'unmatch'
                        ? 'Manually unmatched in reconciliation workspace'
                        : 'Matched in reconciliation workspace'
            });
            await loadData();
            await loadStatementLineWorkspace(selectedStatementImportId, true);
            if (selectedPacketId) {
                await loadPacketDetail(selectedPacketId, true);
            }
            toast.success(
                action === 'match'
                    ? 'Statement line matched'
                    : action === 'ignore'
                        ? 'Statement line ignored'
                        : action === 'reject'
                            ? 'Statement line rejected'
                            : 'Statement line unmatched'
            );
        } catch (err: any) {
            toast.error(err.message || 'Statement line update failed');
        } finally {
            setLineResolutionLoadingId(null);
        }
    };

    const handleSignoffPacket = async (packetId: string) => {
        const notes = prompt('Sign-off notes (optional):') || undefined;
        try {
            await trustApi.post(`/api/trust/reconciliation-packets/${packetId}/signoff`, { notes });
            toast.success('Reconciliation packet signed off');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Sign-off failed');
        }
    };

    const handleApproveRequirement = async (txId: string, requirementType?: string) => {
        try {
            await trustApi.post(`/api/trust/transactions/${txId}/approve-step`, requirementType ? { requirementType } : {});
            toast.success('Approval step recorded');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Approval step failed');
        }
    };

    const handlePrepareMonthCloseFromPacket = async (packet: TrustReconciliationPacket) => {
        try {
            await trustApi.post('/api/trust/month-close/prepare', {
                trustAccountId: packet.trustAccountId,
                periodStart: packet.periodStart,
                periodEnd: packet.periodEnd,
                reconciliationPacketId: packet.id,
                autoGeneratePacket: false
            });
            toast.success('Month close prepared');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Month close preparation failed');
        }
    };

    const collectRequiredAttestations = (close: TrustMonthCloseDto, role: 'reviewer' | 'responsible_lawyer') => {
        const roleAttestations = close.requiredAttestations.filter(attestation => attestation.role === role && attestation.required);
        if (roleAttestations.length === 0) {
            return [];
        }

        const confirmed = window.confirm(
            `${role === 'reviewer' ? 'Reviewer' : 'Responsible lawyer'} attestations:\n\n${roleAttestations.map(attestation => `- ${attestation.label}`).join('\n')}\n\nProceed and record these attestations?`
        );

        if (!confirmed) {
            return null;
        }

        return roleAttestations.map(attestation => ({
            key: attestation.key,
            accepted: true,
            notes: `${role === 'reviewer' ? 'Reviewer' : 'Responsible lawyer'} accepted required attestation in UI.`
        }));
    };

    const handleSignoffMonthClose = async (close: TrustMonthCloseDto, role: 'reviewer' | 'responsible_lawyer') => {
        const notes = prompt(`${role === 'reviewer' ? 'Reviewer' : 'Responsible lawyer'} notes (optional):`) || undefined;
        const attestations = collectRequiredAttestations(close, role);
        if (attestations === null) {
            return;
        }
        try {
            await trustApi.post(`/api/trust/month-close/${close.id}/signoff`, { role, notes, attestations });
            toast.success(role === 'reviewer' ? 'Reviewer sign-off recorded' : 'Responsible lawyer sign-off recorded');
            loadData();
        } catch (err: any) {
            toast.error(err.message || 'Month close sign-off failed');
        }
    };

    // Format currency
    const formatCurrency = (amount: number) => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD'
        }).format(amount);
    };

    // Format date
    const formatDate = (dateStr: string) => {
        return new Date(dateStr).toLocaleDateString('en-US', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    };

    const formatShortDate = (dateStr: string) => {
        return new Date(dateStr).toLocaleDateString('en-US', {
            month: 'short',
            day: '2-digit',
            year: 'numeric'
        });
    };

    const getApprovalRequirementTone = (requirement: TrustApprovalRequirementDto) => {
        const normalizedStatus = (requirement.status || '').toLowerCase();
        if (normalizedStatus === 'satisfied' || requirement.satisfiedCount >= requirement.requiredCount) {
            return 'bg-green-100 text-green-800';
        }
        if (normalizedStatus === 'waived' || normalizedStatus === 'overridden') {
            return 'bg-blue-100 text-blue-800';
        }
        return 'bg-yellow-100 text-yellow-800';
    };

    const getMonthCloseTone = (status: string) => {
        const normalized = (status || '').toLowerCase();
        if (normalized === 'closed' || normalized === 'signed_off') {
            return 'bg-green-100 text-green-800';
        }
        if (normalized === 'partially_signed' || normalized === 'ready_for_signoff') {
            return 'bg-blue-100 text-blue-800';
        }
        if (normalized === 'blocked' || normalized === 'needs_review') {
            return 'bg-red-100 text-red-800';
        }
        return 'bg-yellow-100 text-yellow-800';
    };

    const getPacketTone = (status: string) => {
        const normalized = (status || '').toLowerCase();
        if (normalized === 'signed_off') {
            return 'bg-green-100 text-green-800';
        }
        if (normalized === 'ready_for_signoff') {
            return 'bg-blue-100 text-blue-800';
        }
        if (normalized === 'needs_review') {
            return 'bg-red-100 text-red-800';
        }
        if (normalized === 'superseded') {
            return 'bg-gray-100 text-gray-700';
        }
        return 'bg-yellow-100 text-yellow-800';
    };

    const getOperationalAlertTone = (severity: string) => {
        const normalized = (severity || '').toLowerCase();
        if (normalized === 'critical') {
            return 'bg-red-100 text-red-800 border-red-200';
        }
        if (normalized === 'warning') {
            return 'bg-yellow-100 text-yellow-800 border-yellow-200';
        }
        return 'bg-blue-100 text-blue-800 border-blue-200';
    };

    const getOperationalWorkflowTone = (workflowStatus?: string) => {
        const normalized = (workflowStatus || 'open').toLowerCase();
        if (normalized === 'resolved') return 'bg-emerald-100 text-emerald-800 border-emerald-200';
        if (normalized === 'escalated') return 'bg-red-100 text-red-800 border-red-200';
        if (normalized === 'acknowledged') return 'bg-blue-100 text-blue-800 border-blue-200';
        if (normalized === 'assigned') return 'bg-violet-100 text-violet-800 border-violet-200';
        return 'bg-slate-100 text-slate-800 border-slate-200';
    };

    const getCloseForecastTone = (status: string) => {
        const normalized = (status || '').toLowerCase();
        if (normalized === 'closed' || normalized === 'ready') {
            return 'bg-emerald-100 text-emerald-800 border-emerald-200';
        }
        if (normalized === 'at_risk') {
            return 'bg-amber-100 text-amber-800 border-amber-200';
        }
        if (normalized === 'blocked' || normalized === 'overdue') {
            return 'bg-red-100 text-red-800 border-red-200';
        }
        return 'bg-slate-100 text-slate-800 border-slate-200';
    };

    const getStatementImportTone = (status: string) => {
        const normalized = (status || '').toLowerCase();
        if (normalized === 'duplicate') {
            return 'bg-orange-100 text-orange-800';
        }
        if (normalized === 'superseded') {
            return 'bg-gray-200 text-gray-700';
        }
        if (normalized === 'matched') {
            return 'bg-green-100 text-green-800';
        }
        if (normalized === 'needs_review') {
            return 'bg-red-100 text-red-800';
        }
        return 'bg-blue-100 text-blue-800';
    };

    const shortenFingerprint = (value?: string | null) => {
        if (!value) return 'n/a';
        if (value.length <= 12) return value;
        return `${value.slice(0, 8)}...${value.slice(-4)}`;
    };

    const getStatementLineTone = (status: string) => {
        const normalized = (status || '').toLowerCase();
        if (normalized === 'matched') {
            return 'bg-green-100 text-green-800';
        }
        if (normalized === 'ignored') {
            return 'bg-gray-100 text-gray-700';
        }
        if (normalized === 'rejected') {
            return 'bg-red-100 text-red-800';
        }
        return 'bg-yellow-100 text-yellow-800';
    };

    const getStatementLineCandidates = (line: TrustStatementLine) => {
        const exactAmountMatches = statementMatchingCandidates.filter(tx => Math.abs(Number(tx.amount) - Math.abs(Number(line.amount))) < 0.01);
        return exactAmountMatches.length > 0 ? exactAmountMatches : statementMatchingCandidates.slice(0, 12);
    };

    const getCompletedCloseStepCount = (close: TrustMonthCloseDto) => close.steps.filter(step => step.status === 'completed').length;
    const trustNavigationTabs = [
        { id: 'overview', label: 'Overview', icon: Eye },
        { id: 'accounts', label: 'Accounts', icon: Building2 },
        { id: 'ledgers', label: 'Client Ledgers', icon: Users },
        { id: 'transactions', label: 'Transactions', icon: History },
        { id: 'reconciliation', label: 'Reconciliation', icon: FileCheck },
        ...(canViewAdminAuditLogs ? [{ id: 'audit', label: 'Audit Log', icon: FileText }] : [])
    ];

    if (loading) {
        return (
            <div className="flex items-center justify-center h-full">
                <RefreshCw className="w-8 h-8 animate-spin text-primary-500" />
            </div>
        );
    }

    return (
        <div className="h-full overflow-y-auto p-4 sm:p-6 space-y-6">
            {/* Header */}
            <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                <div className="min-w-0">
                    <h1 className="text-2xl font-bold text-gray-900 dark:text-white flex items-center gap-2">
                        <Scale className="w-7 h-7 text-primary-600" />
                        IOLTA Trust Accounting
                    </h1>
                    <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                        ABA Model Rule 1.15 Compliant Trust Account Management
                    </p>
                </div>
                <div className="flex flex-wrap items-center gap-3 xl:justify-end">
                    <EntityOfficeFilter
                        entityId={entityFilter}
                        officeId={officeFilter}
                        onEntityChange={setEntityFilter}
                        onOfficeChange={setOfficeFilter}
                        allowAll
                    />
                    {canDeposit && (
                        <button
                            onClick={() => setShowDepositForm(true)}
                            className="btn-primary flex items-center gap-2"
                        >
                            <ArrowDownCircle className="w-4 h-4" />
                            Deposit
                        </button>
                    )}
                    {canWithdraw && (
                        <button
                            onClick={() => setShowWithdrawalForm(true)}
                            className="btn-secondary flex items-center gap-2"
                        >
                            <ArrowUpCircle className="w-4 h-4" />
                            Withdrawal
                        </button>
                    )}
                    {canReconcile && (
                        <button
                            onClick={() => setActiveTab('reconciliation')}
                            className="btn-outline flex items-center gap-2"
                        >
                            <Calculator className="w-4 h-4" />
                            Reconciliation
                        </button>
                    )}
                </div>
            </div>

            {/* KPI Cards */}
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-5 gap-4">
                <div className="glass-card p-4 rounded-xl">
                    <div className="flex items-center gap-3">
                        <div className="p-3 rounded-lg bg-green-100 dark:bg-green-900/30">
                            <DollarSign className="w-6 h-6 text-green-600" />
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Journal Balance</p>
                            <p className="text-xl font-bold text-gray-900 dark:text-white">
                                {formatCurrency(totalTrustBalance)}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                {filteredAccounts.length} trust account{filteredAccounts.length === 1 ? '' : 's'}
                            </p>
                        </div>
                    </div>
                </div>

                <div className="glass-card p-4 rounded-xl">
                    <div className="flex items-center gap-3">
                        <div className="p-3 rounded-lg bg-blue-100 dark:bg-blue-900/30">
                            <Users className="w-6 h-6 text-blue-600" />
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Available To Disburse</p>
                            <p className="text-xl font-bold text-gray-900 dark:text-white">
                                {formatCurrency(totalAvailableToDisburse)}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                Client ledger total: {formatCurrency(totalClientLedgers)}
                            </p>
                        </div>
                    </div>
                </div>

                <div className="glass-card p-4 rounded-xl">
                    <div className="flex items-center gap-3">
                        <div className={`p-3 rounded-lg ${pendingClearanceCount > 0 ? 'bg-yellow-100 dark:bg-yellow-900/30' : 'bg-emerald-100 dark:bg-emerald-900/30'}`}>
                            <History className={`w-6 h-6 ${pendingClearanceCount > 0 ? 'text-yellow-600' : 'text-emerald-600'}`} />
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Uncleared Funds</p>
                            <p className="text-xl font-bold text-gray-900 dark:text-white">
                                {formatCurrency(totalUnclearedFunds)}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                {pendingClearanceCount} deposit{pendingClearanceCount === 1 ? '' : 's'} awaiting clearance
                            </p>
                        </div>
                    </div>
                </div>

                <div className="glass-card p-4 rounded-xl">
                    <div className="flex items-center gap-3">
                        <div className={`p-3 rounded-lg ${(visibleOpenHolds.length > 0 || unresolvedPackets > 0 || approvalQueueCount > 0 || unreconciledAccounts > 0) ? 'bg-red-100 dark:bg-red-900/30' : 'bg-green-100 dark:bg-green-900/30'}`}>
                            {(visibleOpenHolds.length > 0 || unresolvedPackets > 0 || approvalQueueCount > 0 || unreconciledAccounts > 0) ? (
                                <AlertTriangle className="w-6 h-6 text-red-600" />
                            ) : (
                                <CheckCircle2 className="w-6 h-6 text-green-600" />
                            )}
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Compliance Queue</p>
                            <p className={`text-xl font-bold ${(visibleOpenHolds.length > 0 || unresolvedPackets > 0 || approvalQueueCount > 0 || unreconciledAccounts > 0) ? 'text-red-600' : 'text-green-600'}`}>
                                {visibleOpenHolds.length + unresolvedPackets + approvalQueueCount}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                {visibleOpenHolds.length} hold / {unresolvedPackets} unsigned packet / {approvalQueueCount} pending approval
                            </p>
                        </div>
                    </div>
                </div>

                <div className="glass-card p-4 rounded-xl">
                    <div className="flex items-center gap-3">
                        <div className={`p-3 rounded-lg ${openMonthCloseCount > 0 ? 'bg-blue-100 dark:bg-blue-900/30' : 'bg-gray-100 dark:bg-gray-800'}`}>
                            <FileCheck className={`w-6 h-6 ${openMonthCloseCount > 0 ? 'text-blue-600' : 'text-gray-500'}`} />
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Month Close</p>
                            <p className="text-xl font-bold text-gray-900 dark:text-white">
                                {openMonthCloseCount}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                {filteredMonthCloses.filter(close => close.status === 'partially_signed').length} awaiting lawyer / {filteredMonthCloses.filter(close => close.status === 'in_progress').length} in progress
                            </p>
                        </div>
                    </div>
                </div>
            </div>

            <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">Operational Alerts</h3>
                        <p className="mt-1 text-sm text-gray-500">
                            Compliance watchlist for missing closes, aged exceptions, duplicate statement evidence, and uncleared funds.
                        </p>
                    </div>
                    <div className="flex flex-col gap-3 lg:flex-row lg:items-start">
                        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Open</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">{filteredOperationalAlerts.length}</div>
                            </div>
                            <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-red-600">Critical</div>
                                <div className="mt-1 text-xl font-semibold text-red-700">{operationalCriticalCount}</div>
                            </div>
                            <div className="rounded-xl border border-yellow-200 bg-yellow-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-yellow-700">Warning</div>
                                <div className="mt-1 text-xl font-semibold text-yellow-800">{operationalWarningCount}</div>
                            </div>
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Accounts</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">
                                    {new Set(filteredOperationalAlerts.map(alert => alert.trustAccountId).filter(Boolean)).size}
                                </div>
                            </div>
                        </div>
                        <button
                            type="button"
                            onClick={() => loadOperationalAlerts(true)}
                            className="inline-flex items-center justify-center gap-2 rounded-xl border border-gray-200 bg-white px-4 py-3 text-sm font-semibold text-gray-700 transition hover:border-primary-200 hover:text-primary-700"
                        >
                            <RefreshCw className="h-4 w-4" />
                            Sync Alerts
                        </button>
                    </div>
                </div>

                {latestOperationalAlerts.length === 0 ? (
                    <div className="mt-4 rounded-xl border border-dashed border-gray-200 bg-gray-50 px-4 py-5 text-sm text-gray-500">
                        No operational alerts in the current filter.
                    </div>
                ) : (
                    <div className="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-2">
                        {latestOperationalAlerts.map(alert => (
                            <div key={alert.alertId || `${alert.alertType}-${alert.relatedEntityId || alert.openedAt}`} className={`rounded-xl border px-4 py-3 ${getOperationalAlertTone(alert.severity)}`}>
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <div className="text-sm font-semibold">{alert.title}</div>
                                        <div className="mt-1 text-xs opacity-80">
                                            {alert.trustAccountName || getAccountLabel(alert.trustAccountId)}{alert.periodEnd ? ` / ${formatShortDate(alert.periodEnd)}` : ''} / {alert.ageDays}d open
                                        </div>
                                    </div>
                                    <div className="flex flex-col items-end gap-2">
                                        <span className="rounded-full bg-white/70 px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.18em]">
                                            {alert.severity}
                                        </span>
                                        <span className={`rounded-full border px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] ${getOperationalWorkflowTone(alert.workflowStatus)}`}>
                                            {alert.workflowStatus || 'open'}
                                        </span>
                                    </div>
                                </div>
                                <p className="mt-3 text-sm leading-6">{alert.summary}</p>
                                {alert.actionHint && (
                                    <div className="mt-2 text-xs font-medium opacity-80">{alert.actionHint}</div>
                                )}
                                <div className="mt-3 flex flex-wrap gap-2 text-[11px] font-medium opacity-80">
                                    {alert.assignedUserId && <span>Owner: {alert.assignedUserId}</span>}
                                    {typeof alert.notificationCount === 'number' && <span>Inbox: {alert.notificationCount}</span>}
                                </div>
                                {alert.alertId && (
                                    <div className="mt-4 flex flex-wrap gap-2">
                                        <button
                                            type="button"
                                            disabled={operationalAlertBusyId === alert.alertId}
                                            onClick={() => handleOperationalAlertAction(alert, 'ack')}
                                            className="rounded-lg border border-blue-200 bg-white px-3 py-1.5 text-xs font-semibold text-blue-700 transition hover:bg-blue-50 disabled:opacity-60"
                                        >
                                            Acknowledge
                                        </button>
                                        <button
                                            type="button"
                                            disabled={operationalAlertBusyId === alert.alertId || !user?.id}
                                            onClick={() => handleOperationalAlertAction(alert, 'assign')}
                                            className="rounded-lg border border-violet-200 bg-white px-3 py-1.5 text-xs font-semibold text-violet-700 transition hover:bg-violet-50 disabled:opacity-60"
                                        >
                                            Assign Me
                                        </button>
                                        <button
                                            type="button"
                                            disabled={operationalAlertBusyId === alert.alertId}
                                            onClick={() => handleOperationalAlertAction(alert, 'escalate')}
                                            className="rounded-lg border border-red-200 bg-white px-3 py-1.5 text-xs font-semibold text-red-700 transition hover:bg-red-50 disabled:opacity-60"
                                        >
                                            Escalate
                                        </button>
                                        <button
                                            type="button"
                                            disabled={operationalAlertBusyId === alert.alertId}
                                            onClick={() => handleOperationalAlertAction(alert, 'resolve')}
                                            className="rounded-lg border border-emerald-200 bg-white px-3 py-1.5 text-xs font-semibold text-emerald-700 transition hover:bg-emerald-50 disabled:opacity-60"
                                        >
                                            Resolve
                                        </button>
                                        <button
                                            type="button"
                                            disabled={operationalAlertHistoryLoading && selectedOperationalAlertId === alert.alertId}
                                            onClick={() => loadOperationalAlertHistory(alert.alertId!)}
                                            className="rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-xs font-semibold text-gray-700 transition hover:bg-gray-50 disabled:opacity-60"
                                        >
                                            History
                                        </button>
                                    </div>
                                )}
                                {selectedOperationalAlertId === alert.alertId && (
                                    <div className="mt-4 rounded-xl border border-white/70 bg-white/70 px-3 py-3 text-xs text-gray-700">
                                        <div className="flex items-center justify-between gap-3">
                                            <div className="font-semibold text-gray-900">Lifecycle</div>
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    setSelectedOperationalAlertId(null);
                                                    setSelectedOperationalAlertHistory([]);
                                                }}
                                                className="text-gray-500 transition hover:text-gray-700"
                                            >
                                                Close
                                            </button>
                                        </div>
                                        {operationalAlertHistoryLoading ? (
                                            <div className="mt-3 text-gray-500">Loading history...</div>
                                        ) : selectedOperationalAlertHistory.length === 0 ? (
                                            <div className="mt-3 text-gray-500">No lifecycle events recorded yet.</div>
                                        ) : (
                                            <div className="mt-3 space-y-2">
                                                {selectedOperationalAlertHistory.map(event => (
                                                    <div key={event.id} className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                                        <div className="flex items-center justify-between gap-3">
                                                            <span className="font-semibold uppercase tracking-[0.16em] text-[10px] text-gray-500">{event.eventType}</span>
                                                            <span className="text-[10px] text-gray-400">{formatDate(event.createdAt)}</span>
                                                        </div>
                                                        {event.actorUserId && <div className="mt-1 text-gray-700">Actor: {event.actorUserId}</div>}
                                                        {event.notes && <div className="mt-1 text-gray-600">{event.notes}</div>}
                                                    </div>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                )}
                            </div>
                        ))}
                    </div>
                )}
            </div>

            <div className="rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">Trust Ops Inbox</h3>
                        <p className="mt-1 text-sm text-gray-500">
                            Routed blocker queue with ownership, deferral, escalation, and SLA breach visibility.
                        </p>
                    </div>
                    <div className="flex flex-col gap-3 lg:flex-row lg:items-start">
                        <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Open</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">{filteredOpsInboxItems.length}</div>
                            </div>
                            <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-red-600">Breached</div>
                                <div className="mt-1 text-xl font-semibold text-red-700">{opsInboxBreachedCount}</div>
                            </div>
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Close</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">{filteredOpsInboxItems.filter(item => item.blockerGroup === 'close_blocker').length}</div>
                            </div>
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Statement</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">{filteredOpsInboxItems.filter(item => item.blockerGroup === 'statement_blocker').length}</div>
                            </div>
                            <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                <div className="text-[11px] uppercase tracking-[0.12em] text-gray-500">Exception</div>
                                <div className="mt-1 text-xl font-semibold text-gray-900">{filteredOpsInboxItems.filter(item => item.blockerGroup === 'exception_blocker').length}</div>
                            </div>
                        </div>
                        <button
                            type="button"
                            onClick={() => loadOpsInbox(true)}
                            className="inline-flex items-center justify-center gap-2 rounded-xl border border-gray-200 bg-white px-4 py-3 text-sm font-semibold text-gray-700 transition hover:border-primary-200 hover:text-primary-700"
                        >
                            <RefreshCw className="h-4 w-4" />
                            Sync Inbox
                        </button>
                    </div>
                </div>

                {latestOpsInboxItems.length === 0 ? (
                    <div className="mt-4 rounded-xl border border-dashed border-gray-200 bg-gray-50 px-4 py-5 text-sm text-gray-500">
                        No trust ops inbox items in the current filter.
                    </div>
                ) : (
                    <div className="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-2">
                        {latestOpsInboxItems.map(item => (
                            <div key={item.id} className={`rounded-xl border px-4 py-3 ${getOperationalAlertTone(item.isSlaBreached ? 'critical' : item.severity)}`}>
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <div className="text-sm font-semibold">{item.title}</div>
                                        <div className="mt-1 text-xs opacity-80">
                                            {item.trustAccountName || getAccountLabel(item.trustAccountId)} / {item.blockerGroup.replace('_', ' ')} / {item.ageDays}d open
                                        </div>
                                    </div>
                                    <div className="flex flex-col items-end gap-2">
                                        {item.dueAt && (
                                            <span className="rounded-full bg-white/70 px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.18em]">
                                                Due {formatShortDate(item.dueAt)}
                                            </span>
                                        )}
                                        <span className={`rounded-full border px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] ${getOperationalWorkflowTone(item.workflowStatus)}`}>
                                            {item.workflowStatus}
                                        </span>
                                    </div>
                                </div>
                                <p className="mt-3 text-sm leading-6">{item.summary}</p>
                                <div className="mt-2 flex flex-wrap gap-2 text-[11px] font-medium opacity-80">
                                    {item.assignedUserId && <span>Owner: {item.assignedUserId}</span>}
                                    {item.suggestedExportType && <span>Export: {item.suggestedExportType}</span>}
                                    {item.isSlaBreached && <span>SLA breached</span>}
                                </div>
                                <div className="mt-4 flex flex-wrap gap-2">
                                    <button type="button" disabled={opsInboxBusyId === item.id} onClick={() => handleOpsInboxAction(item, 'claim')} className="rounded-lg border border-blue-200 bg-white px-3 py-1.5 text-xs font-semibold text-blue-700 transition hover:bg-blue-50 disabled:opacity-60">
                                        Claim
                                    </button>
                                    <button type="button" disabled={opsInboxBusyId === item.id || !user?.id} onClick={() => handleOpsInboxAction(item, 'assign')} className="rounded-lg border border-violet-200 bg-white px-3 py-1.5 text-xs font-semibold text-violet-700 transition hover:bg-violet-50 disabled:opacity-60">
                                        Assign Me
                                    </button>
                                    <button type="button" disabled={opsInboxBusyId === item.id} onClick={() => handleOpsInboxAction(item, 'defer')} className="rounded-lg border border-amber-200 bg-white px-3 py-1.5 text-xs font-semibold text-amber-700 transition hover:bg-amber-50 disabled:opacity-60">
                                        Defer 24h
                                    </button>
                                    <button type="button" disabled={opsInboxBusyId === item.id} onClick={() => handleOpsInboxAction(item, 'escalate')} className="rounded-lg border border-red-200 bg-white px-3 py-1.5 text-xs font-semibold text-red-700 transition hover:bg-red-50 disabled:opacity-60">
                                        Escalate
                                    </button>
                                    <button type="button" disabled={opsInboxBusyId === item.id} onClick={() => handleOpsInboxAction(item, 'resolve')} className="rounded-lg border border-emerald-200 bg-white px-3 py-1.5 text-xs font-semibold text-emerald-700 transition hover:bg-emerald-50 disabled:opacity-60">
                                        Resolve
                                    </button>
                                    <button type="button" disabled={opsInboxHistoryLoading && selectedOpsInboxId === item.id} onClick={() => loadOpsInboxHistory(item.id)} className="rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-xs font-semibold text-gray-700 transition hover:bg-gray-50 disabled:opacity-60">
                                        History
                                    </button>
                                </div>
                                {selectedOpsInboxId === item.id && (
                                    <div className="mt-4 rounded-xl border border-white/70 bg-white/70 px-3 py-3 text-xs text-gray-700">
                                        <div className="flex items-center justify-between gap-3">
                                            <div className="font-semibold text-gray-900">Inbox History</div>
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    setSelectedOpsInboxId(null);
                                                    setSelectedOpsInboxHistory([]);
                                                }}
                                                className="text-gray-500 transition hover:text-gray-700"
                                            >
                                                Close
                                            </button>
                                        </div>
                                        {opsInboxHistoryLoading ? (
                                            <div className="mt-3 text-gray-500">Loading history...</div>
                                        ) : selectedOpsInboxHistory.length === 0 ? (
                                            <div className="mt-3 text-gray-500">No inbox events recorded yet.</div>
                                        ) : (
                                            <div className="mt-3 space-y-2">
                                                {selectedOpsInboxHistory.map(event => (
                                                    <div key={event.id} className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                                        <div className="flex items-center justify-between gap-3">
                                                            <span className="font-semibold uppercase tracking-[0.16em] text-[10px] text-gray-500">{event.eventType}</span>
                                                            <span className="text-[10px] text-gray-400">{formatDate(event.createdAt)}</span>
                                                        </div>
                                                        {event.actorUserId && <div className="mt-1 text-gray-700">Actor: {event.actorUserId}</div>}
                                                        {event.notes && <div className="mt-1 text-gray-600">{event.notes}</div>}
                                                    </div>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                )}
                            </div>
                        ))}
                    </div>
                )}
            </div>

            {/* Three-Way Balance Check */}
            {Math.abs(totalTrustBalance - totalClientLedgers) > 0.01 && (
                <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
                    <div className="flex items-center gap-3">
                        <AlertTriangle className="w-6 h-6 text-red-600" />
                        <div>
                            <h3 className="font-semibold text-red-800 dark:text-red-200">
                                 Balance Discrepancy Detected
                            </h3>
                            <p className="text-sm text-red-700 dark:text-red-300">
                                Trust account balance ({formatCurrency(totalTrustBalance)}) does not match client ledgers total
                                ({formatCurrency(totalClientLedgers)}).
                                Difference: {formatCurrency(Math.abs(totalTrustBalance - totalClientLedgers))}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Tabs */}
            <div className="border-b border-gray-200 dark:border-gray-700">
                <nav className="flex flex-wrap gap-2 overflow-x-auto pb-1">
                    {trustNavigationTabs.map(tab => (
                        <button
                            key={tab.id}
                            onClick={() => setActiveTab(tab.id as any)}
                            className={`flex shrink-0 items-center gap-2 px-4 py-3 border-b-2 transition-colors ${activeTab === tab.id
                                ? 'border-primary-500 text-primary-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700'
                                }`}
                        >
                            <tab.icon className="w-4 h-4" />
                            {tab.label}
                        </button>
                    ))}
                </nav>
            </div>

            {/* Tab Content */}
            <div className="glass-card rounded-xl p-6">
                {/* Accounts Tab */}
                {activeTab === 'accounts' && (
                    <div className="space-y-4">
                        <div className="flex items-center justify-between">
                            <h2 className="text-lg font-semibold">Trust Bank Accounts</h2>
                            <button
                                onClick={() => {
                                    setAccountForm(prev => ({
                                        ...prev,
                                        entityId: entityFilter || prev.entityId,
                                        officeId: officeFilter || prev.officeId
                                    }));
                                    setShowCreateAccount(true);
                                }}
                                className="btn-sm btn-primary flex items-center gap-1"
                            >
                                <Plus className="w-4 h-4" /> New Account
                            </button>
                        </div>
                        {filteredAccounts.length === 0 ? (
                            <p className="text-gray-500">
                                {entityFilter || officeFilter
                                    ? 'No trust accounts match the current entity/office filter.'
                                    : 'No trust accounts yet. Click "New Account" to create one.'}
                            </p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b dark:border-gray-700">
                                            <th className="text-left py-3 px-4">Account Name</th>
                                            <th className="text-left py-3 px-4">Bank</th>
                                            <th className="text-left py-3 px-4">Jurisdiction</th>
                                            <th className="text-right py-3 px-4">Journal</th>
                                            <th className="text-right py-3 px-4">Cleared</th>
                                            <th className="text-right py-3 px-4">Uncleared</th>
                                            <th className="text-right py-3 px-4">Available</th>
                                            <th className="text-left py-3 px-4">Latest Reconciliation</th>
                                            <th className="text-center py-3 px-4">Status</th>
                                            {canExport && <th className="text-center py-3 px-4">Export</th>}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {filteredAccounts.map(account => {
                                            const latestPacket = latestPacketByAccount.get(account.id);
                                            const latestLegacyRecord = latestLegacyReconciliationByAccount.get(account.id);
                                            const latestPeriodLabel = latestPacket
                                                ? `${latestPacket.status.replace(/_/g, ' ')} / ${new Date(latestPacket.periodEnd).toLocaleDateString('en-US')}`
                                                : latestLegacyRecord
                                                    ? `${latestLegacyRecord.isReconciled ? 'legacy matched' : 'legacy exception'} / ${new Date(latestLegacyRecord.periodEnd).toLocaleDateString('en-US')}`
                                                    : 'No packet prepared';

                                            return (
                                                <tr key={account.id} className="border-b dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-800">
                                                    <td className="py-3 px-4">
                                                        <div className="font-medium">{account.name}</div>
                                                        <div className="text-xs text-gray-500 font-mono">
                                                            {maskAccountNumber(account.accountNumberEnc)}
                                                        </div>
                                                    </td>
                                                    <td className="py-3 px-4">{account.bankName}</td>
                                                    <td className="py-3 px-4">{account.jurisdiction}</td>
                                                    <td className="py-3 px-4 text-right font-semibold">
                                                        {formatCurrency(Number(account.currentBalance))}
                                                    </td>
                                                    <td className="py-3 px-4 text-right">
                                                        {formatCurrency(Number(account.clearedBalance ?? account.currentBalance))}
                                                    </td>
                                                    <td className="py-3 px-4 text-right text-yellow-700">
                                                        {formatCurrency(Number(account.unclearedBalance ?? 0))}
                                                    </td>
                                                    <td className="py-3 px-4 text-right text-green-700 font-semibold">
                                                        {formatCurrency(Number(account.availableDisbursementCapacity ?? account.clearedBalance ?? account.currentBalance))}
                                                    </td>
                                                    <td className="py-3 px-4">
                                                        <div className="text-sm">{latestPeriodLabel}</div>
                                                        {latestPacket && (
                                                            <div className="text-xs text-gray-500">
                                                                Exceptions: {latestPacket.exceptionCount}
                                                            </div>
                                                        )}
                                                    </td>
                                                    <td className="py-3 px-4 text-center">
                                                        <span className={`px-2 py-1 rounded-full text-xs ${account.status === 'ACTIVE'
                                                            ? 'bg-green-100 text-green-800'
                                                            : 'bg-gray-100 text-gray-800'
                                                            }`}>
                                                            {account.status}
                                                        </span>
                                                    </td>
                                                    {canExport && (
                                                        <td className="py-3 px-4 text-center">
                                                            <div className="inline-flex items-center gap-2">
                                                                <button
                                                                    onClick={() => handleGenerateExport({ exportType: 'account_journal', format: 'csv', trustAccountId: account.id })}
                                                                    className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                                >
                                                                    Journal CSV
                                                                </button>
                                                                <button
                                                                    onClick={() => handleGenerateExport({ exportType: 'account_journal', format: 'json', trustAccountId: account.id })}
                                                                    className="text-xs font-semibold text-gray-600 hover:text-gray-800"
                                                                >
                                                                    JSON
                                                                </button>
                                                            </div>
                                                        </td>
                                                    )}
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                )}

                {/* Ledgers Tab */}
                {activeTab === 'ledgers' && (
                    <div className="space-y-4">
                        <div className="flex items-center justify-between">
                            <h2 className="text-lg font-semibold">Client Trust Ledgers</h2>
                            <button
                                onClick={() => setShowCreateLedger(true)}
                                className="btn-sm btn-primary flex items-center gap-1"
                            >
                                <Plus className="w-4 h-4" /> New Ledger
                            </button>
                        </div>
                        {filteredLedgers.length === 0 ? (
                            <p className="text-gray-500">
                                {entityFilter || officeFilter
                                    ? 'No client ledgers match the current entity/office filter.'
                                    : 'No client ledgers yet.'}
                            </p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b dark:border-gray-700">
                                            <th className="text-left py-3 px-4">Client</th>
                                            <th className="text-left py-3 px-4">Matter</th>
                                            <th className="text-left py-3 px-4">Trust Account</th>
                                            <th className="text-right py-3 px-4">Running</th>
                                            <th className="text-right py-3 px-4">Cleared</th>
                                            <th className="text-right py-3 px-4">Uncleared</th>
                                            <th className="text-right py-3 px-4">Available</th>
                                            <th className="text-right py-3 px-4">Hold</th>
                                            <th className="text-center py-3 px-4">Status</th>
                                            {canExport && <th className="text-center py-3 px-4">Export</th>}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {filteredLedgers.map(ledger => (
                                            <tr key={ledger.id} className="border-b dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-800">
                                                <td className="py-3 px-4 font-medium">
                                                    {getClientLabel(ledger.clientId)}
                                                </td>
                                                <td className="py-3 px-4">
                                                    {getMatterLabel(ledger.matterId)}
                                                </td>
                                                <td className="py-3 px-4">
                                                    {getAccountLabel(ledger.trustAccountId)}
                                                </td>
                                                <td className="py-3 px-4 text-right font-semibold">
                                                    {formatCurrency(Number(ledger.runningBalance))}
                                                </td>
                                                <td className="py-3 px-4 text-right">
                                                    {formatCurrency(Number(ledger.clearedBalance ?? ledger.runningBalance))}
                                                </td>
                                                <td className="py-3 px-4 text-right text-yellow-700">
                                                    {formatCurrency(Number(ledger.unclearedBalance ?? 0))}
                                                </td>
                                                <td className="py-3 px-4 text-right text-green-700 font-semibold">
                                                    {formatCurrency(Number(ledger.availableToDisburse ?? ledger.runningBalance))}
                                                </td>
                                                <td className="py-3 px-4 text-right text-red-700">
                                                    {formatCurrency(Number(ledger.holdAmount ?? 0))}
                                                </td>
                                                <td className="py-3 px-4 text-center">
                                                    <span className={`px-2 py-1 rounded-full text-xs ${ledger.status === 'ACTIVE'
                                                        ? 'bg-green-100 text-green-800'
                                                        : ledger.status === 'FROZEN'
                                                            ? 'bg-yellow-100 text-yellow-800'
                                                            : 'bg-gray-100 text-gray-800'
                                                        }`}>
                                                        {ledger.status}
                                                    </span>
                                                </td>
                                                {canExport && (
                                                    <td className="py-3 px-4 text-center">
                                                        <div className="inline-flex items-center gap-2">
                                                            <button
                                                                onClick={() => handleGenerateExport({ exportType: 'client_ledger', format: 'csv', trustAccountId: ledger.trustAccountId, clientTrustLedgerId: ledger.id })}
                                                                className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                            >
                                                                Ledger CSV
                                                            </button>
                                                            <button
                                                                onClick={() => handleGenerateExport({ exportType: 'client_ledger', format: 'json', trustAccountId: ledger.trustAccountId, clientTrustLedgerId: ledger.id })}
                                                                className="text-xs font-semibold text-gray-600 hover:text-gray-800"
                                                            >
                                                                JSON
                                                            </button>
                                                        </div>
                                                    </td>
                                                )}
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                )}

                {/* Transactions Tab */}
                {activeTab === 'transactions' && (
                    <div className="space-y-4">
                        <div className="flex items-center justify-between">
                            <h2 className="text-lg font-semibold">Transaction History</h2>
                            <div className="flex items-center gap-3">
                                {canExport && (
                                    <>
                                        <button
                                            onClick={() => handleGenerateExport({ exportType: 'approval_register', format: 'csv', trustAccountId: selectedAccount || undefined })}
                                            className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                        >
                                            Approvals CSV
                                        </button>
                                        <button
                                            onClick={() => handleGenerateExport({ exportType: 'approval_register', format: 'json', trustAccountId: selectedAccount || undefined })}
                                            className="text-xs font-semibold text-gray-600 hover:text-gray-800"
                                        >
                                            Approvals JSON
                                        </button>
                                    </>
                                )}
                                <div className="text-sm text-gray-500">
                                    {pendingTransactions} pending approval / {pendingClearanceCount} pending clearance
                                </div>
                            </div>
                        </div>
                        {filteredTransactions.length === 0 ? (
                            <p className="text-gray-500">
                                {entityFilter || officeFilter
                                    ? 'No trust transactions match the current entity/office filter.'
                                    : 'No transactions yet.'}
                            </p>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b dark:border-gray-700">
                                            <th className="text-left py-3 px-4">Date</th>
                                            <th className="text-left py-3 px-4">Type</th>
                                            <th className="text-left py-3 px-4">Description</th>
                                            <th className="text-left py-3 px-4">Account / Ledger</th>
                                            <th className="text-left py-3 px-4">Payor/Payee</th>
                                            <th className="text-right py-3 px-4">Amount</th>
                                            <th className="text-center py-3 px-4">Approval</th>
                                            <th className="text-center py-3 px-4">Clearing</th>
                                            <th className="text-center py-3 px-4">Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {filteredTransactions.map(tx => {
                                            const approvalItem = approvalQueueByTransactionId.get(tx.id);
                                            const nextRequirement = approvalItem?.requirements.find(requirement => requirement.satisfiedCount < requirement.requiredCount);

                                            return (
                                            <tr key={tx.id} className={`border-b dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-800 ${tx.isVoided ? 'opacity-50 line-through' : ''}`}>
                                                <td className="py-3 px-4 text-sm">{formatDate(tx.createdAt)}</td>
                                                <td className="py-3 px-4">
                                                    <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-xs ${tx.type === 'DEPOSIT' ? 'bg-green-100 text-green-800' :
                                                        tx.type === 'WITHDRAWAL' ? 'bg-red-100 text-red-800' :
                                                            tx.type === 'FEE_EARNED' ? 'bg-blue-100 text-blue-800' :
                                                                'bg-gray-100 text-gray-800'
                                                        }`}>
                                                        {tx.type === 'DEPOSIT' && <ArrowDownCircle className="w-3 h-3" />}
                                                        {tx.type === 'WITHDRAWAL' && <ArrowUpCircle className="w-3 h-3" />}
                                                        {tx.type}
                                                    </span>
                                                </td>
                                                <td className="py-3 px-4">
                                                    <div>{tx.description}</div>
                                                    {(tx.checkNumber || tx.wireReference) && (
                                                        <div className="text-xs text-gray-500">
                                                            {[tx.checkNumber ? `Check ${tx.checkNumber}` : null, tx.wireReference ? `Wire ${tx.wireReference}` : null]
                                                                .filter(Boolean)
                                                                .join(' / ')}
                                                        </div>
                                                    )}
                                                </td>
                                                <td className="py-3 px-4 text-sm text-gray-600">
                                                    <div>{getAccountLabel(tx.trustAccountId)}</div>
                                                    <div className="text-xs text-gray-500">
                                                        {tx.ledgerId
                                                            ? filteredLedgers.find(ledger => ledger.id === tx.ledgerId)
                                                                ? getLedgerLabel(filteredLedgers.find(ledger => ledger.id === tx.ledgerId)!)
                                                                : tx.ledgerId
                                                            : tx.matterId
                                                                ? getMatterLabel(tx.matterId)
                                                                : 'Account-level'}
                                                    </div>
                                                </td>
                                                <td className="py-3 px-4">{tx.payorPayee}</td>
                                                <td className="py-3 px-4 text-right font-semibold">
                                                    <span className={tx.type === 'DEPOSIT' ? 'text-green-600' : 'text-red-600'}>
                                                        {tx.type === 'DEPOSIT' ? '+' : '-'}{formatCurrency(Number(tx.amount))}
                                                    </span>
                                                </td>
                                                <td className="py-3 px-4 text-center">
                                                    <div className="flex flex-col items-center gap-2">
                                                        <span className={`px-2 py-1 rounded-full text-xs ${tx.status === 'APPROVED' ? 'bg-green-100 text-green-800' :
                                                            tx.status === 'PENDING' ? 'bg-yellow-100 text-yellow-800' :
                                                                tx.status === 'VOIDED' ? 'bg-gray-100 text-gray-800' :
                                                                    'bg-red-100 text-red-800'
                                                            }`}>
                                                            {tx.isVoided ? 'VOIDED' : tx.status}
                                                        </span>
                                                        {approvalItem && (
                                                            <div className="flex flex-wrap justify-center gap-1">
                                                                {approvalItem.requirements.map(requirement => (
                                                                    <span key={requirement.id} className={`px-2 py-1 rounded-full text-[11px] ${getApprovalRequirementTone(requirement)}`}>
                                                                        {requirement.requirementType.replace(/_/g, ' ')} {requirement.satisfiedCount}/{requirement.requiredCount}
                                                                    </span>
                                                                ))}
                                                            </div>
                                                        )}
                                                    </div>
                                                </td>
                                                <td className="py-3 px-4 text-center">
                                                    <span className={`px-2 py-1 rounded-full text-xs ${tx.clearingStatus === 'cleared'
                                                        ? 'bg-green-100 text-green-800'
                                                        : tx.clearingStatus === 'pending_clearance'
                                                            ? 'bg-yellow-100 text-yellow-800'
                                                            : tx.clearingStatus === 'returned'
                                                                ? 'bg-red-100 text-red-800'
                                                                : 'bg-gray-100 text-gray-700'
                                                        }`}>
                                                        {tx.clearingStatus ? tx.clearingStatus.replace(/_/g, ' ') : 'not applicable'}
                                                    </span>
                                                </td>
                                                <td className="py-3 px-4 text-center">
                                                    <div className="inline-flex flex-wrap items-center justify-center gap-2">
                                                        {!tx.isVoided && tx.status === 'PENDING' && canApprove && (
                                                            <>
                                                                <button
                                                                    onClick={() => approvalItem ? handleApproveRequirement(tx.id, nextRequirement?.requirementType) : handleApproveTransaction(tx.id)}
                                                                    className="text-green-600 hover:text-green-800"
                                                                    title="Approve"
                                                                >
                                                                    <CheckCircle2 className="w-4 h-4" />
                                                                </button>
                                                                <button
                                                                    onClick={() => handleRejectTransaction(tx.id)}
                                                                    className="text-red-600 hover:text-red-800"
                                                                    title="Reject"
                                                                >
                                                                    <XCircle className="w-4 h-4" />
                                                                </button>
                                                            </>
                                                        )}
                                                        {!tx.isVoided && tx.status === 'APPROVED' && tx.type === 'DEPOSIT' && tx.clearingStatus === 'pending_clearance' && canApprove && (
                                                            <>
                                                                <button
                                                                    onClick={() => handleClearDeposit(tx.id)}
                                                                    className="text-green-600 hover:text-green-800 text-xs font-semibold"
                                                                    title="Mark Cleared"
                                                                >
                                                                    Clear
                                                                </button>
                                                                <button
                                                                    onClick={() => handleReturnDeposit(tx.id)}
                                                                    className="text-red-600 hover:text-red-800 text-xs font-semibold"
                                                                    title="Return Deposit"
                                                                >
                                                                    Return
                                                                </button>
                                                            </>
                                                        )}
                                                        {!tx.isVoided && tx.status === 'APPROVED' && canVoid && (
                                                            <button
                                                                onClick={() => handleVoidTransaction(tx.id)}
                                                                className="text-red-600 hover:text-red-800"
                                                                title="Void"
                                                            >
                                                                <Ban className="w-4 h-4" />
                                                            </button>
                                                        )}
                                                    </div>
                                                </td>
                                            </tr>
                                        )})}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                )}

                {/* Reconciliation Tab */}
                {activeTab === 'reconciliation' && (
                    <div className="space-y-4">
                        <div className="flex items-start justify-between gap-4">
                            <div>
                                <h2 className="text-lg font-semibold">Three-Way Reconciliation Ops</h2>
                                <p className="text-sm text-gray-500 mt-1">Import bank evidence, add manual adjustments, then prepare and sign off the monthly packet.</p>
                                {secondaryDataLoading && (
                                    <p className="mt-2 text-xs font-medium text-gray-500">Advanced reconciliation data is still loading in the background.</p>
                                )}
                            </div>
                            <div className="flex gap-3 text-sm">
                                <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                    <div className="text-gray-500">Open Items</div>
                                    <div className="text-lg font-semibold">{openOutstandingItems.length}</div>
                                </div>
                                <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                    <div className="text-gray-500">Unsigned Packets</div>
                                    <div className="text-lg font-semibold">{unresolvedPackets}</div>
                                </div>
                                <div className={`rounded-xl border px-4 py-3 ${operationalCriticalCount > 0 ? 'border-red-200 bg-red-50' : operationalWarningCount > 0 ? 'border-yellow-200 bg-yellow-50' : 'border-gray-200 bg-gray-50'}`}>
                                    <div className="text-gray-500">Ops Alerts</div>
                                    <div className="text-lg font-semibold">{filteredOperationalAlerts.length}</div>
                                </div>
                            </div>
                        </div>

                        <div className="grid grid-cols-1 2xl:grid-cols-2 gap-4">
                            <div className="rounded-xl border border-gray-200 p-4 space-y-4">
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <h3 className="font-semibold">Canonical Packet Inspector</h3>
                                        <p className="text-sm text-gray-500 mt-1">Inspect packet state, packet-linked exceptions, and sign-off trail.</p>
                                    </div>
                                    {selectedPacket && (
                                        <span className={`px-2 py-1 rounded-full text-xs ${getPacketTone(selectedPacket.status)}`}>
                                            {selectedPacket.status.replace(/_/g, ' ')}
                                        </span>
                                    )}
                                </div>

                                {packetDetailLoading ? (
                                    <div className="text-sm text-gray-500">Loading packet detail...</div>
                                ) : !selectedPacket ? (
                                    <div className="text-sm text-gray-500">Select a packet from the queue to inspect it.</div>
                                ) : (
                                    <>
                                        <div className="rounded-lg bg-gray-50 px-4 py-4">
                                            <div className="flex items-start justify-between gap-3">
                                                <div>
                                                    <div className="font-semibold">{getAccountLabel(selectedPacket.trustAccountId)}</div>
                                                    <div className="text-xs text-gray-500 mt-1">{formatShortDate(selectedPacket.periodStart)} - {formatShortDate(selectedPacket.periodEnd)}</div>
                                                </div>
                                                <div className="flex flex-wrap gap-2">
                                                    {selectedPacket.isCanonical !== false && <span className="px-2 py-1 rounded-full text-xs bg-slate-100 text-slate-700">Canonical</span>}
                                                    {selectedPacket.statementImportId && <span className="px-2 py-1 rounded-full text-xs bg-blue-50 text-blue-700">Statement linked</span>}
                                                </div>
                                            </div>
                                            <div className="grid grid-cols-2 gap-3 mt-4 text-sm">
                                                <div className="rounded-lg border border-gray-200 bg-white px-3 py-3">
                                                    <div className="text-gray-500">Adjusted bank</div>
                                                    <div className="font-semibold mt-1">{formatCurrency(Number(selectedPacket.adjustedBankBalance))}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 bg-white px-3 py-3">
                                                    <div className="text-gray-500">Journal</div>
                                                    <div className="font-semibold mt-1">{formatCurrency(Number(selectedPacket.journalBalance))}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 bg-white px-3 py-3">
                                                    <div className="text-gray-500">Matched lines</div>
                                                    <div className="font-semibold mt-1">{selectedPacket.matchedStatementLineCount ?? activeStatementLines.filter(line => line.matchStatus === 'matched').length}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 bg-white px-3 py-3">
                                                    <div className="text-gray-500">Open lines</div>
                                                    <div className="font-semibold mt-1">{selectedPacket.unmatchedStatementLineCount ?? activeStatementLines.filter(line => line.matchStatus === 'unmatched').length}</div>
                                                </div>
                                            </div>
                                        </div>

                                        {reconciliationFocusAccount && (
                                            <div className="grid grid-cols-2 gap-3 text-sm">
                                                <div className="rounded-lg border border-gray-200 px-3 py-3">
                                                    <div className="text-gray-500">Cleared</div>
                                                    <div className="font-semibold mt-1">{formatCurrency(Number(reconciliationFocusAccount.clearedBalance ?? reconciliationFocusAccount.currentBalance))}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 px-3 py-3">
                                                    <div className="text-gray-500">Uncleared</div>
                                                    <div className={`font-semibold mt-1 ${Number(reconciliationFocusAccount.unclearedBalance ?? 0) > 0 ? 'text-amber-700' : 'text-gray-900'}`}>{formatCurrency(Number(reconciliationFocusAccount.unclearedBalance ?? 0))}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 px-3 py-3">
                                                    <div className="text-gray-500">Available</div>
                                                    <div className="font-semibold mt-1">{formatCurrency(Number(reconciliationFocusAccount.availableDisbursementCapacity ?? reconciliationFocusAccount.clearedBalance ?? reconciliationFocusAccount.currentBalance))}</div>
                                                </div>
                                                <div className="rounded-lg border border-gray-200 px-3 py-3">
                                                    <div className="text-gray-500">Pending clearance</div>
                                                    <div className="font-semibold mt-1">{reconciliationPendingClearanceCount}</div>
                                                </div>
                                            </div>
                                        )}

                                        <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
                                            <div>
                                                <div className="flex items-center justify-between mb-2">
                                                    <h4 className="text-sm font-semibold">Exception Register</h4>
                                                    <span className="text-xs text-gray-500">{activePacketOutstandingItems.length}</span>
                                                </div>
                                                <div className="space-y-2">
                                                    {activePacketOutstandingItems.length === 0 ? (
                                                        <div className="text-sm text-gray-500">No packet-linked exceptions.</div>
                                                    ) : activePacketOutstandingItems.slice(0, 6).map(item => (
                                                        <div key={item.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                            <div className="font-medium text-sm">{item.itemType.replace(/_/g, ' ')}</div>
                                                            <div className="text-xs text-gray-500 mt-1">{item.reference || 'No reference'} / {formatShortDate(item.occurredAt || item.createdAt)}</div>
                                                        </div>
                                                    ))}
                                                </div>
                                            </div>
                                            <div>
                                                <div className="flex items-center justify-between mb-2">
                                                    <h4 className="text-sm font-semibold">Sign-off Trail</h4>
                                                    <span className="text-xs text-gray-500">{selectedPacketSignoffs.length}</span>
                                                </div>
                                                <div className="space-y-2">
                                                    {selectedPacketSignoffs.length === 0 ? (
                                                        <div className="text-sm text-gray-500">No sign-offs recorded yet.</div>
                                                    ) : selectedPacketSignoffs.map(signoff => (
                                                        <div key={signoff.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                            <div className="font-medium text-sm">{signoff.signedBy}</div>
                                                            <div className="text-xs text-gray-500 mt-1">{signoff.signerRole || 'Signer'} / {formatDate(signoff.signedAt)}</div>
                                                        </div>
                                                    ))}
                                                </div>
                                            </div>
                                        </div>
                                    </>
                                )}
                            </div>

                            <div className="rounded-xl border border-gray-200 p-4 space-y-4">
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <h3 className="font-semibold">Statement Matching Workspace</h3>
                                        <p className="text-sm text-gray-500 mt-1">Resolve statement rows before month close sign-off.</p>
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <button type="button" onClick={() => selectedStatementImportId && loadStatementLineWorkspace(selectedStatementImportId)} className="btn-secondary" disabled={!selectedStatementImportId || statementLinesLoading}>Refresh</button>
                                        <button type="button" onClick={handleRunStatementMatching} className="btn-primary" disabled={!selectedStatementImportId || matchingRunLoading}>{matchingRunLoading ? 'Matching...' : 'Run Auto-Match'}</button>
                                    </div>
                                </div>

                                <div className="grid grid-cols-1 xl:grid-cols-[260px_1fr] gap-4">
                                    <div className="space-y-3">
                                        <select value={selectedStatementImportId} onChange={e => handleSelectStatementImport(e.target.value)} className="input w-full">
                                            <option value="">Select statement import...</option>
                                            {filteredStatementImports.slice().sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime()).map(statement => (
                                                <option key={statement.id} value={statement.id}>{getAccountLabel(statement.trustAccountId)} / {formatShortDate(statement.periodEnd)}</option>
                                            ))}
                                        </select>

                                        {selectedStatementEvidence && (
                                            <div className="rounded-lg bg-gray-50 px-4 py-4 text-sm">
                                                <div className="flex items-start justify-between gap-3">
                                                    <div>
                                                        <div className="font-semibold">{getAccountLabel(selectedStatementEvidence.trustAccountId)}</div>
                                                        <div className="text-gray-500 mt-1">{formatShortDate(selectedStatementEvidence.periodStart)} - {formatShortDate(selectedStatementEvidence.periodEnd)}</div>
                                                    </div>
                                                    <span className={`rounded-full px-2 py-1 text-[11px] font-semibold ${getStatementImportTone(selectedStatementEvidence.status)}`}>
                                                        {selectedStatementEvidence.status.replace(/_/g, ' ')}
                                                    </span>
                                                </div>
                                                <div className="mt-3 space-y-2">
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Ending balance</span><span className="font-semibold">{formatCurrency(Number(selectedStatementEvidence.statementEndingBalance))}</span></div>
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Rows</span><span className="font-semibold">{selectedStatementEvidence.lineCount}</span></div>
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Matched</span><span className="font-semibold">{activeStatementLines.filter(line => line.matchStatus === 'matched').length}</span></div>
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Needs review</span><span className="font-semibold">{activeStatementLines.filter(line => line.matchStatus === 'unmatched').length}</span></div>
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Source file</span><span className="font-semibold">{selectedStatementEvidence.sourceFileName || 'Manual evidence'}</span></div>
                                                    <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Hash</span><span className="font-semibold">{shortenFingerprint(selectedStatementEvidence.sourceFileHash || selectedStatementEvidence.importFingerprint)}</span></div>
                                                    {selectedStatementEvidence.sourceEvidenceKey && (
                                                        <div className="flex items-center justify-between gap-3"><span className="text-gray-500">Evidence key</span><span className="font-semibold">{selectedStatementEvidence.sourceEvidenceKey}</span></div>
                                                    )}
                                                    {selectedEvidenceFile && (
                                                        <div className="rounded-lg border border-blue-200 bg-blue-50 px-3 py-3 text-xs text-blue-900">
                                                            <div className="font-semibold">Evidence registry</div>
                                                            <div className="mt-1">File: {selectedEvidenceFile.fileName}</div>
                                                            <div>Registry status: {selectedEvidenceFile.status.replace(/_/g, ' ')}</div>
                                                            <div>Checksum: {shortenFingerprint(selectedEvidenceFile.fileHash)}</div>
                                                        </div>
                                                    )}
                                                    {selectedParserRun && (
                                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-3 text-xs text-gray-700">
                                                            <div className="font-semibold">Parser run</div>
                                                            <div className="mt-1">Parser: {selectedParserRun.parserKey}</div>
                                                            <div>Status: {selectedParserRun.status.replace(/_/g, ' ')}</div>
                                                            <div>Started: {formatDate(selectedParserRun.startedAt)}</div>
                                                        </div>
                                                    )}
                                                    {selectedStatementEvidence.duplicateOfStatementImportId && (
                                                        <div className="rounded-lg border border-orange-200 bg-orange-50 px-3 py-2 text-xs text-orange-800">
                                                            Duplicate of import {shortenFingerprint(selectedStatementEvidence.duplicateOfStatementImportId)}. Matching is intentionally skipped for this evidence row.
                                                        </div>
                                                    )}
                                                    {selectedStatementEvidence.supersededByStatementImportId && (
                                                        <div className="rounded-lg border border-gray-200 bg-gray-100 px-3 py-2 text-xs text-gray-700">
                                                            Superseded by import {shortenFingerprint(selectedStatementEvidence.supersededByStatementImportId)} on {selectedStatementEvidence.supersededAt ? formatShortDate(selectedStatementEvidence.supersededAt) : 'later version'}.
                                                        </div>
                                                    )}
                                                </div>
                                            </div>
                                        )}

                                        {activePacketTemplate && (
                                            <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-4 text-sm">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div className="font-semibold text-slate-900">Jurisdiction packet template</div>
                                                    <span className="rounded-full bg-white px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-700">
                                                        v{activePacketTemplate.versionNumber}
                                                    </span>
                                                </div>
                                                <div className="mt-2 text-xs text-slate-700">{activePacketTemplate.name || activePacketTemplate.templateKey}</div>
                                                <div className="mt-3 space-y-2 text-xs text-slate-700">
                                                    <div>Required sections: {activePacketTemplate.requiredSections.length}</div>
                                                    <div>Required attestations: {activePacketTemplate.requiredAttestations.filter(item => item.required).length}</div>
                                                    <div>Disclosure blocks: {activePacketTemplate.disclosureBlocks.join(', ') || 'None'}</div>
                                                </div>
                                            </div>
                                        )}

                                        {reconciliationOperationalAlerts.length > 0 && (
                                            <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-4 text-sm">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div className="font-semibold text-amber-900">Operational alerts on this account</div>
                                                    <span className="rounded-full bg-white/70 px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-amber-800">
                                                        {reconciliationOperationalAlerts.length} open
                                                    </span>
                                                </div>
                                                <div className="mt-3 space-y-2">
                                                    {reconciliationOperationalAlerts.map(alert => (
                                                        <div key={`${alert.alertType}-${alert.relatedEntityId || alert.periodEnd || alert.openedAt}`} className="rounded-lg border border-amber-200 bg-white/70 px-3 py-3">
                                                            <div className="flex items-center justify-between gap-3">
                                                                <div className="font-medium text-amber-900">{alert.title}</div>
                                                                <div className="flex items-center gap-2">
                                                                    <span className="text-[11px] font-semibold uppercase tracking-[0.16em] text-amber-700">{alert.severity}</span>
                                                                    <span className={`rounded-full border px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.16em] ${getOperationalWorkflowTone(alert.workflowStatus)}`}>
                                                                        {alert.workflowStatus || 'open'}
                                                                    </span>
                                                                </div>
                                                            </div>
                                                            <div className="mt-1 text-xs text-amber-900/80">{alert.summary}</div>
                                                            {alert.actionHint && <div className="mt-2 text-xs font-medium text-amber-900">{alert.actionHint}</div>}
                                                        </div>
                                                    ))}
                                                </div>
                                            </div>
                                        )}
                                    </div>

                                    <div className="rounded-lg border border-gray-200 overflow-hidden">
                                        {statementLinesLoading ? (
                                            <div className="p-4 text-sm text-gray-500">Loading statement lines...</div>
                                        ) : activeStatementLines.length === 0 ? (
                                            <div className="p-4 text-sm text-gray-500">Select a statement import to review line-level matches.</div>
                                        ) : (
                                            <div className="overflow-auto max-h-[680px]">
                                                <table className="w-full text-sm">
                                                    <thead className="bg-gray-50">
                                                        <tr className="border-b border-gray-200">
                                                            <th className="text-left py-3 px-3">Line</th>
                                                            <th className="text-right py-3 px-3">Amount</th>
                                                            <th className="text-center py-3 px-3">Status</th>
                                                            <th className="text-left py-3 px-3">Transaction</th>
                                                            <th className="text-center py-3 px-3">Actions</th>
                                                        </tr>
                                                    </thead>
                                                    <tbody>
                                                        {activeStatementLines.map(line => {
                                                            const candidates = getStatementLineCandidates(line);
                                                            const selectedMatchId = lineMatchSelections[line.id] ?? line.matchedTrustTransactionId ?? '';
                                                            const isBusy = lineResolutionLoadingId === line.id;
                                                            return (
                                                                <tr key={line.id} className="border-b border-gray-100 align-top">
                                                                    <td className="py-3 px-3">
                                                                        <div className="font-medium">{line.description || line.counterparty || 'Bank line'}</div>
                                                                        <div className="text-xs text-gray-500 mt-1">{formatShortDate(line.postedAt)} / {line.reference || line.checkNumber || line.externalLineId || 'No reference'}</div>
                                                                    </td>
                                                                    <td className="py-3 px-3 text-right font-semibold">{line.amount >= 0 ? '+' : '-'}{formatCurrency(Math.abs(Number(line.amount)))}</td>
                                                                    <td className="py-3 px-3 text-center">
                                                                        <span className={`px-2 py-1 rounded-full text-xs ${getStatementLineTone(line.matchStatus)}`}>{line.matchStatus}</span>
                                                                    </td>
                                                                    <td className="py-3 px-3 min-w-[220px]">
                                                                        <select value={selectedMatchId} onChange={e => setLineMatchSelections(prev => ({ ...prev, [line.id]: e.target.value }))} className="input w-full">
                                                                            <option value="">Select transaction...</option>
                                                                            {candidates.map(tx => (
                                                                                <option key={tx.id} value={tx.id}>{formatShortDate(tx.createdAt)} / {tx.type} / {formatCurrency(Number(tx.amount))}</option>
                                                                            ))}
                                                                        </select>
                                                                    </td>
                                                                    <td className="py-3 px-3">
                                                                        <div className="flex flex-col items-center gap-2">
                                                                            <button type="button" onClick={() => handleResolveStatementLine(line, 'match')} className="text-xs font-semibold text-primary-600 hover:text-primary-700 disabled:text-gray-400" disabled={isBusy}>Match</button>
                                                                            <button type="button" onClick={() => handleResolveStatementLine(line, 'ignore')} className="text-xs font-semibold text-gray-600 hover:text-gray-800 disabled:text-gray-400" disabled={isBusy}>Ignore</button>
                                                                            <button type="button" onClick={() => handleResolveStatementLine(line, 'reject')} className="text-xs font-semibold text-red-600 hover:text-red-700 disabled:text-gray-400" disabled={isBusy}>Reject</button>
                                                                            {(line.matchStatus === 'matched' || line.matchStatus === 'ignored' || line.matchStatus === 'rejected') && (
                                                                                <button type="button" onClick={() => handleResolveStatementLine(line, 'unmatch')} className="text-xs font-semibold text-red-600 hover:text-red-700 disabled:text-gray-400" disabled={isBusy}>Unmatch</button>
                                                                            )}
                                                                        </div>
                                                                    </td>
                                                                </tr>
                                                            );
                                                        })}
                                                    </tbody>
                                                </table>
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div className="grid grid-cols-1 xl:grid-cols-2 2xl:grid-cols-5 gap-4">
                            <form onSubmit={handleRegisterEvidenceFile} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <h3 className="font-semibold">Register Evidence File</h3>
                                <select value={evidenceFileForm.trustAccountId} onChange={e => setEvidenceFileForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full" required>
                                    <option value="">Select trust account...</option>
                                    {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                </select>
                                <div className="grid grid-cols-2 gap-3">
                                    <input type="date" value={evidenceFileForm.periodStart} onChange={e => setEvidenceFileForm(prev => ({ ...prev, periodStart: e.target.value }))} className="input w-full" required />
                                    <input type="date" value={evidenceFileForm.periodEnd} onChange={e => setEvidenceFileForm(prev => ({ ...prev, periodEnd: e.target.value }))} className="input w-full" required />
                                </div>
                                <input type="text" value={evidenceFileForm.fileName} onChange={e => setEvidenceFileForm(prev => ({ ...prev, fileName: e.target.value }))} className="input w-full" placeholder="Statement file name" required />
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={evidenceFileForm.fileHash} onChange={e => setEvidenceFileForm(prev => ({ ...prev, fileHash: e.target.value }))} className="input w-full" placeholder="SHA256 / manifest hash" required />
                                    <input type="text" value={evidenceFileForm.evidenceKey} onChange={e => setEvidenceFileForm(prev => ({ ...prev, evidenceKey: e.target.value }))} className="input w-full" placeholder="Evidence storage key" />
                                </div>
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={evidenceFileForm.contentType} onChange={e => setEvidenceFileForm(prev => ({ ...prev, contentType: e.target.value }))} className="input w-full" placeholder="Content type" />
                                    <input type="number" value={evidenceFileForm.fileSizeBytes} onChange={e => setEvidenceFileForm(prev => ({ ...prev, fileSizeBytes: e.target.value }))} className="input w-full" placeholder="File size bytes" />
                                </div>
                                <label className="flex items-center gap-2 text-sm text-gray-600">
                                    <input type="checkbox" checked={evidenceFileForm.allowDuplicateRegistration} onChange={e => setEvidenceFileForm(prev => ({ ...prev, allowDuplicateRegistration: e.target.checked }))} />
                                    Keep duplicate evidence row instead of blocking it
                                </label>
                                <textarea value={evidenceFileForm.notes} onChange={e => setEvidenceFileForm(prev => ({ ...prev, notes: e.target.value }))} className="input w-full min-h-[88px]" placeholder="Operator notes / source details" />
                                <button type="submit" className="btn-secondary w-full">Register Evidence</button>
                            </form>

                            <form onSubmit={handleCreateParserRun} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <h3 className="font-semibold">Run Parser Import</h3>
                                <select value={parserRunForm.trustAccountId} onChange={e => setParserRunForm(prev => ({ ...prev, trustAccountId: e.target.value, trustEvidenceFileId: '' }))} className="input w-full" required>
                                    <option value="">Select trust account...</option>
                                    {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                </select>
                                <select value={parserRunForm.trustEvidenceFileId} onChange={e => setParserRunForm(prev => ({ ...prev, trustEvidenceFileId: e.target.value }))} className="input w-full" required>
                                    <option value="">Select evidence file...</option>
                                    {filteredEvidenceFiles.filter(file => file.trustAccountId === parserRunForm.trustAccountId).sort((a, b) => new Date(b.registeredAt).getTime() - new Date(a.registeredAt).getTime()).map(file => (
                                        <option key={file.id} value={file.id}>{file.fileName} / {formatShortDate(file.periodEnd)} / {file.status}</option>
                                    ))}
                                </select>
                                <input type="number" step="0.01" value={parserRunForm.statementEndingBalance} onChange={e => setParserRunForm(prev => ({ ...prev, statementEndingBalance: e.target.value }))} className="input w-full" placeholder="Statement ending balance" required />
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={parserRunForm.parserKey} onChange={e => setParserRunForm(prev => ({ ...prev, parserKey: e.target.value }))} className="input w-full" placeholder="Parser key" />
                                    <input type="text" value={parserRunForm.source} onChange={e => setParserRunForm(prev => ({ ...prev, source: e.target.value }))} className="input w-full" placeholder="Parser source" />
                                </div>
                                <label className="flex items-center gap-2 text-sm text-gray-600">
                                    <input type="checkbox" checked={parserRunForm.allowDuplicateImport} onChange={e => setParserRunForm(prev => ({ ...prev, allowDuplicateImport: e.target.checked }))} />
                                    Allow parser to emit duplicate statement import lineage
                                </label>
                                <textarea value={parserRunForm.linesJson} onChange={e => setParserRunForm(prev => ({ ...prev, linesJson: e.target.value }))} className="input w-full min-h-[88px]" placeholder='Normalized lines JSON [{"postedAt":"2026-04-30T00:00:00Z","description":"Check 105","amount":125.00}]' />
                                <button type="submit" className="btn-primary w-full">Run Parser</button>
                            </form>

                            <form onSubmit={handleUpsertPacketTemplate} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <div className="flex items-start justify-between gap-3">
                                    <div>
                                        <h3 className="font-semibold">Packet Template</h3>
                                        <p className="mt-1 text-xs text-gray-500">Control jurisdiction close sections and required attestations without code changes.</p>
                                    </div>
                                    {activePacketTemplate && (
                                        <button
                                            type="button"
                                            onClick={() => setPacketTemplateForm({
                                                policyKey: activePacketTemplate.policyKey,
                                                jurisdiction: activePacketTemplate.jurisdiction,
                                                accountType: activePacketTemplate.accountType,
                                                templateKey: activePacketTemplate.templateKey,
                                                name: activePacketTemplate.name || '',
                                                versionNumber: String(activePacketTemplate.versionNumber),
                                                requiredSectionsText: activePacketTemplate.requiredSections.join(', '),
                                                disclosureBlocksText: activePacketTemplate.disclosureBlocks.join(', '),
                                                requiredAttestationsText: JSON.stringify(activePacketTemplate.requiredAttestations, null, 2),
                                                renderingProfileJson: activePacketTemplate.renderingProfileJson || '',
                                                metadataJson: activePacketTemplate.metadataJson || ''
                                            })}
                                            className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                        >
                                            Load Active
                                        </button>
                                    )}
                                </div>
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={packetTemplateForm.policyKey} onChange={e => setPacketTemplateForm(prev => ({ ...prev, policyKey: e.target.value }))} className="input w-full" placeholder="Policy key" required />
                                    <input type="text" value={packetTemplateForm.jurisdiction} onChange={e => setPacketTemplateForm(prev => ({ ...prev, jurisdiction: e.target.value.toUpperCase() }))} className="input w-full" placeholder="Jurisdiction" required />
                                </div>
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <select value={packetTemplateForm.accountType} onChange={e => setPacketTemplateForm(prev => ({ ...prev, accountType: e.target.value }))} className="input w-full">
                                        <option value="all">All account types</option>
                                        <option value="iolta">IOLTA</option>
                                        <option value="non_iolta">Non-IOLTA</option>
                                    </select>
                                    <input type="number" min="1" value={packetTemplateForm.versionNumber} onChange={e => setPacketTemplateForm(prev => ({ ...prev, versionNumber: e.target.value }))} className="input w-full" placeholder="Version" required />
                                </div>
                                <input type="text" value={packetTemplateForm.templateKey} onChange={e => setPacketTemplateForm(prev => ({ ...prev, templateKey: e.target.value }))} className="input w-full" placeholder="Template key" required />
                                <input type="text" value={packetTemplateForm.name} onChange={e => setPacketTemplateForm(prev => ({ ...prev, name: e.target.value }))} className="input w-full" placeholder="Template display name" />
                                <textarea value={packetTemplateForm.requiredSectionsText} onChange={e => setPacketTemplateForm(prev => ({ ...prev, requiredSectionsText: e.target.value }))} className="input w-full min-h-[72px]" placeholder="Required sections, comma separated" />
                                <textarea value={packetTemplateForm.disclosureBlocksText} onChange={e => setPacketTemplateForm(prev => ({ ...prev, disclosureBlocksText: e.target.value }))} className="input w-full min-h-[72px]" placeholder="Disclosure blocks, comma separated" />
                                <div className="rounded-xl border border-gray-200 bg-gray-50 p-3">
                                    <div className="flex items-start justify-between gap-3">
                                        <div>
                                            <div className="text-sm font-semibold text-gray-900">Required Attestations</div>
                                            <p className="mt-1 text-xs text-gray-500">JSON tabanli alanlar varsayilan olarak gizli. Gerektiginde advanced editoru acabilirsiniz.</p>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => setShowPacketTemplateAdvanced(prev => !prev)}
                                            className="rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-xs font-semibold text-gray-700 transition hover:bg-gray-50"
                                        >
                                            {showPacketTemplateAdvanced ? 'Hide Advanced' : 'Edit JSON'}
                                        </button>
                                    </div>
                                    {requiredAttestationPreview.error ? (
                                        <div className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
                                            {requiredAttestationPreview.error}
                                        </div>
                                    ) : requiredAttestationPreview.items.length === 0 ? (
                                        <div className="mt-3 text-sm text-gray-500">No required attestations configured.</div>
                                    ) : (
                                        <div className="mt-3 space-y-2">
                                            {requiredAttestationPreview.items.map(attestation => (
                                                <div key={attestation.key} className="rounded-lg border border-white bg-white px-3 py-2 text-sm">
                                                    <div className="flex items-center justify-between gap-3">
                                                        <span className="font-medium text-gray-900">{attestation.label}</span>
                                                        <span className="rounded-full bg-slate-100 px-2 py-1 text-[11px] font-semibold uppercase tracking-[0.08em] text-slate-700">
                                                            {attestation.role.replace(/_/g, ' ')}
                                                        </span>
                                                    </div>
                                                    <div className="mt-1 text-xs text-gray-500">
                                                        Key: {attestation.key} {attestation.required ? '• required' : '• optional'}
                                                    </div>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                                {showPacketTemplateAdvanced && (
                                    <div className="space-y-3 rounded-xl border border-dashed border-gray-300 bg-gray-50/80 p-3">
                                        <textarea value={packetTemplateForm.requiredAttestationsText} onChange={e => setPacketTemplateForm(prev => ({ ...prev, requiredAttestationsText: e.target.value }))} className="input w-full min-h-[120px] font-mono text-xs" placeholder='Required attestations JSON [{"key":"reviewed_three_way_reconciliation","label":"...","role":"reviewer","required":true}]' />
                                        <textarea value={packetTemplateForm.renderingProfileJson} onChange={e => setPacketTemplateForm(prev => ({ ...prev, renderingProfileJson: e.target.value }))} className="input w-full min-h-[72px] font-mono text-xs" placeholder="Optional rendering profile JSON" />
                                        <textarea value={packetTemplateForm.metadataJson} onChange={e => setPacketTemplateForm(prev => ({ ...prev, metadataJson: e.target.value }))} className="input w-full min-h-[72px] font-mono text-xs" placeholder="Optional template metadata JSON" />
                                    </div>
                                )}
                                <button type="submit" className="btn-secondary w-full">Save Template</button>
                            </form>

                            <form onSubmit={handleImportStatement} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <h3 className="font-semibold">Import Statement</h3>
                                <select value={statementImportForm.trustAccountId} onChange={e => setStatementImportForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full" required>
                                    <option value="">Select trust account...</option>
                                    {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                </select>
                                <div className="grid grid-cols-2 gap-3">
                                    <input type="date" value={statementImportForm.periodStart} onChange={e => setStatementImportForm(prev => ({ ...prev, periodStart: e.target.value }))} className="input w-full" required />
                                    <input type="date" value={statementImportForm.periodEnd} onChange={e => setStatementImportForm(prev => ({ ...prev, periodEnd: e.target.value }))} className="input w-full" required />
                                </div>
                                <input type="number" step="0.01" value={statementImportForm.statementEndingBalance} onChange={e => setStatementImportForm(prev => ({ ...prev, statementEndingBalance: e.target.value }))} className="input w-full" placeholder="Statement ending balance" required />
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={statementImportForm.sourceFileName} onChange={e => setStatementImportForm(prev => ({ ...prev, sourceFileName: e.target.value }))} className="input w-full" placeholder="Optional source file name" />
                                    <input type="text" value={statementImportForm.sourceFileHash} onChange={e => setStatementImportForm(prev => ({ ...prev, sourceFileHash: e.target.value }))} className="input w-full" placeholder="Optional source SHA/hash" />
                                </div>
                                <input type="text" value={statementImportForm.sourceEvidenceKey} onChange={e => setStatementImportForm(prev => ({ ...prev, sourceEvidenceKey: e.target.value }))} className="input w-full" placeholder="Optional evidence key / storage reference" />
                                <label className="flex items-center gap-2 text-sm text-gray-600">
                                    <input type="checkbox" checked={statementImportForm.allowDuplicateImport} onChange={e => setStatementImportForm(prev => ({ ...prev, allowDuplicateImport: e.target.checked }))} />
                                    Allow import even if fingerprint/hash matches an existing statement version
                                </label>
                                <textarea value={statementImportForm.linesJson} onChange={e => setStatementImportForm(prev => ({ ...prev, linesJson: e.target.value }))} className="input w-full min-h-[88px]" placeholder='Optional lines JSON [{"postedAt":"2026-04-30T00:00:00Z","description":"Check 105","amount":125.00}]' />
                                <button type="submit" className="btn-primary w-full">Import Statement</button>
                            </form>

                            <form onSubmit={handleCreateOutstandingItem} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <h3 className="font-semibold">Manual Outstanding Item</h3>
                                <select value={outstandingItemForm.trustAccountId} onChange={e => setOutstandingItemForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full" required>
                                    <option value="">Select trust account...</option>
                                    {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                </select>
                                <div className="grid grid-cols-2 gap-3">
                                    <input type="date" value={outstandingItemForm.periodStart} onChange={e => setOutstandingItemForm(prev => ({ ...prev, periodStart: e.target.value }))} className="input w-full" required />
                                    <input type="date" value={outstandingItemForm.periodEnd} onChange={e => setOutstandingItemForm(prev => ({ ...prev, periodEnd: e.target.value }))} className="input w-full" required />
                                </div>
                                <div className="grid grid-cols-2 gap-3">
                                    <select value={outstandingItemForm.itemType} onChange={e => setOutstandingItemForm(prev => ({ ...prev, itemType: e.target.value }))} className="input w-full">
                                        <option value="other_adjustment">Other adjustment</option>
                                        <option value="outstanding_check">Outstanding check</option>
                                        <option value="deposit_in_transit">Deposit in transit</option>
                                        <option value="bank_fee">Bank fee</option>
                                    </select>
                                    <select value={outstandingItemForm.impactDirection} onChange={e => setOutstandingItemForm(prev => ({ ...prev, impactDirection: e.target.value }))} className="input w-full">
                                        <option value="decrease_bank">Decrease bank</option>
                                        <option value="increase_bank">Increase bank</option>
                                    </select>
                                </div>
                                <input type="number" step="0.01" value={outstandingItemForm.amount} onChange={e => setOutstandingItemForm(prev => ({ ...prev, amount: e.target.value }))} className="input w-full" placeholder="Amount" required />
                                <input type="text" value={outstandingItemForm.reference} onChange={e => setOutstandingItemForm(prev => ({ ...prev, reference: e.target.value }))} className="input w-full" placeholder="Reference / check number" />
                                <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                    <input type="text" value={outstandingItemForm.reasonCode} onChange={e => setOutstandingItemForm(prev => ({ ...prev, reasonCode: e.target.value }))} className="input w-full" placeholder="Reason code (e.g. bank_fee, stale_check)" />
                                    <input type="text" value={outstandingItemForm.attachmentEvidenceKey} onChange={e => setOutstandingItemForm(prev => ({ ...prev, attachmentEvidenceKey: e.target.value }))} className="input w-full" placeholder="Attachment evidence key" />
                                </div>
                                <textarea value={outstandingItemForm.description} onChange={e => setOutstandingItemForm(prev => ({ ...prev, description: e.target.value }))} className="input w-full min-h-[88px]" placeholder="Description / reviewer note" />
                                <button type="submit" className="btn-secondary w-full">Add Item</button>
                            </form>

                            <form onSubmit={handleGeneratePacket} className="rounded-xl border border-gray-200 p-4 space-y-3">
                                <h3 className="font-semibold">Prepare Packet</h3>
                                <select value={packetForm.trustAccountId} onChange={e => setPacketForm(prev => ({ ...prev, trustAccountId: e.target.value, statementImportId: '' }))} className="input w-full" required>
                                    <option value="">Select trust account...</option>
                                    {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                </select>
                                <div className="grid grid-cols-2 gap-3">
                                    <input type="date" value={packetForm.periodStart} onChange={e => setPacketForm(prev => ({ ...prev, periodStart: e.target.value }))} className="input w-full" required />
                                    <input type="date" value={packetForm.periodEnd} onChange={e => setPacketForm(prev => ({ ...prev, periodEnd: e.target.value }))} className="input w-full" required />
                                </div>
                                <select value={packetForm.statementImportId} onChange={e => setPacketForm(prev => ({ ...prev, statementImportId: e.target.value }))} className="input w-full">
                                    <option value="">Use direct statement ending balance...</option>
                                    {filteredStatementImports.filter(statement => statement.trustAccountId === packetForm.trustAccountId).sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime()).map(statement => (
                                        <option key={statement.id} value={statement.id}>{new Date(statement.periodEnd).toLocaleDateString('en-US')} / {formatCurrency(Number(statement.statementEndingBalance))}</option>
                                    ))}
                                </select>
                                {!packetForm.statementImportId && <input type="number" step="0.01" value={packetForm.statementEndingBalance} onChange={e => setPacketForm(prev => ({ ...prev, statementEndingBalance: e.target.value }))} className="input w-full" placeholder="Direct statement ending balance" />}
                                <button type="submit" className="btn-primary w-full">Prepare Packet</button>
                            </form>
                        </div>

                        <div className="rounded-xl border border-gray-200 p-4">
                            <div className="flex items-start justify-between gap-4 mb-4">
                                <div>
                                    <h3 className="font-semibold">Recovery And Compliance Bundle</h3>
                                    <p className="text-sm text-gray-500 mt-1">Run explicit as-of projection diagnostics, regenerate canonical packets, and produce a regulator-ready bundle without touching the hot trust read path.</p>
                                </div>
                                <div className="text-xs text-gray-500">
                                    Selected packet: {selectedPacket ? `${getAccountLabel(selectedPacket.trustAccountId)} / ${formatShortDate(selectedPacket.periodEnd)}` : 'none'}
                                </div>
                            </div>

                            <div className="grid grid-cols-1 xl:grid-cols-4 gap-4">
                                <div className="rounded-xl border border-gray-200 bg-gray-50/70 p-4 space-y-3">
                                    <div className="flex items-start justify-between gap-3">
                                        <div>
                                            <div className="font-semibold">Close Automation</div>
                                            <div className="text-xs text-gray-500 mt-1">Forecast the next close, create draft bundle scope, and surface overdue trust periods before month-close stalls.</div>
                                        </div>
                                        <button
                                            type="button"
                                            onClick={() => loadCloseForecast(true)}
                                            disabled={closeForecastBusy}
                                            className="rounded-xl border border-gray-200 bg-white px-3 py-2 text-xs font-semibold text-gray-700 transition hover:bg-gray-50 disabled:opacity-60"
                                        >
                                            {closeForecastBusy ? 'Syncing...' : 'Sync Forecast'}
                                        </button>
                                    </div>

                                    <div className="grid grid-cols-2 gap-2 text-sm">
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                            <div className="text-gray-500 text-[11px] uppercase tracking-[0.2em]">Ready</div>
                                            <div className="text-lg font-semibold text-emerald-700">{closeForecastSummary?.readyCount || 0}</div>
                                        </div>
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                            <div className="text-gray-500 text-[11px] uppercase tracking-[0.2em]">At Risk</div>
                                            <div className="text-lg font-semibold text-amber-700">{closeForecastSummary?.atRiskCount || 0}</div>
                                        </div>
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                            <div className="text-gray-500 text-[11px] uppercase tracking-[0.2em]">Blocked</div>
                                            <div className="text-lg font-semibold text-red-700">{closeForecastSummary?.blockedCount || 0}</div>
                                        </div>
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-2">
                                            <div className="text-gray-500 text-[11px] uppercase tracking-[0.2em]">Overdue</div>
                                            <div className="text-lg font-semibold text-red-700">{closeForecastSummary?.overdueCount || 0}</div>
                                        </div>
                                    </div>

                                    <div className="rounded-lg border border-dashed border-gray-200 bg-white/80 px-3 py-3 text-xs text-gray-600 space-y-1">
                                        <div>Draft-eligible bundles: <span className="font-semibold text-gray-900">{closeForecastSummary?.draftBundleEligibleCount || 0}</span></div>
                                        <div>Reminder due: <span className="font-semibold text-gray-900">{closeForecastSummary?.reminderDueCount || 0}</span></div>
                                        <div>Snapshots: <span className="font-semibold text-gray-900">{closeForecastSummary?.totalCount || 0}</span></div>
                                        <div>Generated: <span className="font-semibold text-gray-900">{closeForecastSummary?.generatedAtUtc ? formatDate(closeForecastSummary.generatedAtUtc) : 'n/a'}</span></div>
                                        {closeForecastSyncResult && (
                                            <div className="pt-2 border-t border-gray-200">
                                                Last sync: {closeForecastSyncResult.snapshotCount} snapshot(s), {closeForecastSyncResult.draftBundleCount} draft bundle(s), {closeForecastSyncResult.reminderCount} reminder(s), {closeForecastSyncResult.escalatedCount} escalation(s)
                                            </div>
                                        )}
                                    </div>

                                    <div className="space-y-2 max-h-[370px] overflow-y-auto pr-1">
                                        {filteredCloseForecasts.length === 0 ? (
                                            <div className="rounded-lg border border-dashed border-gray-200 bg-white px-3 py-4 text-sm text-gray-500">
                                                No close forecast snapshots yet. Run sync to generate readiness state for active trust accounts.
                                            </div>
                                        ) : (
                                            filteredCloseForecasts.slice(0, 6).map(snapshot => (
                                                <div key={snapshot.id} className="rounded-lg border border-gray-200 bg-white px-3 py-3 space-y-2">
                                                    <div className="flex items-start justify-between gap-3">
                                                        <div>
                                                            <div className="font-medium text-sm text-gray-900">{snapshot.trustAccountName || getAccountLabel(snapshot.trustAccountId)}</div>
                                                            <div className="text-xs text-gray-500">
                                                                {formatShortDate(snapshot.periodStart)} - {formatShortDate(snapshot.periodEnd)} • due {formatShortDate(snapshot.closeDueAt)}
                                                            </div>
                                                        </div>
                                                        <span className={`rounded-full border px-2 py-1 text-[11px] font-semibold ${getCloseForecastTone(snapshot.readinessStatus)}`}>
                                                            {snapshot.readinessStatus.replace(/_/g, ' ')}
                                                        </span>
                                                    </div>
                                                    <div className="flex flex-wrap gap-2 text-[11px]">
                                                        <span className={`rounded-full border px-2 py-1 ${getOperationalAlertTone(snapshot.severity)}`}>{snapshot.severity}</span>
                                                        {snapshot.draftBundleEligible && <span className="rounded-full border border-blue-200 bg-blue-100 px-2 py-1 text-blue-800">draft bundle</span>}
                                                        {snapshot.isOverdue && <span className="rounded-full border border-red-200 bg-red-100 px-2 py-1 text-red-800">overdue</span>}
                                                        {snapshot.hasCanonicalMonthClose && snapshot.monthCloseStatus && <span className={`rounded-full px-2 py-1 ${getMonthCloseTone(snapshot.monthCloseStatus)}`}>close {snapshot.monthCloseStatus.replace(/_/g, ' ')}</span>}
                                                    </div>
                                                    <div className="grid grid-cols-2 gap-2 text-[11px] text-gray-600">
                                                        <div>Exceptions: <span className="font-semibold text-gray-900">{snapshot.openExceptionCount}</span></div>
                                                        <div>Outstanding: <span className="font-semibold text-gray-900">{snapshot.outstandingItemCount}</span></div>
                                                        <div>Missing sections: <span className="font-semibold text-gray-900">{snapshot.missingRequiredSectionCount}</span></div>
                                                        <div>Attestations: <span className="font-semibold text-gray-900">{snapshot.missingAttestationCount}</span></div>
                                                        <div>Uncleared: <span className="font-semibold text-gray-900">{formatCurrency(snapshot.unclearedBalance)}</span></div>
                                                        <div>{snapshot.daysUntilDue >= 0 ? `Due in ${snapshot.daysUntilDue}d` : `${Math.abs(snapshot.daysUntilDue)}d overdue`}</div>
                                                    </div>
                                                    <div className="text-xs text-gray-600">
                                                        {snapshot.recommendedAction || 'No recommendation generated.'}
                                                    </div>
                                                    <div className="flex items-center justify-between gap-2">
                                                        <div className="text-[11px] text-gray-500">
                                                            {snapshot.nextReminderAt ? `Reminder ${formatDate(snapshot.nextReminderAt)}` : 'No reminder queued'}
                                                        </div>
                                                        <div className="flex items-center gap-2">
                                                            {snapshot.draftBundleManifestExportId && (
                                                                <button
                                                                    type="button"
                                                                    onClick={() => refreshBundleIntegrity(snapshot.draftBundleManifestExportId)}
                                                                    className="text-xs font-semibold text-slate-600 hover:text-slate-800"
                                                                >
                                                                    Verify Draft
                                                                </button>
                                                            )}
                                                            <button
                                                                type="button"
                                                                onClick={() => applyCloseForecastScope(snapshot)}
                                                                className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                            >
                                                                Use Scope
                                                            </button>
                                                        </div>
                                                    </div>
                                                </div>
                                            ))
                                        )}
                                    </div>
                                </div>

                                <div className="rounded-xl border border-gray-200 bg-gray-50/70 p-4 space-y-3">
                                    <div className="font-semibold">As-Of Recovery</div>
                                    <select value={projectionRecoveryForm.trustAccountId} onChange={e => setProjectionRecoveryForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full">
                                        <option value="">Select trust account...</option>
                                        {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                    </select>
                                    <input type="date" value={projectionRecoveryForm.asOfUtc} onChange={e => setProjectionRecoveryForm(prev => ({ ...prev, asOfUtc: e.target.value }))} className="input w-full" />
                                    <label className="flex items-center gap-2 text-sm text-gray-600">
                                        <input type="checkbox" checked={projectionRecoveryForm.onlyIfDrifted} onChange={e => setProjectionRecoveryForm(prev => ({ ...prev, onlyIfDrifted: e.target.checked }))} />
                                        Only repair drifted projections on current repair
                                    </label>
                                    <div className="grid grid-cols-2 gap-2">
                                        <button type="button" onClick={() => handleProjectionRecovery(false)} disabled={projectionRecoveryBusy} className="btn-secondary">
                                            {projectionRecoveryBusy ? 'Running...' : 'Preview As-Of'}
                                        </button>
                                        <button type="button" onClick={() => handleProjectionRecovery(true)} disabled={projectionRecoveryBusy} className="btn-primary">
                                            Repair Current
                                        </button>
                                    </div>
                                    {projectionRecoveryResult && (
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-3 text-xs text-gray-700 space-y-1">
                                            <div className="font-semibold text-gray-900">Last run</div>
                                            <div>As-of: {formatDate(projectionRecoveryResult.effectiveAsOfUtc)}</div>
                                            <div>Accounts: {projectionRecoveryResult.accountCount} / Ledgers: {projectionRecoveryResult.ledgerCount}</div>
                                            <div>Drifted accounts: {projectionRecoveryResult.driftedAccountCount}</div>
                                            <div>Drifted ledgers: {projectionRecoveryResult.driftedLedgerCount}</div>
                                            <div>{projectionRecoveryResult.commitProjectionRepair ? 'Current repair applied' : 'Preview only'}</div>
                                        </div>
                                    )}
                                </div>

                                <div className="rounded-xl border border-gray-200 bg-gray-50/70 p-4 space-y-3">
                                    <div className="font-semibold">Packet Regeneration</div>
                                    <select value={packetRecoveryForm.trustAccountId} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full">
                                        <option value="">Select trust account...</option>
                                        {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                    </select>
                                    <select value={packetRecoveryForm.trustReconciliationPacketId} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, trustReconciliationPacketId: e.target.value }))} className="input w-full">
                                        <option value="">Use canonical packet for selected period...</option>
                                        {filteredPackets.slice(0, 24).map(packet => (
                                            <option key={packet.id} value={packet.id}>{getAccountLabel(packet.trustAccountId)} / {formatShortDate(packet.periodEnd)} / v{packet.versionNumber}</option>
                                        ))}
                                    </select>
                                    <div className="grid grid-cols-2 gap-3">
                                        <input type="date" value={packetRecoveryForm.periodStart} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, periodStart: e.target.value }))} className="input w-full" />
                                        <input type="date" value={packetRecoveryForm.periodEnd} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, periodEnd: e.target.value }))} className="input w-full" />
                                    </div>
                                    <input type="text" value={packetRecoveryForm.reason} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, reason: e.target.value }))} className="input w-full" placeholder="Reason for regeneration" />
                                    <textarea value={packetRecoveryForm.notes} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, notes: e.target.value }))} className="input w-full min-h-[84px]" placeholder="Optional regeneration notes" />
                                    <label className="flex items-center gap-2 text-sm text-gray-600">
                                        <input type="checkbox" checked={packetRecoveryForm.autoPrepareMonthClose} onChange={e => setPacketRecoveryForm(prev => ({ ...prev, autoPrepareMonthClose: e.target.checked }))} />
                                        Auto-prepare month close from regenerated packet
                                    </label>
                                    <button type="button" onClick={handlePacketRegeneration} disabled={packetRegenerationBusy} className="btn-primary w-full">
                                        {packetRegenerationBusy ? 'Regenerating...' : 'Regenerate Packet'}
                                    </button>
                                    {packetRegenerationResult && (
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-3 text-xs text-gray-700 space-y-1">
                                            <div className="font-semibold text-gray-900">Last regeneration</div>
                                            <div>Packet: {packetRegenerationResult.packetId}</div>
                                            <div>Status: {packetRegenerationResult.packetStatus}</div>
                                            <div>Version: v{packetRegenerationResult.packetVersionNumber}</div>
                                            <div>{packetRegenerationResult.trustMonthCloseId ? `Month close: ${packetRegenerationResult.trustMonthCloseStatus}` : 'Month close not prepared'}</div>
                                        </div>
                                    )}
                                </div>

                                <div className="rounded-xl border border-gray-200 bg-gray-50/70 p-4 space-y-3">
                                    <div className="font-semibold">Compliance Bundle</div>
                                    <select value={bundleForm.trustAccountId} onChange={e => setBundleForm(prev => ({ ...prev, trustAccountId: e.target.value }))} className="input w-full">
                                        <option value="">Select trust account...</option>
                                        {filteredAccounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}
                                    </select>
                                    <select value={bundleForm.trustMonthCloseId} onChange={e => setBundleForm(prev => ({ ...prev, trustMonthCloseId: e.target.value }))} className="input w-full">
                                        <option value="">Optional month close...</option>
                                        {filteredMonthCloses.slice(0, 24).map(close => (
                                            <option key={close.id} value={close.id}>{getAccountLabel(close.trustAccountId)} / {formatShortDate(close.periodEnd)} / {close.status}</option>
                                        ))}
                                    </select>
                                    <div className="grid grid-cols-2 gap-2 text-sm text-gray-600">
                                        <label className="flex items-center gap-2">
                                            <input type="checkbox" checked={bundleForm.includeJsonPacket} onChange={e => setBundleForm(prev => ({ ...prev, includeJsonPacket: e.target.checked }))} />
                                            JSON packet
                                        </label>
                                        <label className="flex items-center gap-2">
                                            <input type="checkbox" checked={bundleForm.includeAccountJournalCsv} onChange={e => setBundleForm(prev => ({ ...prev, includeAccountJournalCsv: e.target.checked }))} />
                                            Journal CSV
                                        </label>
                                        <label className="flex items-center gap-2">
                                            <input type="checkbox" checked={bundleForm.includeApprovalRegisterCsv} onChange={e => setBundleForm(prev => ({ ...prev, includeApprovalRegisterCsv: e.target.checked }))} />
                                            Approval CSV
                                        </label>
                                        <label className="flex items-center gap-2">
                                            <input type="checkbox" checked={bundleForm.includeClientLedgerCards} onChange={e => setBundleForm(prev => ({ ...prev, includeClientLedgerCards: e.target.checked }))} />
                                            Ledger cards
                                        </label>
                                    </div>
                                    <textarea value={bundleForm.notes} onChange={e => setBundleForm(prev => ({ ...prev, notes: e.target.value }))} className="input w-full min-h-[84px]" placeholder="Optional bundle notes / ticket reference" />
                                    <button type="button" onClick={handleComplianceBundle} disabled={complianceBundleBusy} className="btn-primary w-full">
                                        {complianceBundleBusy ? 'Generating...' : 'Generate Bundle'}
                                    </button>
                                    <div className="grid grid-cols-1 gap-2 border-t border-gray-200 pt-3">
                                        <input
                                            value={bundleSignForm.retentionPolicyTag}
                                            onChange={e => setBundleSignForm(prev => ({ ...prev, retentionPolicyTag: e.target.value }))}
                                            className="input w-full"
                                            placeholder="Retention policy tag"
                                        />
                                        <input
                                            value={bundleSignForm.redactionProfile}
                                            onChange={e => setBundleSignForm(prev => ({ ...prev, redactionProfile: e.target.value }))}
                                            className="input w-full"
                                            placeholder="Redaction profile"
                                        />
                                        <textarea
                                            value={bundleSignForm.notes}
                                            onChange={e => setBundleSignForm(prev => ({ ...prev, notes: e.target.value }))}
                                            className="input w-full min-h-[72px]"
                                            placeholder="Optional signature / custody notes"
                                        />
                                        <div className="grid grid-cols-2 gap-2">
                                            <button type="button" onClick={handleBundleSign} disabled={bundleIntegrityBusy || !complianceBundleResult?.manifestExportId} className="rounded-xl border border-emerald-200 bg-white px-4 py-2 text-sm font-semibold text-emerald-700 transition hover:bg-emerald-50 disabled:opacity-60">
                                                {bundleIntegrityBusy ? 'Working...' : 'Sign Bundle'}
                                            </button>
                                            <button type="button" onClick={() => refreshBundleIntegrity()} disabled={bundleIntegrityBusy || !complianceBundleResult?.manifestExportId} className="rounded-xl border border-gray-200 bg-white px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-gray-50 disabled:opacity-60">
                                                Verify
                                            </button>
                                        </div>
                                    </div>
                                    {complianceBundleResult && (
                                        <div className="rounded-lg border border-gray-200 bg-white px-3 py-3 text-xs text-gray-700 space-y-1">
                                            <div className="font-semibold text-gray-900">Last bundle</div>
                                            <div>Manifest: {complianceBundleResult.manifestFileName}</div>
                                            <div>Artifacts: {complianceBundleResult.exportCount}</div>
                                            <div>Generated: {formatDate(complianceBundleResult.generatedAtUtc)}</div>
                                            {bundleIntegrity && (
                                                <>
                                                    <div>Integrity: {bundleIntegrity.integrityStatus} / {bundleIntegrity.verificationStatus}</div>
                                                    <div>Retention: {bundleIntegrity.retentionPolicyTag || 'n/a'}</div>
                                                    <div>Redaction: {bundleIntegrity.redactionProfile || 'n/a'}</div>
                                                    <div>Evidence refs: {bundleIntegrity.evidenceReferenceCount}</div>
                                                    {bundleIntegrity.signedAt && <div>Signed: {formatDate(bundleIntegrity.signedAt)}</div>}
                                                </>
                                            )}
                                        </div>
                                    )}
                                </div>
                            </div>
                        </div>

                        <div className="rounded-xl border border-gray-200 p-4">
                            <div className="flex items-start justify-between gap-4 mb-3">
                                <div>
                                    <h3 className="font-semibold">Month-Close Workspace</h3>
                                    <p className="text-sm text-gray-500 mt-1">Drive reviewer and responsible-lawyer signoff from the reconciled packet instead of treating packet generation as the last step.</p>
                                </div>
                                <div className="flex gap-3 text-sm">
                                    <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                        <div className="text-gray-500">Open Closes</div>
                                        <div className="text-lg font-semibold">{openMonthCloseCount}</div>
                                    </div>
                                    <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3">
                                        <div className="text-gray-500">Awaiting Lawyer</div>
                                        <div className="text-lg font-semibold">{filteredMonthCloses.filter(close => close.status === 'partially_signed').length}</div>
                                    </div>
                                </div>
                            </div>

                            {latestMonthCloses.length === 0 ? (
                                <div className="rounded-lg border border-dashed border-gray-200 bg-gray-50/70 p-4 text-sm text-gray-500">
                                    No month-close records yet. Prepare a reconciliation packet, then create the close directly from that packet row.
                                </div>
                            ) : (
                                <div className="overflow-x-auto">
                                    <table className="w-full">
                                        <thead>
                                            <tr className="border-b dark:border-gray-700">
                                                <th className="text-left py-3 px-3">Period</th>
                                                <th className="text-left py-3 px-3">Account</th>
                                                <th className="text-left py-3 px-3">Checklist</th>
                                                <th className="text-center py-3 px-3">Exceptions</th>
                                                <th className="text-center py-3 px-3">Status</th>
                                                <th className="text-center py-3 px-3">Actions</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {latestMonthCloses.map(close => {
                                                const completedSteps = getCompletedCloseStepCount(close);
                                                const totalSteps = close.steps.length || 0;
                                                const reviewerStepDone = close.steps.some(step => step.stepKey === 'reviewer_signoff' && step.status === 'completed');
                                                const lawyerStepDone = close.steps.some(step => step.stepKey === 'responsible_lawyer_signoff' && step.status === 'completed');
                                                const blockingSteps = close.steps.filter(step => step.status === 'blocked');
                                                const readySteps = close.steps.filter(step => step.status === 'ready');
                                                const linkedPacket = filteredPackets.find(packet => packet.id === close.reconciliationPacketId);

                                                return (
                                                    <tr key={close.id} className="border-b dark:border-gray-700">
                                                        <td className="py-3 px-3">
                                                            <div className="font-medium">{formatShortDate(close.periodEnd)}</div>
                                                            <div className="text-xs text-gray-500">{formatShortDate(close.periodStart)} - {formatShortDate(close.periodEnd)}</div>
                                                        </td>
                                                        <td className="py-3 px-3">
                                                            <div className="font-medium">{getAccountLabel(close.trustAccountId)}</div>
                                                            <div className="text-xs text-gray-500">{close.policyKey}</div>
                                                            {close.packetTemplateKey && (
                                                                <div className="mt-2 text-xs text-slate-600">
                                                                    Template: {close.packetTemplateName || close.packetTemplateKey}
                                                                    {close.packetTemplateVersionNumber ? ` v${close.packetTemplateVersionNumber}` : ''}
                                                                </div>
                                                            )}
                                                            {linkedPacket && (
                                                                <div className="mt-2">
                                                                    <span className={`px-2 py-1 rounded-full text-[11px] ${getPacketTone(linkedPacket.status)}`}>
                                                                        Packet: {linkedPacket.status.replace(/_/g, ' ')}
                                                                    </span>
                                                                </div>
                                                            )}
                                                        </td>
                                                        <td className="py-3 px-3">
                                                            <div className="flex flex-wrap gap-2">
                                                                {close.steps.map(step => (
                                                                    <span key={step.stepKey} className={`px-2 py-1 rounded-full text-xs ${step.status === 'completed' ? 'bg-green-100 text-green-800' : step.status === 'blocked' ? 'bg-red-100 text-red-800' : 'bg-yellow-100 text-yellow-800'}`}>
                                                                        {step.stepKey.replace(/_/g, ' ')}
                                                                    </span>
                                                                ))}
                                                            </div>
                                                            <div className="text-xs text-gray-500 mt-2">{completedSteps}/{totalSteps} steps complete</div>
                                                            {close.requiredAttestations.length > 0 && (
                                                                <div className="text-xs text-gray-500 mt-1">
                                                                    Attestations: {close.completedAttestations.length}/{close.requiredAttestations.filter(item => item.required).length} complete
                                                                </div>
                                                            )}
                                                            {blockingSteps.length > 0 && (
                                                                <div className="text-xs text-red-600 mt-2">
                                                                    Blocking: {blockingSteps.map(step => `${step.stepKey.replace(/_/g, ' ')}${step.notes ? ` (${step.notes})` : ''}`).join(' â€¢ ')}
                                                                </div>
                                                            )}
                                                            {blockingSteps.length === 0 && readySteps.length > 0 && (
                                                                <div className="text-xs text-blue-600 mt-2">
                                                                    Ready next: {readySteps.map(step => step.stepKey.replace(/_/g, ' ')).join(' • ')}
                                                                </div>
                                                            )}
                                                            {close.missingRequiredSections.length > 0 && (
                                                                <div className="text-xs text-red-600 mt-2">
                                                                    Missing sections: {close.missingRequiredSections.join(' • ')}
                                                                </div>
                                                            )}
                                                            {close.disclosureBlocks.length > 0 && (
                                                                <div className="text-xs text-amber-700 mt-2">
                                                                    Disclosure blocks: {close.disclosureBlocks.join(' • ')}
                                                                </div>
                                                            )}
                                                        </td>
                                                        <td className="py-3 px-3 text-center">{close.openExceptionCount}</td>
                                                        <td className="py-3 px-3 text-center">
                                                            <span className={`px-2 py-1 rounded-full text-xs ${getMonthCloseTone(close.status)}`}>
                                                                {close.status.replace(/_/g, ' ')}
                                                            </span>
                                                        </td>
                                                        <td className="py-3 px-3">
                                                            <div className="flex items-center justify-center gap-2">
                                                                {canSignoff && !reviewerStepDone && (
                                                                        <button onClick={() => handleSignoffMonthClose(close, 'reviewer')} className="text-primary-600 hover:text-primary-700 text-sm font-semibold">
                                                                        Reviewer sign-off
                                                                    </button>
                                                                )}
                                                                {canSignoff && reviewerStepDone && !lawyerStepDone && (
                                                                        <button onClick={() => handleSignoffMonthClose(close, 'responsible_lawyer')} className="text-primary-600 hover:text-primary-700 text-sm font-semibold">
                                                                        Lawyer sign-off
                                                                    </button>
                                                                )}
                                                                {(lawyerStepDone || close.status === 'closed') && (
                                                                    <span className="text-xs text-gray-500">Closed</span>
                                                                )}
                                                                {canExport && (
                                                                    <>
                                                                        <button
                                                                            onClick={() => handleGenerateExport({ exportType: 'month_close_packet', format: 'pdf', trustAccountId: close.trustAccountId, trustMonthCloseId: close.id, trustReconciliationPacketId: close.reconciliationPacketId })}
                                                                            className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                                        >
                                                                            PDF
                                                                        </button>
                                                                        <button
                                                                            onClick={() => handleGenerateExport({ exportType: 'month_close_packet', format: 'json', trustAccountId: close.trustAccountId, trustMonthCloseId: close.id, trustReconciliationPacketId: close.reconciliationPacketId })}
                                                                            className="text-xs font-semibold text-gray-600 hover:text-gray-800"
                                                                        >
                                                                            JSON
                                                                        </button>
                                                                    </>
                                                                )}
                                                            </div>
                                                        </td>
                                                    </tr>
                                                );
                                            })}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </div>

                        <div className="rounded-xl border border-gray-200 p-4">
                            <div className="flex items-center justify-between mb-3">
                                <h3 className="font-semibold">Packet Queue</h3>
                                <span className="text-xs text-gray-500">{filteredPackets.length} packet(s)</span>
                            </div>
                            {filteredPackets.length === 0 ? (
                                <div className="text-sm text-gray-500">No reconciliation packets yet.</div>
                            ) : (
                                <div className="overflow-x-auto">
                                    <table className="w-full">
                                        <thead>
                                            <tr className="border-b dark:border-gray-700">
                                                <th className="text-left py-3 px-3">Period</th>
                                                <th className="text-left py-3 px-3">Account</th>
                                                <th className="text-right py-3 px-3">Adjusted Bank</th>
                                                <th className="text-right py-3 px-3">Journal</th>
                                                <th className="text-right py-3 px-3">Client Ledgers</th>
                                                <th className="text-center py-3 px-3">Exceptions</th>
                                                <th className="text-center py-3 px-3">Status</th>
                                                <th className="text-center py-3 px-3">Action</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {filteredPackets.slice().sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime()).map(packet => {
                                                const relatedClose = monthCloseByPacketId.get(packet.id);

                                                return (
                                                    <tr key={packet.id} className="border-b dark:border-gray-700">
                                                        <td className="py-3 px-3">{new Date(packet.periodEnd).toLocaleDateString('en-US')}</td>
                                                        <td className="py-3 px-3">{getAccountLabel(packet.trustAccountId)}</td>
                                                        <td className="py-3 px-3 text-right">{formatCurrency(Number(packet.adjustedBankBalance))}</td>
                                                        <td className="py-3 px-3 text-right">{formatCurrency(Number(packet.journalBalance))}</td>
                                                        <td className="py-3 px-3 text-right">{formatCurrency(Number(packet.clientLedgerBalance))}</td>
                                                        <td className="py-3 px-3 text-center">{packet.exceptionCount}</td>
                                                        <td className="py-3 px-3 text-center">
                                                            <span className={`px-2 py-1 rounded-full text-xs ${getPacketTone(packet.status)}`}>
                                                                {packet.status.replace(/_/g, ' ')}
                                                            </span>
                                                        </td>
                                                        <td className="py-3 px-3 text-center">
                                                            <div className="flex flex-col items-center gap-1">
                                                                <button
                                                                    type="button"
                                                                    onClick={() => handleInspectPacket(packet)}
                                                                    className="text-xs font-semibold text-gray-700 hover:text-gray-900"
                                                                >
                                                                    Inspect
                                                                </button>
                                                                {packet.status !== 'signed_off' && canSignoff ? (
                                                                    <button onClick={() => handleSignoffPacket(packet.id)} className="text-primary-600 hover:text-primary-700 text-sm font-semibold">Sign Off</button>
                                                                ) : (
                                                                    <span className="text-xs text-gray-500">Packet locked</span>
                                                                )}
                                                                {relatedClose ? (
                                                                    <span className={`px-2 py-1 rounded-full text-[11px] ${getMonthCloseTone(relatedClose.status)}`}>
                                                                        Close: {relatedClose.status.replace(/_/g, ' ')}
                                                                    </span>
                                                                ) : (
                                                                    <button
                                                                        onClick={() => handlePrepareMonthCloseFromPacket(packet)}
                                                                        className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                                    >
                                                                        Prepare close
                                                                    </button>
                                                                )}
                                                                {canExport && (
                                                                    <div className="flex items-center gap-2">
                                                                        <button
                                                                            onClick={() => handleGenerateExport({ exportType: 'month_close_packet', format: 'pdf', trustAccountId: packet.trustAccountId, trustReconciliationPacketId: packet.id })}
                                                                            className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                                        >
                                                                            Export PDF
                                                                        </button>
                                                                        <button
                                                                            onClick={() => handleGenerateExport({ exportType: 'month_close_packet', format: 'json', trustAccountId: packet.trustAccountId, trustReconciliationPacketId: packet.id })}
                                                                            className="text-xs font-semibold text-gray-600 hover:text-gray-800"
                                                                        >
                                                                            JSON
                                                                        </button>
                                                                    </div>
                                                                )}
                                                                <div className="text-[11px] text-gray-500">
                                                                    {(packet.matchedStatementLineCount ?? 0)} matched / {(packet.unmatchedStatementLineCount ?? 0)} open
                                                                </div>
                                                            </div>
                                                        </td>
                                                    </tr>
                                                );
                                            })}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </div>

                        <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="font-semibold">Statement Imports</h3>
                                    <span className="text-xs text-gray-500">{latestStatementImports.length} recent</span>
                                </div>
                                {latestStatementImports.length === 0 ? (
                                    <div className="text-sm text-gray-500">No imported statements yet.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {latestStatementImports.map(statement => (
                                                <div key={statement.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                    <div className="flex items-start justify-between gap-3">
                                                        <div>
                                                            <div className="font-medium text-sm">{getAccountLabel(statement.trustAccountId)}</div>
                                                            <div className="text-xs text-gray-500">{new Date(statement.periodEnd).toLocaleDateString('en-US')} / {statement.lineCount} line(s)</div>
                                                        </div>
                                                        <span className={`rounded-full px-2 py-1 text-[11px] font-semibold ${getStatementImportTone(statement.status)}`}>
                                                            {statement.status.replace(/_/g, ' ')}
                                                        </span>
                                                    </div>
                                                    <div className="text-sm font-semibold mt-1">{formatCurrency(Number(statement.statementEndingBalance))}</div>
                                                    <div className="mt-2 space-y-1 text-xs text-gray-500">
                                                        {statement.sourceFileName && <div>File: {statement.sourceFileName}</div>}
                                                        {statement.sourceFileHash && <div>Hash: {shortenFingerprint(statement.sourceFileHash)}</div>}
                                                        {statement.duplicateOfStatementImportId && <div>Duplicate of: {shortenFingerprint(statement.duplicateOfStatementImportId)}</div>}
                                                    </div>
                                                    <button
                                                        type="button"
                                                        onClick={() => handleSelectStatementImport(statement.id)}
                                                        className="mt-2 text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                    >
                                                        Open matching workspace
                                                    </button>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>

                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="font-semibold">Outstanding Items</h3>
                                    <span className="text-xs text-gray-500">{latestOutstandingItems.length} recent</span>
                                </div>
                                {latestOutstandingItems.length === 0 ? (
                                    <div className="text-sm text-gray-500">No outstanding items for this filter.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {latestOutstandingItems.slice(0, 6).map(item => (
                                            <div key={item.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <div className="font-medium text-sm">{item.itemType.replace(/_/g, ' ')}</div>
                                                        <div className="text-xs text-gray-500">{getAccountLabel(item.trustAccountId)} / {item.reference || 'No ref'}</div>
                                                    </div>
                                                    <div className={`font-semibold ${item.impactDirection === 'increase_bank' ? 'text-green-700' : 'text-red-700'}`}>
                                                        {item.impactDirection === 'increase_bank' ? '+' : '-'}{formatCurrency(Number(item.amount))}
                                                    </div>
                                                </div>
                                                {(item.reasonCode || item.attachmentEvidenceKey || item.description) && (
                                                    <div className="mt-2 space-y-1 text-xs text-gray-600">
                                                        {item.reasonCode && <div>Reason: <span className="font-medium">{item.reasonCode}</span></div>}
                                                        {item.attachmentEvidenceKey && <div>Evidence: <span className="font-medium">{item.attachmentEvidenceKey}</span></div>}
                                                        {item.description && <div>{item.description}</div>}
                                                    </div>
                                                )}
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="font-semibold">Legacy Records</h3>
                                    <span className="text-xs text-gray-500">{filteredReconciliations.length} total</span>
                                </div>
                                {filteredReconciliations.length === 0 ? (
                                    <div className="text-sm text-gray-500">No legacy reconciliation records yet.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {filteredReconciliations.slice().sort((a, b) => new Date(b.periodEnd).getTime() - new Date(a.periodEnd).getTime()).slice(0, 6).map(recon => (
                                            <div key={recon.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <div className="font-medium text-sm">{getAccountLabel(recon.trustAccountId)}</div>
                                                        <div className="text-xs text-gray-500">{new Date(recon.periodEnd).toLocaleDateString('en-US')}</div>
                                                    </div>
                                                    <span className={`px-2 py-1 rounded-full text-xs ${recon.isReconciled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
                                                        {recon.isReconciled ? 'Matched' : 'Exception'}
                                                    </span>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                        {canExport && (
                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="font-semibold">Export History</h3>
                                    <span className="text-xs text-gray-500">{filteredExportHistory.length} recent export(s)</span>
                                </div>
                                {filteredExportHistory.length === 0 ? (
                                    <div className="text-sm text-gray-500">No trust exports generated yet.</div>
                                ) : (
                                    <div className="overflow-x-auto">
                                        <table className="w-full">
                                            <thead>
                                                <tr className="border-b dark:border-gray-700">
                                                    <th className="text-left py-3 px-3">Generated</th>
                                                    <th className="text-left py-3 px-3">Type</th>
                                                    <th className="text-left py-3 px-3">Scope</th>
                                                    <th className="text-left py-3 px-3">File</th>
                                                    <th className="text-center py-3 px-3">Action</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                {filteredExportHistory.slice(0, 12).map(item => (
                                                    <tr key={item.id} className="border-b dark:border-gray-700">
                                                        <td className="py-3 px-3 text-sm">{formatDate(item.generatedAt)}</td>
                                                        <td className="py-3 px-3 text-sm">{item.exportType.replace(/_/g, ' ')}</td>
                                                        <td className="py-3 px-3 text-sm">
                                                            {item.trustAccountId ? getAccountLabel(item.trustAccountId) : 'All trust accounts'}
                                                        </td>
                                                        <td className="py-3 px-3 text-sm text-gray-600">{item.fileName}</td>
                                                        <td className="py-3 px-3 text-center">
                                                            <button
                                                                onClick={() => downloadExportRecord(item)}
                                                                className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                            >
                                                                Download
                                                            </button>
                                                        </td>
                                                    </tr>
                                                ))}
                                            </tbody>
                                        </table>
                                    </div>
                                )}
                            </div>
                        )}
                    </div>
                )}

                {/* Audit Log Tab */}
                {activeTab === 'audit' && (
                    <div className="space-y-4">
                        <div className="flex items-center justify-between">
                            <h2 className="text-lg font-semibold">Audit Log</h2>
                            <button
                                onClick={loadAuditLogs}
                                className="btn-sm btn-outline flex items-center gap-1"
                            >
                                <RefreshCw className="w-4 h-4" /> Refresh
                            </button>
                        </div>

                        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                            <div>
                                <label className="block text-xs font-semibold text-gray-500 mb-1">Entity Type</label>
                                <select
                                    value={auditFilters.entityType}
                                    onChange={(e) => setAuditFilters(prev => ({ ...prev, entityType: e.target.value }))}
                                    className="input w-full"
                                >
                                    <option value="all">All</option>
                                    <option value="TrustBankAccount">Trust Bank Account</option>
                                    <option value="ClientTrustLedger">Client Ledger</option>
                                    <option value="TrustTransaction">Trust Transaction</option>
                                    <option value="ReconciliationRecord">Reconciliation</option>
                                </select>
                            </div>
                            <div className="md:col-span-2">
                                <label className="block text-xs font-semibold text-gray-500 mb-1">Search</label>
                                <input
                                    value={auditFilters.query}
                                    onChange={(e) => setAuditFilters(prev => ({ ...prev, query: e.target.value }))}
                                    className="input w-full"
                                    placeholder="Search by user, action, or details"
                                />
                            </div>
                        </div>

                        {auditAccessDenied ? (
                            <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-900">
                                Audit log visibility is limited to `Admin` and `SecurityAdmin` users.
                            </div>
                        ) : auditLoading ? (
                            <div className="text-sm text-gray-500">Loading audit logs...</div>
                        ) : auditLogs.length === 0 ? (
                            <div className="text-sm text-gray-500">No audit log entries found.</div>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="w-full">
                                    <thead>
                                        <tr className="border-b dark:border-gray-700 text-xs uppercase text-gray-400">
                                            <th className="text-left py-3 px-4">Timestamp</th>
                                            <th className="text-left py-3 px-4">Action</th>
                                            <th className="text-left py-3 px-4">Entity</th>
                                            <th className="text-left py-3 px-4">Actor</th>
                                            <th className="text-left py-3 px-4">Details</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {auditLogs.map(log => (
                                            <tr key={log.id} className="border-b dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-gray-800">
                                                <td className="py-3 px-4 text-sm text-gray-600">
                                                    {formatDate(log.createdAt)}
                                                </td>
                                                <td className="py-3 px-4 text-sm font-medium text-gray-800">
                                                    {log.action}
                                                </td>
                                                <td className="py-3 px-4 text-sm text-gray-600">
                                                    {log.entityType}
                                                </td>
                                                <td className="py-3 px-4 text-sm text-gray-600">
                                                    {log.userEmail || log.clientEmail || 'System'}
                                                </td>
                                                <td className="py-3 px-4 text-sm text-gray-500">
                                                    {log.details || '-'}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                )}

                {/* Overview Tab */}
                {activeTab === 'overview' && (
                    <div className="space-y-6">
                        <div className="grid grid-cols-1 xl:grid-cols-4 gap-4">
                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="text-lg font-semibold">Pending Clearance</h3>
                                    <span className="text-xs text-gray-500">{pendingClearanceTransactions.length} open</span>
                                </div>
                                {pendingClearanceTransactions.length === 0 ? (
                                    <div className="text-sm text-gray-500">No approved deposits are waiting for clearance.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {pendingClearanceTransactions.slice(0, 4).map(tx => (
                                            <div key={tx.id} className="rounded-lg bg-yellow-50 px-3 py-3">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <div className="font-medium text-sm">{tx.description}</div>
                                                        <div className="text-xs text-gray-600">{getAccountLabel(tx.trustAccountId)} / {tx.payorPayee}</div>
                                                    </div>
                                                    <div className="text-right">
                                                        <div className="font-semibold text-yellow-700">{formatCurrency(Number(tx.amount))}</div>
                                                        {canApprove && (
                                                            <button onClick={() => handleClearDeposit(tx.id)} className="text-xs font-semibold text-primary-600 hover:text-primary-700">
                                                                Mark cleared
                                                            </button>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="text-lg font-semibold">Approval Queue</h3>
                                    <span className="text-xs text-gray-500">{latestApprovalQueue.length} shown</span>
                                </div>
                                {latestApprovalQueue.length === 0 ? (
                                    <div className="text-sm text-gray-500">No transactions are waiting on trust approval steps.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {latestApprovalQueue.slice(0, 4).map(item => (
                                            <div key={item.trustTransactionId} className="rounded-lg bg-yellow-50 px-3 py-3">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <div className="font-medium text-sm">{item.transactionType.replace(/_/g, ' ')}</div>
                                                        <div className="text-xs text-gray-600">{getAccountLabel(item.trustAccountId)} / {formatCurrency(Number(item.amount))}</div>
                                                        <div className="flex flex-wrap gap-1 mt-2">
                                                            {item.requirements.slice(0, 3).map(requirement => (
                                                                <span key={requirement.id} className={`px-2 py-1 rounded-full text-[11px] ${getApprovalRequirementTone(requirement)}`}>
                                                                    {requirement.requirementType.replace(/_/g, ' ')} {requirement.satisfiedCount}/{requirement.requiredCount}
                                                                </span>
                                                            ))}
                                                        </div>
                                                    </div>
                                                    {canApprove && item.requirements.some(requirement => requirement.satisfiedCount < requirement.requiredCount) && (
                                                        <button
                                                            onClick={() => handleApproveRequirement(item.trustTransactionId, item.requirements.find(requirement => requirement.satisfiedCount < requirement.requiredCount)?.requirementType)}
                                                            className="text-xs font-semibold text-primary-600 hover:text-primary-700"
                                                        >
                                                            Approve step
                                                        </button>
                                                    )}
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="text-lg font-semibold">Open Holds</h3>
                                    <span className="text-xs text-gray-500">{visibleOpenHolds.length} active</span>
                                </div>
                                {visibleOpenHolds.length === 0 ? (
                                    <div className="text-sm text-gray-500">No active trust holds.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {visibleOpenHolds.slice(0, 4).map(hold => (
                                            <div key={hold.id} className="rounded-lg bg-red-50 px-3 py-3">
                                                <div className="font-medium text-sm">{hold.holdType || 'Compliance hold'}</div>
                                                <div className="text-xs text-red-700 mt-1">{hold.reason || 'Manual review required before funds move.'}</div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div className="rounded-xl border border-gray-200 p-4">
                                <div className="flex items-center justify-between mb-3">
                                    <h3 className="text-lg font-semibold">Month Close</h3>
                                    <span className="text-xs text-gray-500">{latestMonthCloses.length} shown</span>
                                </div>
                                {latestMonthCloses.length === 0 ? (
                                    <div className="text-sm text-gray-500">No month-close records prepared yet.</div>
                                ) : (
                                    <div className="space-y-2">
                                        {latestMonthCloses.slice(0, 4).map(close => (
                                            <div key={close.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                                <div className="flex items-center justify-between gap-3">
                                                    <div>
                                                        <div className="font-medium text-sm">{getAccountLabel(close.trustAccountId)}</div>
                                                        <div className="text-xs text-gray-500">{formatShortDate(close.periodEnd)} / exceptions {close.openExceptionCount}</div>
                                                        <div className="text-xs text-gray-500 mt-1">{getCompletedCloseStepCount(close)}/{close.steps.length} steps complete</div>
                                                    </div>
                                                    <span className={`px-2 py-1 rounded-full text-xs ${getMonthCloseTone(close.status)}`}>
                                                        {close.status.replace(/_/g, ' ')}
                                                    </span>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                        <div className="rounded-xl border border-gray-200 p-4">
                            <div className="flex items-center justify-between mb-3">
                                <h3 className="text-lg font-semibold">Latest Packets</h3>
                                <span className="text-xs text-gray-500">{latestPackets.length} shown</span>
                            </div>
                            {latestPackets.length === 0 ? (
                                <div className="text-sm text-gray-500">No reconciliation packets yet.</div>
                            ) : (
                                <div className="grid grid-cols-1 xl:grid-cols-3 gap-3">
                                    {latestPackets.slice(0, 6).map(packet => (
                                        <div key={packet.id} className="rounded-lg bg-gray-50 px-3 py-3">
                                            <div className="flex items-center justify-between gap-3">
                                                <div>
                                                    <div className="font-medium text-sm">{getAccountLabel(packet.trustAccountId)}</div>
                                                    <div className="text-xs text-gray-500">{formatShortDate(packet.periodEnd)} / exceptions {packet.exceptionCount}</div>
                                                </div>
                                                            <span className={`px-2 py-1 rounded-full text-xs ${getPacketTone(packet.status)}`}>
                                                                {packet.status.replace(/_/g, ' ')}
                                                            </span>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>

                        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        {/* Recent Transactions */}
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Recent Transactions</h3>
                            {filteredTransactions.length === 0 ? (
                                <div className="rounded-xl border border-dashed border-gray-200 bg-gray-50/70 p-5 text-sm text-gray-500">
                                    No trust transactions yet. Deposits, withdrawals, and approvals will appear here once posted.
                                </div>
                            ) : (
                                <div className="space-y-2">
                                    {filteredTransactions.slice(0, 5).map(tx => (
                                        <div key={tx.id} className="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
                                            <div className="flex items-center gap-3">
                                                {tx.type === 'DEPOSIT' ? (
                                                    <ArrowDownCircle className="w-5 h-5 text-green-600" />
                                                ) : (
                                                    <ArrowUpCircle className="w-5 h-5 text-red-600" />
                                                )}
                                                <div>
                                                    <p className="font-medium text-sm">{tx.description}</p>
                                                    <p className="text-xs text-gray-500">
                                                        {[tx.payorPayee, getAccountLabel(tx.trustAccountId)].filter(Boolean).join(' â€¢ ')}
                                                    </p>
                                                </div>
                                            </div>
                                            <div className="text-right">
                                                <p className={`font-semibold ${tx.type === 'DEPOSIT' ? 'text-green-600' : 'text-red-600'}`}>
                                                    {tx.type === 'DEPOSIT' ? '+' : '-'}{formatCurrency(Number(tx.amount))}
                                                </p>
                                                <p className="text-xs text-gray-500">{formatDate(tx.createdAt)}</p>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>

                        {/* Client Ledger Summary */}
                        <div>
                            <h3 className="text-lg font-semibold mb-4">Client Balances</h3>
                            {filteredLedgers.length === 0 ? (
                                <div className="rounded-xl border border-dashed border-gray-200 bg-gray-50/70 p-5 text-sm text-gray-500">
                                    No client ledgers yet. Create a ledger to tie trust funds to a client or matter.
                                </div>
                            ) : (
                                <div className="space-y-2">
                                    {filteredLedgers.slice(0, 5).map(ledger => (
                                        <div key={ledger.id} className="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-800 rounded-lg">
                                            <div>
                                                <p className="font-medium text-sm">{getClientLabel(ledger.clientId)}</p>
                                                <p className="text-xs text-gray-500">
                                                    {[getMatterLabel(ledger.matterId), getAccountLabel(ledger.trustAccountId)].filter(Boolean).join(' â€¢ ')}
                                                </p>
                                            </div>
                                            <p className="font-semibold text-green-600">
                                                {formatCurrency(Number(ledger.runningBalance))}
                                            </p>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>
                    </div>
                )}
            </div>

            {/* Deposit Modal */}
            {showDepositForm && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white dark:bg-gray-900 rounded-xl p-6 w-full max-w-lg max-h-[90vh] overflow-y-auto">
                        <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                            <ArrowDownCircle className="w-5 h-5 text-green-600" />
                            Deposit to Trust Account
                        </h2>
                        <form onSubmit={handleDeposit} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-1">Trust Account</label>
                                <select
                                    value={depositForm.trustAccountId}
                                    onChange={e => setDepositForm({
                                        ...depositForm,
                                        trustAccountId: e.target.value,
                                        allocations: depositForm.allocations.map(allocation => ({ ...allocation, ledgerId: '' }))
                                    })}
                                    className="input w-full"
                                    required
                                >
                                    {filteredAccounts.map(a => (
                                        <option key={a.id} value={a.id}>{a.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Amount ($)</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={depositForm.amount}
                                    onChange={e => setDepositForm({ ...depositForm, amount: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Payor Name</label>
                                <input
                                    type="text"
                                    value={depositForm.payorPayee}
                                    onChange={e => setDepositForm({ ...depositForm, payorPayee: e.target.value })}
                                    className="input w-full"
                                    placeholder="Client name or organization"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Description</label>
                                <input
                                    type="text"
                                    value={depositForm.description}
                                    onChange={e => setDepositForm({ ...depositForm, description: e.target.value })}
                                    className="input w-full"
                                    placeholder="Retainer, filing fees, etc."
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Check/Reference # (Optional)</label>
                                <input
                                    type="text"
                                    value={depositForm.checkNumber}
                                    onChange={e => setDepositForm({ ...depositForm, checkNumber: e.target.value })}
                                    className="input w-full"
                                />
                            </div>

                            {/* Allocations */}
                            <div className="border-t pt-4">
                                <label className="block text-sm font-medium mb-2">Allocation (to Client Ledgers)</label>
                                {depositForm.allocations.map((alloc, idx) => (
                                    <div key={idx} className="flex gap-2 mb-2">
                                        <select
                                            value={alloc.ledgerId}
                                            onChange={e => {
                                                const newAllocs = [...depositForm.allocations];
                                                newAllocs[idx].ledgerId = e.target.value;
                                                setDepositForm({ ...depositForm, allocations: newAllocs });
                                            }}
                                            className="input flex-1"
                                        >
                                            <option value="">Select Ledger...</option>
                                            {availableDepositLedgers.map(l => (
                                                <option key={l.id} value={l.id}>
                                                    {getLedgerLabel(l)} - {formatCurrency(Number(l.runningBalance))}
                                                </option>
                                            ))}
                                        </select>
                                        <input
                                            type="number"
                                            step="0.01"
                                            value={alloc.amount}
                                            onChange={e => {
                                                const newAllocs = [...depositForm.allocations];
                                                newAllocs[idx].amount = e.target.value;
                                                setDepositForm({ ...depositForm, allocations: newAllocs });
                                            }}
                                            className="input w-28"
                                            placeholder="Amount"
                                        />
                                    </div>
                                ))}
                                <button
                                    type="button"
                                    onClick={() => setDepositForm({
                                        ...depositForm,
                                        allocations: [...depositForm.allocations, { ledgerId: '', amount: '', description: '' }]
                                    })}
                                    className="text-sm text-primary-600 hover:underline"
                                >
                                    + Add another ledger
                                </button>
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setShowDepositForm(false)} className="btn-secondary">
                                    Cancel
                                </button>
                                <button type="submit" className="btn-primary">
                                    Save Deposit
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Withdrawal Modal */}
            {showWithdrawalForm && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white dark:bg-gray-900 rounded-xl p-6 w-full max-w-lg">
                        <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                            <ArrowUpCircle className="w-5 h-5 text-red-600" />
                            Withdrawal from Trust Account
                        </h2>
                        <form onSubmit={handleWithdrawal} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-1">Trust Account</label>
                                <select
                                    value={withdrawalForm.trustAccountId}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, trustAccountId: e.target.value, ledgerId: '' })}
                                    className="input w-full"
                                    required
                                >
                                    {filteredAccounts.map(a => (
                                        <option key={a.id} value={a.id}>{a.name} - {formatCurrency(Number(a.currentBalance))}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Client Ledger</label>
                                <select
                                    value={withdrawalForm.ledgerId}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, ledgerId: e.target.value })}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">Select Ledger...</option>
                                    {availableWithdrawalLedgers.map(l => (
                                        <option key={l.id} value={l.id}>
                                            {getLedgerLabel(l)} - Available: {formatCurrency(Number(l.availableToDisburse ?? l.runningBalance))}
                                        </option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Amount ($)</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={withdrawalForm.amount}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, amount: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Payee Name</label>
                                <input
                                    type="text"
                                    value={withdrawalForm.payorPayee}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, payorPayee: e.target.value })}
                                    className="input w-full"
                                    placeholder="Court, expert, client, etc."
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Description</label>
                                <input
                                    type="text"
                                    value={withdrawalForm.description}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, description: e.target.value })}
                                    className="input w-full"
                                    placeholder="Fee, expense, refund, etc."
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Check # (Optional)</label>
                                <input
                                    type="text"
                                    value={withdrawalForm.checkNumber}
                                    onChange={e => setWithdrawalForm({ ...withdrawalForm, checkNumber: e.target.value })}
                                    className="input w-full"
                                />
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setShowWithdrawalForm(false)} className="btn-secondary">
                                    Cancel
                                </button>
                                <button type="submit" className="btn-primary">
                                    Save Withdrawal
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Reconciliation Modal */}
            {showReconcileForm && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white dark:bg-gray-900 rounded-xl p-6 w-full max-w-lg">
                        <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                            <Calculator className="w-5 h-5 text-blue-600" />
                            Three-Way Reconciliation
                        </h2>
                        <form onSubmit={handleReconcile} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-1">Trust Account</label>
                                <select
                                    value={reconcileForm.trustAccountId}
                                    onChange={e => setReconcileForm({ ...reconcileForm, trustAccountId: e.target.value })}
                                    className="input w-full"
                                    required
                                >
                                    {filteredAccounts.map(a => (
                                        <option key={a.id} value={a.id}>{a.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Period End Date</label>
                                <input
                                    type="date"
                                    value={reconcileForm.periodEnd}
                                    onChange={e => setReconcileForm({ ...reconcileForm, periodEnd: e.target.value })}
                                    className="input w-full"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Bank Statement Balance ($)</label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={reconcileForm.bankStatementBalance}
                                    onChange={e => setReconcileForm({ ...reconcileForm, bankStatementBalance: e.target.value })}
                                    className="input w-full"
                                    placeholder="Balance from bank statement"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Notes (Optional)</label>
                                <textarea
                                    value={reconcileForm.notes}
                                    onChange={e => setReconcileForm({ ...reconcileForm, notes: e.target.value })}
                                    className="input w-full"
                                    rows={3}
                                    placeholder="Reconciliation notes..."
                                />
                            </div>

                            <div className="bg-blue-50 dark:bg-blue-900/20 p-4 rounded-lg text-sm">
                                <p className="font-semibold text-blue-800 dark:text-blue-200 mb-2">
                                    Three-Way Check:
                                </p>
                                <ul className="text-blue-700 dark:text-blue-300 space-y-1">
                                    <li>Bank Statement Balance</li>
                                    <li>Trust Account Balance (Software)</li>
                                    <li>Client Ledgers Total</li>
                                </ul>
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setShowReconcileForm(false)} className="btn-secondary">
                                    Cancel
                                </button>
                                <button type="submit" className="btn-primary">
                                    Perform Reconciliation
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Create Ledger Modal */}
            {showCreateLedger && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white dark:bg-gray-900 rounded-xl p-6 w-full max-w-lg max-h-[90vh] overflow-y-auto">
                        <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                            <Users className="w-5 h-5 text-blue-600" />
                            Create Client Ledger
                        </h2>
                        <form onSubmit={handleCreateLedger} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-1">Trust Account *</label>
                                <select
                                    value={ledgerForm.trustAccountId}
                                    onChange={e => setLedgerForm({ ...ledgerForm, trustAccountId: e.target.value })}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">Select Trust Account...</option>
                                    {filteredAccounts.map(a => (
                                        <option key={a.id} value={a.id}>{a.name} - {a.bankName}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Client *</label>
                                <select
                                    value={ledgerForm.clientId}
                                    onChange={e => setLedgerForm({ ...ledgerForm, clientId: e.target.value, matterId: '' })}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">Select Client...</option>
                                    {clients.map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Matter (Optional)</label>
                                <select
                                    value={ledgerForm.matterId}
                                    onChange={e => setLedgerForm({ ...ledgerForm, matterId: e.target.value })}
                                    className="input w-full"
                                >
                                    <option value="">General Ledger (No specific matter)</option>
                                    {availableMattersForLedger.map(m => (
                                        <option key={m.id} value={m.id}>{m.name} - {m.caseNumber}</option>
                                    ))}
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Notes (Optional)</label>
                                <textarea
                                    value={ledgerForm.notes}
                                    onChange={e => setLedgerForm({ ...ledgerForm, notes: e.target.value })}
                                    className="input w-full"
                                    rows={2}
                                    placeholder="Any notes about this ledger..."
                                />
                            </div>

                            <div className="bg-blue-50 dark:bg-blue-900/20 p-3 rounded-lg text-sm">
                                <p className="text-blue-800 dark:text-blue-200">
                                    <strong>Note:</strong> A client ledger tracks trust funds held for a specific client.
                                    Each client can have multiple ledgers if needed (one per matter).
                                </p>
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setShowCreateLedger(false)} className="btn-secondary">
                                    Cancel
                                </button>
                                <button type="submit" className="btn-primary">
                                    Create Ledger
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}

            {/* Create Trust Account Modal */}
            {showCreateAccount && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white dark:bg-gray-900 rounded-xl p-6 w-full max-w-lg max-h-[90vh] overflow-y-auto">
                        <h2 className="text-xl font-bold mb-4 flex items-center gap-2">
                            <Building2 className="w-5 h-5 text-green-600" />
                            Create Trust Bank Account
                        </h2>
                        <form onSubmit={handleCreateAccount} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium mb-2">Entity & Office</label>
                                <EntityOfficeFilter
                                    entityId={accountForm.entityId}
                                    officeId={accountForm.officeId}
                                    onEntityChange={(id) => setAccountForm(prev => ({ ...prev, entityId: id, officeId: '' }))}
                                    onOfficeChange={(id) => setAccountForm(prev => ({ ...prev, officeId: id }))}
                                    autoSelectDefault
                                />
                                <p className="text-xs text-gray-500 mt-2">Use the entity/office defaults if you leave this blank.</p>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Account Name *</label>
                                <input
                                    type="text"
                                    value={accountForm.name}
                                    onChange={e => setAccountForm({ ...accountForm, name: e.target.value })}
                                    className="input w-full"
                                    placeholder="e.g., Primary IOLTA Account"
                                    required
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">Bank Name *</label>
                                <input
                                    type="text"
                                    value={accountForm.bankName}
                                    onChange={e => setAccountForm({ ...accountForm, bankName: e.target.value })}
                                    className="input w-full"
                                    placeholder="e.g., Chase Bank, Bank of America"
                                    required
                                />
                            </div>
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium mb-1">Routing/ABA Number *</label>
                                    <input
                                        type="text"
                                        value={accountForm.routingNumber}
                                        onChange={e => setAccountForm({ ...accountForm, routingNumber: e.target.value.replace(/\D/g, '').slice(0, 9) })}
                                        className="input w-full"
                                        placeholder="9 digits"
                                        maxLength={9}
                                        required
                                    />
                                    <p className="text-xs text-gray-500 mt-1">9-digit routing number</p>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium mb-1">Account Number *</label>
                                    <input
                                        type="text"
                                        value={accountForm.accountNumber}
                                        onChange={e => setAccountForm({ ...accountForm, accountNumber: e.target.value })}
                                        className="input w-full"
                                        placeholder="Account number"
                                        required
                                    />
                                </div>
                            </div>
                            <div>
                                <label className="block text-sm font-medium mb-1">State/Jurisdiction *</label>
                                <select
                                    value={accountForm.jurisdiction}
                                    onChange={e => setAccountForm({ ...accountForm, jurisdiction: e.target.value })}
                                    className="input w-full"
                                    required
                                >
                                    {usStates.map(state => (
                                        <option key={state} value={state}>{state}</option>
                                    ))}
                                </select>
                                <p className="text-xs text-gray-500 mt-1">Each state has different IOLTA rules</p>
                            </div>

                            <div className="bg-blue-50 dark:bg-blue-900/20 p-3 rounded-lg text-sm">
                                <p className="text-blue-800 dark:text-blue-200">
                                    <strong>Note:</strong> This account must be an IOLTA-compliant trust account at an approved financial institution.
                                    Account numbers are encrypted and stored securely.
                                </p>
                            </div>

                            <div className="flex justify-end gap-2 pt-4">
                                <button type="button" onClick={() => setShowCreateAccount(false)} className="btn-secondary">
                                    Cancel
                                </button>
                                <button type="submit" className="btn-primary">
                                    Create Account
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
