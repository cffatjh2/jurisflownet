import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Matter, CaseStatus, PracticeArea, FeeStructure, Client, DocumentFile, CourtType, OutcomeFeePlanDetailResult, OutcomeFeePlanVersionCompareResult, OutcomeFeePlanPortfolioMetricsResult, OutcomeFeeCalibrationEffectiveResult } from '../types';
import { Search, ChevronRight, Filter, Plus, X, Clock, FileText, Mail, Calendar, Trash, Users } from './Icons';
import { Can } from './common/Can';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { api } from '../services/api';
import { passwordRequirementsText, validatePassword } from '../services/passwordPolicy';
import mammoth from 'mammoth';
import { toast } from './Toast';
import { Combobox } from './common/Combobox';
import ClientSelectorModal from './ClientSelectorModal';
import EntityOfficeFilter from './common/EntityOfficeFilter';
import MatterNotesPanel from './MatterNotesPanel';

type PlannerComplexity = 'low' | 'medium' | 'high';
type PlannerClaimSizeBand = 'small' | 'medium' | 'large' | 'enterprise';
type PlannerPayorProfile = 'client' | 'corporate' | 'third_party';

type OutcomePlannerDraft = {
  enabled: boolean;
  autoSave: boolean;
  complexity: PlannerComplexity;
  claimSizeBand: PlannerClaimSizeBand;
  primaryPayorProfile: PlannerPayorProfile;
  jurisdictionCode: string;
  baseBillableRateOverride: string;
  notes: string;
};

type OutcomePlannerPreview = {
  scenarios: Array<{
    key: string;
    name: string;
    probability: number;
    budgetTotal: number;
    expectedCollected: number;
    expectedMargin: number;
    confidenceScore: number;
    confidenceBand: 'low' | 'medium' | 'high';
    dataCoverageScore: number;
    riskFlags: string[];
    outcomeProbabilities: { settle: number; dismiss: number; trial: number; adverse: number };
    topDrivers: Array<{ key: string; impact: number }>;
    inputSensitivitySummary: string;
    driverSummary: string;
  }>;
  assumptions: Array<{ key: string; value: string }>;
};

type OutcomePlannerFeedbackDraft = {
  actualOutcome: string;
  actualFeesCollected: string;
  actualCost: string;
  actualMargin: string;
  notes: string;
};

const defaultOutcomePlannerDraft = (): OutcomePlannerDraft => ({
  enabled: true,
  autoSave: true,
  complexity: 'medium',
  claimSizeBand: 'medium',
  primaryPayorProfile: 'client',
  jurisdictionCode: '',
  baseBillableRateOverride: '',
  notes: ''
});

const defaultOutcomePlannerFeedbackDraft = (): OutcomePlannerFeedbackDraft => ({
  actualOutcome: '',
  actualFeesCollected: '',
  actualCost: '',
  actualMargin: '',
  notes: ''
});

const round2 = (value: number) => Math.round((Number.isFinite(value) ? value : 0) * 100) / 100;

const mapFeeStructureToPlannerArrangement = (feeStructure?: FeeStructure | string): 'hourly' | 'fixed' | 'hybrid' | 'contingency' => {
  if (feeStructure === FeeStructure.FlatFee || String(feeStructure).toLowerCase().includes('flat')) return 'fixed';
  if (feeStructure === FeeStructure.Contingency || String(feeStructure).toLowerCase().includes('contingency')) return 'contingency';
  return 'hourly';
};

const buildOutcomePlannerPreview = (params: {
  complexity: PlannerComplexity;
  claimSizeBand: PlannerClaimSizeBand;
  primaryPayorProfile: PlannerPayorProfile;
  jurisdictionCode?: string;
  courtType?: string;
  billingArrangement: 'hourly' | 'fixed' | 'hybrid' | 'contingency';
  billableRate: number;
  practiceArea?: string;
}): OutcomePlannerPreview => {
  const complexityMultiplier = params.complexity === 'low' ? 0.75 : params.complexity === 'high' ? 1.35 : 1.0;
  const claimSizeMultiplier = params.claimSizeBand === 'small' ? 0.70 : params.claimSizeBand === 'large' ? 1.25 : params.claimSizeBand === 'enterprise' ? 1.60 : 1.0;
  const courtMultiplier = params.courtType?.toLowerCase().includes('federal') ? 1.20 : 1.0;
  const arrangementMultiplier = params.billingArrangement === 'fixed' ? 0.90 : params.billingArrangement === 'contingency' ? 1.10 : params.billingArrangement === 'hybrid' ? 1.05 : 1.0;
  const rate = round2(params.billableRate > 0 ? params.billableRate : 275);
  const baseHours = round2(120 * complexityMultiplier * claimSizeMultiplier * courtMultiplier * arrangementMultiplier);

  const defs = [
    { key: 'conservative', name: 'Conservative', probability: 0.25, hoursMult: 1.15, collectMult: 0.84, confidence: 0.60 },
    { key: 'base', name: 'Base', probability: 0.50, hoursMult: 1.00, collectMult: 0.90, confidence: 0.72 },
    { key: 'aggressive', name: 'Aggressive', probability: 0.25, hoursMult: 0.85, collectMult: 0.95, confidence: 0.66 }
  ];

  const payorAdj = params.primaryPayorProfile === 'corporate' ? 0.03 : params.primaryPayorProfile === 'third_party' ? -0.04 : 0;
  const scenarios = defs.map((def) => {
    const totalHours = round2(baseHours * def.hoursMult);
    const feeTotal = round2(totalHours * rate);
    const expenseTotal = round2(feeTotal * 0.08);
    const budgetTotal = round2(feeTotal + expenseTotal);
    const collectedRatio = Math.min(0.99, Math.max(0.50, def.collectMult + payorAdj));
    const expectedCollected = round2(budgetTotal * collectedRatio);
    const expectedCost = round2((feeTotal * 0.42) + (expenseTotal * 0.55));
    const expectedMargin = round2(expectedCollected - expectedCost);
    const dataCoverageScore = round2(
      0.35 +
      (params.jurisdictionCode ? 0.15 : 0) +
      (params.courtType ? 0.15 : 0) +
      (params.practiceArea ? 0.10 : 0) +
      (rate > 0 ? 0.10 : 0) +
      0.10
    );
    const confidenceScore = round2(Math.min(1, Math.max(0, def.confidence * 0.75 + dataCoverageScore * 0.25)));
    const confidenceBand: 'low' | 'medium' | 'high' = confidenceScore >= 0.8 ? 'high' : confidenceScore >= 0.6 ? 'medium' : 'low';
    const outcomeProbabilities = {
      settle: round2(def.key === 'aggressive' ? 0.62 : def.key === 'conservative' ? 0.42 : 0.54),
      dismiss: round2(def.key === 'aggressive' ? 0.10 : def.key === 'conservative' ? 0.12 : 0.11),
      trial: round2(def.key === 'aggressive' ? 0.18 : def.key === 'conservative' ? 0.28 : 0.22),
      adverse: round2(def.key === 'aggressive' ? 0.10 : def.key === 'conservative' ? 0.18 : 0.13)
    };
    const riskFlags = [
      ...(dataCoverageScore < 0.6 ? ['low_data_coverage'] : []),
      ...(!params.jurisdictionCode ? ['jurisdiction_gap'] : []),
      ...((params.primaryPayorProfile === 'third_party' || collectedRatio < 0.86) ? ['high_collections_risk'] : []),
      ...((params.claimSizeBand === 'enterprise' || (params.complexity === 'high' && def.key === 'conservative')) ? ['atypical_matter'] : [])
    ];
    const topDrivers = [
      { key: 'complexity', impact: round2(complexityMultiplier - 1) },
      { key: 'claim_size_band', impact: round2(claimSizeMultiplier - 1) },
      { key: 'court_type', impact: round2(courtMultiplier - 1) },
      { key: 'billing_arrangement', impact: round2(arrangementMultiplier - 1) },
      { key: 'payor_mix', impact: round2(payorAdj) }
    ].sort((a, b) => Math.abs(b.impact) - Math.abs(a.impact));
    return {
      key: def.key,
      name: def.name,
      probability: def.probability,
      budgetTotal,
      expectedCollected,
      expectedMargin,
      confidenceScore,
      confidenceBand,
      dataCoverageScore,
      riskFlags,
      outcomeProbabilities,
      topDrivers,
      inputSensitivitySummary: `${params.billingArrangement} + ${params.primaryPayorProfile} + ${params.complexity}`,
      driverSummary: `${params.complexity} complexity • ${params.claimSizeBand} claim • ${params.billingArrangement} • ${params.primaryPayorProfile}`
    };
  });

  return {
    scenarios,
    assumptions: [
      { key: 'complexity', value: params.complexity },
      { key: 'claim_size_band', value: params.claimSizeBand },
      { key: 'billing_arrangement', value: params.billingArrangement },
      { key: 'primary_payor_profile', value: params.primaryPayorProfile },
      { key: 'court_type', value: params.courtType || 'unspecified' },
      { key: 'practice_area', value: params.practiceArea || 'unspecified' },
      { key: 'base_billable_rate', value: rate.toFixed(2) }
    ]
  };
};

const buildInitialMatterForm = () => ({
  name: '',
  caseNumber: '',
  practiceArea: PracticeArea.CivilLitigation,
  feeStructure: FeeStructure.Hourly,
  partyId: '',
  partyType: 'client' as 'client' | 'lead',
  relatedClientIds: [] as string[],
  trustAmount: '' as string | number,
  courtType: '',
  bailStatus: 'None',
  bailAmount: '' as string | number,
  outcome: '',
  shareWithFirm: false,
  shareBillingWithFirm: false,
  shareNotesWithFirm: false,
  entityId: '',
  officeId: '',
  opposingPartyName: '',
  opposingPartyType: 'Individual' as 'Individual' | 'Corporation' | 'Government',
  opposingPartyCompany: '',
  opposingCounselName: '',
  opposingCounselFirm: '',
  opposingCounselEmail: ''
});

type MatterFormState = ReturnType<typeof buildInitialMatterForm>;

const buildInitialNewClientData = () => ({
  name: '',
  email: '',
  phone: '',
  mobile: '',
  company: '',
  type: 'Individual' as 'Individual' | 'Corporate',
  status: 'Active' as 'Active' | 'Inactive',
  address: '',
  city: '',
  state: '',
  zipCode: '',
  country: '',
  taxId: '',
  notes: '',
  password: ''
});

type NewMatterClientFormState = ReturnType<typeof buildInitialNewClientData>;

type MatterAnalysisSectionProps = {
  title: string;
  subtitle?: string;
  actions?: React.ReactNode;
  defaultExpanded?: boolean;
  className?: string;
  titleClassName?: string;
  children: React.ReactNode;
};

const MatterAnalysisSection: React.FC<MatterAnalysisSectionProps> = ({
  title,
  subtitle,
  actions,
  defaultExpanded = true,
  className = 'bg-white border border-slate-200 rounded-lg p-3',
  titleClassName = 'text-[11px] font-bold uppercase text-slate-500',
  children
}) => {
  const [expanded, setExpanded] = useState(defaultExpanded);

  return (
    <div className={className}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className={titleClassName}>{title}</p>
          {subtitle && <p className="mt-1 text-[11px] text-gray-500">{subtitle}</p>}
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {actions}
          <button
            type="button"
            onClick={() => setExpanded(prev => !prev)}
            className="inline-flex items-center gap-1 rounded border border-slate-200 bg-white px-2 py-1 text-[11px] font-bold text-slate-600 hover:bg-slate-50"
          >
            <ChevronRight className={`w-3 h-3 transition-transform ${expanded ? 'rotate-90' : ''}`} />
            {expanded ? 'Hide' : 'Show'}
          </button>
        </div>
      </div>
      {expanded && <div className="mt-3">{children}</div>}
    </div>
  );
};

const Matters: React.FC = () => {
  const matterSecondaryClientsEnabled = false;
  const { t, formatCurrency, formatDate } = useTranslation();
  const { matters, clients, leads, addMatter, updateMatter, deleteMatter, addClient, timeEntries, documents, messages, tasks } = useData();
  const [showModal, setShowModal] = useState(false);
  const [selectedMatter, setSelectedMatter] = useState<Matter | null>(null);
  const [showDocs, setShowDocs] = useState(false);
  const [editData, setEditData] = useState<Partial<Matter> | null>(null);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | CaseStatus>('all');
  const [viewingDoc, setViewingDoc] = useState<DocumentFile | null>(null);
  const [docContent, setDocContent] = useState<string>('');
  const [loadingContent, setLoadingContent] = useState(false);
  const [docObjectUrl, setDocObjectUrl] = useState<string | null>(null);
  const [showNewClientModal, setShowNewClientModal] = useState(false);
  const [showClientSelector, setShowClientSelector] = useState(false);
  const [selectedPartyName, setSelectedPartyName] = useState('');
  const [entityFilter, setEntityFilter] = useState('');
  const [officeFilter, setOfficeFilter] = useState('');
  const [matterSubmitting, setMatterSubmitting] = useState(false);
  const [inlineClientSubmitting, setInlineClientSubmitting] = useState(false);
  const [showOutcomePlannerPreview, setShowOutcomePlannerPreview] = useState(true);
  const [outcomePlannerDraft, setOutcomePlannerDraft] = useState<OutcomePlannerDraft>(defaultOutcomePlannerDraft);
  const latestCreatedClientRef = useRef<Client | null>(null);
  const [selectedMatterPlanner, setSelectedMatterPlanner] = useState<OutcomeFeePlanDetailResult | null>(null);
  const [selectedMatterPlannerCompare, setSelectedMatterPlannerCompare] = useState<OutcomeFeePlanVersionCompareResult | null>(null);
  const [selectedMatterPlannerLoading, setSelectedMatterPlannerLoading] = useState(false);
  const [selectedMatterPlannerRecomputing, setSelectedMatterPlannerRecomputing] = useState(false);
  const [outcomePlannerPortfolioMetrics, setOutcomePlannerPortfolioMetrics] = useState<OutcomeFeePlanPortfolioMetricsResult | null>(null);
  const [outcomePlannerPortfolioMetricsLoading, setOutcomePlannerPortfolioMetricsLoading] = useState(false);
  const [selectedMatterCalibration, setSelectedMatterCalibration] = useState<OutcomeFeeCalibrationEffectiveResult | null>(null);
  const [selectedMatterCalibrationLoading, setSelectedMatterCalibrationLoading] = useState(false);
  const [selectedMatterCalibrationJobRunning, setSelectedMatterCalibrationJobRunning] = useState(false);
  const [selectedMatterCalibrationActionRunning, setSelectedMatterCalibrationActionRunning] = useState(false);
  const [selectedMatterOutcomeFeedbackSaving, setSelectedMatterOutcomeFeedbackSaving] = useState(false);
  const [selectedMatterOutcomeFeedbackDraft, setSelectedMatterOutcomeFeedbackDraft] = useState<OutcomePlannerFeedbackDraft>(defaultOutcomePlannerFeedbackDraft);
  const [selectedMatterTransparencyWorkspace, setSelectedMatterTransparencyWorkspace] = useState<any | null>(null);
  const [selectedMatterTransparencyLoading, setSelectedMatterTransparencyLoading] = useState(false);
  const [selectedMatterTransparencyPolicySaving, setSelectedMatterTransparencyPolicySaving] = useState(false);
  const [selectedMatterTransparencyActionSaving, setSelectedMatterTransparencyActionSaving] = useState(false);
  const [selectedMatterTransparencyEvidenceMetrics, setSelectedMatterTransparencyEvidenceMetrics] = useState<any | null>(null);
  const [selectedMatterTransparencyEvidenceLoading, setSelectedMatterTransparencyEvidenceLoading] = useState(false);
  const [selectedMatterTransparencyEvidenceActionLoading, setSelectedMatterTransparencyEvidenceActionLoading] = useState(false);
  const [selectedMatterTransparencyDraftEvidence, setSelectedMatterTransparencyDraftEvidence] = useState<any | null>(null);
  const [selectedMatterTransparencyPublishedEvidence, setSelectedMatterTransparencyPublishedEvidence] = useState<any | null>(null);
  const [selectedMatterTransparencyPolicyDraft, setSelectedMatterTransparencyPolicyDraft] = useState({
    publishPolicy: 'warn_only',
    autoPublishSafe: false,
    reviewRequiredForDelayReason: false,
    reviewRequiredForCostImpactChange: false,
    costImpactChangeThreshold: '1000',
    blockOnLowConfidence: false,
    lowConfidenceThreshold: '0.55'
  });
  const [selectedMatterTransparencyRewriteDraft, setSelectedMatterTransparencyRewriteDraft] = useState({
    snapshotSummary: '',
    whatChangedSummary: '',
    nextStepActionText: '',
    nextStepBlockedByText: '',
    reason: '',
    assignedTo: ''
  });
  const [selectedMatterTransparencyDelayReasonEdits, setSelectedMatterTransparencyDelayReasonEdits] = useState<Record<string, string>>({});
  const [newClientData, setNewClientData] = useState<NewMatterClientFormState>(buildInitialNewClientData);

  const clientsById = useMemo(() => {
    const safeClients = clients.filter((client): client is Client => !!client && typeof client.id === 'string');
    return new Map(safeClients.map((client) => [client.id, client]));
  }, [clients]);

  const resolveMatterClient = (matter: Matter): Client | undefined => {
    if (!matter) return undefined;
    const candidateId = matter.clientId || matter.client?.id;
    if (!candidateId) return matter.client;
    return clientsById.get(candidateId) || matter.client;
  };

  // Deep-link from Command Palette
  useEffect(() => {
    const targetId = localStorage.getItem('cmd_target_matter');
    if (!targetId) return;
    const target = matters.find(m => m.id === targetId);
    if (target) {
      setSelectedMatter(target);
      localStorage.removeItem('cmd_target_matter');
    }
  }, [matters]);

  useEffect(() => {
    if (!selectedMatter?.id) return;
    const refreshedMatter = matters.find((matter) => matter.id === selectedMatter.id);
    if (refreshedMatter && refreshedMatter !== selectedMatter) {
      setSelectedMatter(refreshedMatter);
    }
  }, [matters, selectedMatter]);

  useEffect(() => {
    void refreshOutcomePlannerPortfolioMetrics();
  }, []);

  useEffect(() => {
    let cancelled = false;

    const loadPlanner = async () => {
      if (!selectedMatter?.id) {
        setSelectedMatterPlanner(null);
        setSelectedMatterPlannerCompare(null);
        setSelectedMatterPlannerLoading(false);
        return;
      }

      setSelectedMatterPlannerLoading(true);
      try {
        const detail = await api.outcomeFeePlans.getLatestForMatter(selectedMatter.id);
        if (cancelled) return;
        setSelectedMatterPlanner(detail || null);

        if (detail?.plan?.id && (detail.versions?.length || 0) > 1) {
          const compare = await api.outcomeFeePlans.compare(detail.plan.id);
          if (cancelled) return;
          setSelectedMatterPlannerCompare(compare || null);
        } else {
          setSelectedMatterPlannerCompare(null);
        }
      } catch (error) {
        console.error('Failed to load Outcome-to-Fee planner detail', error);
        if (!cancelled) {
          setSelectedMatterPlanner(null);
          setSelectedMatterPlannerCompare(null);
        }
      } finally {
        if (!cancelled) {
          setSelectedMatterPlannerLoading(false);
        }
      }
    };

    loadPlanner();
    return () => {
      cancelled = true;
    };
  }, [selectedMatter?.id]);

  useEffect(() => {
    let cancelled = false;

    const syncDraftsFromWorkspace = (workspace: any | null) => {
      const policy = workspace?.policy || {};
      setSelectedMatterTransparencyPolicyDraft({
        publishPolicy: String(policy.publishPolicy || 'warn_only'),
        autoPublishSafe: !!policy.autoPublishSafe,
        reviewRequiredForDelayReason: !!policy.reviewRequiredForDelayReason,
        reviewRequiredForCostImpactChange: !!policy.reviewRequiredForCostImpactChange,
        costImpactChangeThreshold: String(policy.costImpactChangeThreshold ?? '1000'),
        blockOnLowConfidence: !!policy.blockOnLowConfidence,
        lowConfidenceThreshold: String(policy.lowConfidenceThreshold ?? '0.55')
      });

      const draft = workspace?.draft;
      const delayRows = (draft?.delayReasons || []) as Array<any>;
      setSelectedMatterTransparencyDelayReasonEdits(
        delayRows.reduce((acc: Record<string, string>, row) => {
          if (row?.id) acc[String(row.id)] = String(row.clientSafeText ?? row.text ?? '');
          return acc;
        }, {})
      );
      setSelectedMatterTransparencyRewriteDraft({
        snapshotSummary: String(draft?.snapshot?.snapshotSummary ?? draft?.snapshot?.summary ?? ''),
        whatChangedSummary: String(draft?.snapshot?.whatChangedSummary ?? draft?.snapshot?.whatChanged ?? ''),
        nextStepActionText: String(draft?.nextStep?.actionText ?? ''),
        nextStepBlockedByText: String(draft?.nextStep?.blockedByText ?? ''),
        reason: '',
        assignedTo: String(workspace?.pendingReviewItem?.assignedTo ?? '')
      });
    };

      const loadTransparencyWorkspace = async () => {
        if (!selectedMatter?.id) {
          setSelectedMatterTransparencyWorkspace(null);
          setSelectedMatterTransparencyEvidenceMetrics(null);
          setSelectedMatterTransparencyDraftEvidence(null);
          setSelectedMatterTransparencyPublishedEvidence(null);
          setSelectedMatterTransparencyLoading(false);
          setSelectedMatterTransparencyEvidenceLoading(false);
          return;
        }

        setSelectedMatterTransparencyLoading(true);
        setSelectedMatterTransparencyEvidenceLoading(true);
        try {
          const workspace = await api.clientTransparency.getReviewWorkspace(selectedMatter.id);
          if (cancelled) return;
          setSelectedMatterTransparencyWorkspace(workspace || null);
          syncDraftsFromWorkspace(workspace || null);
          const [metrics, draftEvidence, publishedEvidence] = await Promise.all([
            api.clientTransparency.metrics({ days: 90, matterId: selectedMatter.id }).catch(() => null),
            workspace?.draft?.snapshot?.id ? api.clientTransparency.getSnapshotEvidence(String(workspace.draft.snapshot.id)).catch(() => null) : Promise.resolve(null),
            workspace?.published?.snapshot?.id ? api.clientTransparency.getSnapshotEvidence(String(workspace.published.snapshot.id)).catch(() => null) : Promise.resolve(null)
          ]);
          if (cancelled) return;
          setSelectedMatterTransparencyEvidenceMetrics(metrics || null);
          setSelectedMatterTransparencyDraftEvidence(draftEvidence || null);
          setSelectedMatterTransparencyPublishedEvidence(publishedEvidence || null);
        } catch (error) {
          console.error('Failed to load client transparency review workspace', error);
          if (!cancelled) {
            setSelectedMatterTransparencyWorkspace(null);
            setSelectedMatterTransparencyEvidenceMetrics(null);
            setSelectedMatterTransparencyDraftEvidence(null);
            setSelectedMatterTransparencyPublishedEvidence(null);
          }
        } finally {
          if (!cancelled) {
            setSelectedMatterTransparencyLoading(false);
            setSelectedMatterTransparencyEvidenceLoading(false);
          }
        }
      };

    loadTransparencyWorkspace();
    return () => {
      cancelled = true;
    };
  }, [selectedMatter?.id]);

  useEffect(() => {
    let cancelled = false;

    const loadCalibration = async () => {
      if (!selectedMatter?.id) {
        setSelectedMatterCalibration(null);
        setSelectedMatterCalibrationLoading(false);
        setSelectedMatterOutcomeFeedbackDraft(defaultOutcomePlannerFeedbackDraft());
        return;
      }

      setSelectedMatterCalibrationLoading(true);
      try {
        const result = await api.outcomeFeePlans.getEffectiveCalibrationForMatter(selectedMatter.id);
        if (cancelled) return;
        setSelectedMatterCalibration(result || null);
      } catch (error) {
        console.error('Failed to load Outcome-to-Fee planner calibration', error);
        if (!cancelled) {
          setSelectedMatterCalibration(null);
        }
      } finally {
        if (!cancelled) {
          setSelectedMatterCalibrationLoading(false);
        }
      }
    };

    setSelectedMatterOutcomeFeedbackDraft(defaultOutcomePlannerFeedbackDraft());
    loadCalibration();
    return () => {
      cancelled = true;
    };
  }, [selectedMatter?.id]);

  // Form State
  const [formData, setFormData] = useState<MatterFormState>(buildInitialMatterForm);

  // US Court Types from enum
  const courtOptions = Object.values(CourtType);

  const formEntityId = editData?.entityId ?? formData.entityId;
  const formOfficeId = editData?.officeId ?? formData.officeId;
  const primaryClientId = editData
    ? (resolveMatterClient(editData as Matter)?.id || editData.clientId || '')
    : (formData.partyType === 'client' ? formData.partyId : '');
  const additionalClientOptions = useMemo(
    () => clients.filter((client) => client.id !== primaryClientId),
    [clients, primaryClientId]
  );

  const toggleRelatedClientId = (clientId: string, checked: boolean) => {
    if (editData) {
      const currentIds = Array.isArray(editData.relatedClientIds) ? editData.relatedClientIds : [];
      const nextIds = checked
        ? Array.from(new Set([...currentIds, clientId]))
        : currentIds.filter((id) => id !== clientId);
      setEditData({ ...editData, relatedClientIds: nextIds });
      return;
    }

    const currentIds = Array.isArray(formData.relatedClientIds) ? formData.relatedClientIds : [];
    const nextIds = checked
      ? Array.from(new Set([...currentIds, clientId]))
      : currentIds.filter((id) => id !== clientId);
    setFormData(prev => ({ ...prev, relatedClientIds: nextIds }));
  };

  const outcomePlannerPreview = useMemo(() => {
    if (!outcomePlannerDraft.enabled || !!editData) return null;
    const billableRate =
      parseFloat(outcomePlannerDraft.baseBillableRateOverride) ||
      (editData?.billableRate ?? 400);
    return buildOutcomePlannerPreview({
      complexity: outcomePlannerDraft.complexity,
      claimSizeBand: outcomePlannerDraft.claimSizeBand,
      primaryPayorProfile: outcomePlannerDraft.primaryPayorProfile,
      jurisdictionCode: outcomePlannerDraft.jurisdictionCode,
      courtType: editData?.courtType || formData.courtType,
      billingArrangement: mapFeeStructureToPlannerArrangement((editData?.feeStructure as any) || formData.feeStructure),
      billableRate,
      practiceArea: String(editData?.practiceArea || formData.practiceArea || '')
    });
  }, [outcomePlannerDraft, editData, formData.courtType, formData.feeStructure, formData.practiceArea]);

  const resetMatterDraft = () => {
    setFormData(buildInitialMatterForm());
    setSelectedPartyName('');
    latestCreatedClientRef.current = null;
  };

  const resetInlineClientForm = () => {
    setNewClientData(buildInitialNewClientData());
  };

  const closeNewClientModal = () => {
    setShowNewClientModal(false);
    resetInlineClientForm();
  };

  const resetOutcomePlannerState = () => {
    setOutcomePlannerDraft(defaultOutcomePlannerDraft());
    setShowOutcomePlannerPreview(true);
  };

  const closeMatterModal = () => {
    setShowModal(false);
    setEditData(null);
    resetOutcomePlannerState();
    resetMatterDraft();
  };

  const persistOutcomePlannerForMatter = async (createdMatter: any, plannerDraft: OutcomePlannerDraft = outcomePlannerDraft) => {
    const baseRateOverride = parseFloat(plannerDraft.baseBillableRateOverride);
    const payload = {
      matterId: createdMatter.id,
      title: `${createdMatter.name} Intake Planner`,
      complexity: plannerDraft.complexity,
      claimSizeBand: plannerDraft.claimSizeBand,
      billingArrangement: mapFeeStructureToPlannerArrangement(createdMatter.feeStructure || formData.feeStructure),
      primaryPayorProfile: plannerDraft.primaryPayorProfile,
      jurisdictionCode: plannerDraft.jurisdictionCode || undefined,
      baseBillableRateOverride: Number.isFinite(baseRateOverride) && baseRateOverride > 0 ? baseRateOverride : undefined,
      notes: plannerDraft.notes || undefined
    };

    const result = await api.outcomeFeePlans.generate(payload);
    if (result?.currentVersion?.versionNumber) {
      toast.success(`Outcome-to-Fee Planner v${result.currentVersion.versionNumber} saved.`);
    } else {
      toast.success('Outcome-to-Fee Planner saved.');
    }
  };

  const refreshSelectedMatterPlanner = async (matterId: string, showErrors = false) => {
    try {
      const detail = await api.outcomeFeePlans.getLatestForMatter(matterId);
      setSelectedMatterPlanner(detail || null);

      if (detail?.plan?.id && (detail.versions?.length || 0) > 1) {
        const compare = await api.outcomeFeePlans.compare(detail.plan.id);
        setSelectedMatterPlannerCompare(compare || null);
      } else {
        setSelectedMatterPlannerCompare(null);
      }
    } catch (error) {
      console.error('Failed to refresh Outcome-to-Fee planner detail', error);
      if (showErrors) {
        toast.error('Failed to refresh planner details.');
      }
    }
  };

  const refreshOutcomePlannerPortfolioMetrics = async (showErrors = false) => {
    setOutcomePlannerPortfolioMetricsLoading(true);
    try {
      const metrics = await api.outcomeFeePlans.metrics({ days: 90 });
      setOutcomePlannerPortfolioMetrics(metrics || null);
    } catch (error) {
      console.error('Failed to load Outcome-to-Fee planner portfolio metrics', error);
      if (showErrors) {
        toast.error('Failed to load planner portfolio metrics.');
      }
    } finally {
      setOutcomePlannerPortfolioMetricsLoading(false);
    }
  };

  const refreshSelectedMatterCalibration = async (matterId: string, showErrors = false) => {
    setSelectedMatterCalibrationLoading(true);
    try {
      const result = await api.outcomeFeePlans.getEffectiveCalibrationForMatter(matterId);
      setSelectedMatterCalibration(result || null);
    } catch (error) {
      console.error('Failed to refresh planner calibration', error);
      if (showErrors) {
        toast.error('Failed to refresh calibration details.');
      }
    } finally {
      setSelectedMatterCalibrationLoading(false);
    }
  };

  const refreshSelectedMatterTransparencyWorkspace = async (matterId: string, showErrors = false) => {
    setSelectedMatterTransparencyLoading(true);
    setSelectedMatterTransparencyEvidenceLoading(true);
    try {
      const workspace = await api.clientTransparency.getReviewWorkspace(matterId);
      setSelectedMatterTransparencyWorkspace(workspace || null);
      const policy = workspace?.policy || {};
      setSelectedMatterTransparencyPolicyDraft({
        publishPolicy: String(policy.publishPolicy || 'warn_only'),
        autoPublishSafe: !!policy.autoPublishSafe,
        reviewRequiredForDelayReason: !!policy.reviewRequiredForDelayReason,
        reviewRequiredForCostImpactChange: !!policy.reviewRequiredForCostImpactChange,
        costImpactChangeThreshold: String(policy.costImpactChangeThreshold ?? '1000'),
        blockOnLowConfidence: !!policy.blockOnLowConfidence,
        lowConfidenceThreshold: String(policy.lowConfidenceThreshold ?? '0.55')
      });
      const draft = workspace?.draft;
      const delayRows = (draft?.delayReasons || []) as Array<any>;
      setSelectedMatterTransparencyDelayReasonEdits(
        delayRows.reduce((acc: Record<string, string>, row) => {
          if (row?.id) acc[String(row.id)] = String(row.clientSafeText ?? row.text ?? '');
          return acc;
        }, {})
      );
      setSelectedMatterTransparencyRewriteDraft(prev => ({
        ...prev,
        snapshotSummary: String(draft?.snapshot?.snapshotSummary ?? draft?.snapshot?.summary ?? ''),
        whatChangedSummary: String(draft?.snapshot?.whatChangedSummary ?? draft?.snapshot?.whatChanged ?? ''),
        nextStepActionText: String(draft?.nextStep?.actionText ?? ''),
        nextStepBlockedByText: String(draft?.nextStep?.blockedByText ?? ''),
        assignedTo: String(workspace?.pendingReviewItem?.assignedTo ?? '')
      }));

      const [metrics, draftEvidence, publishedEvidence] = await Promise.all([
        api.clientTransparency.metrics({ days: 90, matterId }).catch(() => null),
        workspace?.draft?.snapshot?.id ? api.clientTransparency.getSnapshotEvidence(String(workspace.draft.snapshot.id)).catch(() => null) : Promise.resolve(null),
        workspace?.published?.snapshot?.id ? api.clientTransparency.getSnapshotEvidence(String(workspace.published.snapshot.id)).catch(() => null) : Promise.resolve(null)
      ]);
      setSelectedMatterTransparencyEvidenceMetrics(metrics || null);
      setSelectedMatterTransparencyDraftEvidence(draftEvidence || null);
      setSelectedMatterTransparencyPublishedEvidence(publishedEvidence || null);
    } catch (error) {
      console.error('Failed to refresh client transparency workspace', error);
      if (showErrors) toast.error('Failed to refresh client transparency review workspace.');
    } finally {
      setSelectedMatterTransparencyLoading(false);
      setSelectedMatterTransparencyEvidenceLoading(false);
    }
  };

  const reverifySelectedMatterTransparencyEvidence = async () => {
    if (!selectedMatter?.id) return;
    setSelectedMatterTransparencyEvidenceActionLoading(true);
    try {
      const result = await api.clientTransparency.batchReverifyEvidence({
        matterId: selectedMatter.id,
        onlyPublished: false,
        onlyCurrent: false,
        days: 365,
        limit: 20
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success(`Evidence reverify completed (${Number((result as any)?.reverified || 0)} snapshots).`);
    } catch (error) {
      console.error('Failed to batch reverify transparency evidence', error);
      toast.error('Failed to reverify transparency evidence.');
    } finally {
      setSelectedMatterTransparencyEvidenceActionLoading(false);
    }
  };

  const saveSelectedMatterTransparencyPolicy = async () => {
    if (!selectedMatter?.id) return;
    setSelectedMatterTransparencyPolicySaving(true);
    try {
      await api.clientTransparency.upsertMatterPolicy(selectedMatter.id, {
        publishPolicy: selectedMatterTransparencyPolicyDraft.publishPolicy,
        autoPublishSafe: selectedMatterTransparencyPolicyDraft.autoPublishSafe,
        reviewRequiredForDelayReason: selectedMatterTransparencyPolicyDraft.reviewRequiredForDelayReason,
        reviewRequiredForCostImpactChange: selectedMatterTransparencyPolicyDraft.reviewRequiredForCostImpactChange,
        costImpactChangeThreshold: parseFloat(selectedMatterTransparencyPolicyDraft.costImpactChangeThreshold) || 1000,
        blockOnLowConfidence: selectedMatterTransparencyPolicyDraft.blockOnLowConfidence,
        lowConfidenceThreshold: parseFloat(selectedMatterTransparencyPolicyDraft.lowConfidenceThreshold) || 0.55
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success('Transparency publish policy saved.');
    } catch (error) {
      console.error('Failed to save client transparency policy', error);
      toast.error('Failed to save transparency publish policy.');
    } finally {
      setSelectedMatterTransparencyPolicySaving(false);
    }
  };

  const rewriteSelectedMatterTransparencyDraft = async () => {
    const snapshotId = selectedMatterTransparencyWorkspace?.draft?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterTransparencyActionSaving(true);
    try {
      const draftDelayReasons = (selectedMatterTransparencyWorkspace?.draft?.delayReasons || []) as Array<any>;
      await api.clientTransparency.reviewSnapshot(snapshotId, {
        action: 'rewrite',
        reason: selectedMatterTransparencyRewriteDraft.reason || 'Client-safe wording refinement',
        assignedTo: selectedMatterTransparencyRewriteDraft.assignedTo || undefined,
        snapshotSummary: selectedMatterTransparencyRewriteDraft.snapshotSummary || undefined,
        whatChangedSummary: selectedMatterTransparencyRewriteDraft.whatChangedSummary || undefined,
        nextStepActionText: selectedMatterTransparencyRewriteDraft.nextStepActionText || undefined,
        nextStepBlockedByText: selectedMatterTransparencyRewriteDraft.nextStepBlockedByText || undefined,
        delayReasonTextUpdates: draftDelayReasons.map((d) => ({ id: d.id, clientSafeText: selectedMatterTransparencyDelayReasonEdits[String(d.id)] ?? d.clientSafeText ?? d.text ?? '' }))
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success('Transparency draft rewrite saved.');
    } catch (error) {
      console.error('Failed to rewrite transparency draft', error);
      toast.error('Failed to save transparency rewrite.');
    } finally {
      setSelectedMatterTransparencyActionSaving(false);
    }
  };

  const approveAndPublishSelectedMatterTransparency = async () => {
    const snapshotId = selectedMatterTransparencyWorkspace?.draft?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterTransparencyActionSaving(true);
    try {
      const draftDelayReasons = (selectedMatterTransparencyWorkspace?.draft?.delayReasons || []) as Array<any>;
      await api.clientTransparency.reviewSnapshot(snapshotId, {
        action: 'approve',
        reason: selectedMatterTransparencyRewriteDraft.reason || 'Approved for client portal',
        assignedTo: selectedMatterTransparencyRewriteDraft.assignedTo || undefined,
        snapshotSummary: selectedMatterTransparencyRewriteDraft.snapshotSummary || undefined,
        whatChangedSummary: selectedMatterTransparencyRewriteDraft.whatChangedSummary || undefined,
        nextStepActionText: selectedMatterTransparencyRewriteDraft.nextStepActionText || undefined,
        nextStepBlockedByText: selectedMatterTransparencyRewriteDraft.nextStepBlockedByText || undefined,
        delayReasonTextUpdates: draftDelayReasons.map((d) => ({ id: d.id, clientSafeText: selectedMatterTransparencyDelayReasonEdits[String(d.id)] ?? d.clientSafeText ?? d.text ?? '' })),
        publishAfter: true
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success('Transparency snapshot approved and published.');
    } catch (error: any) {
      console.error('Failed to approve/publish transparency snapshot', error);
      const msg = String(error?.message || '');
      if (msg.toLowerCase().includes('conflict') || msg.toLowerCase().includes('blocked by policy')) {
        toast.error('Publish blocked by policy. Use Override Publish if appropriate.');
      } else {
        toast.error('Failed to approve/publish transparency snapshot.');
      }
    } finally {
      setSelectedMatterTransparencyActionSaving(false);
    }
  };

  const rejectSelectedMatterTransparency = async () => {
    const snapshotId = selectedMatterTransparencyWorkspace?.draft?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterTransparencyActionSaving(true);
    try {
      await api.clientTransparency.reviewSnapshot(snapshotId, {
        action: 'reject',
        reason: selectedMatterTransparencyRewriteDraft.reason || 'Rejected - requires further internal revision',
        assignedTo: selectedMatterTransparencyRewriteDraft.assignedTo || undefined
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success('Transparency snapshot rejected.');
    } catch (error) {
      console.error('Failed to reject transparency snapshot', error);
      toast.error('Failed to reject transparency snapshot.');
    } finally {
      setSelectedMatterTransparencyActionSaving(false);
    }
  };

  const overridePublishSelectedMatterTransparency = async () => {
    const snapshotId = selectedMatterTransparencyWorkspace?.draft?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterTransparencyActionSaving(true);
    try {
      await api.clientTransparency.publishSnapshot(snapshotId, {
        reason: selectedMatterTransparencyRewriteDraft.reason || 'Override publish from reviewer panel',
        overridePolicy: true,
        approverReason: selectedMatterTransparencyRewriteDraft.reason || 'Reviewed and approved override'
      });
      await refreshSelectedMatterTransparencyWorkspace(selectedMatter.id);
      toast.success('Transparency snapshot published with override.');
    } catch (error) {
      console.error('Failed to override publish transparency snapshot', error);
      toast.error('Failed to override publish transparency snapshot.');
    } finally {
      setSelectedMatterTransparencyActionSaving(false);
    }
  };

  const runOutcomePlannerCalibrationShadowJob = async () => {
    if (!selectedMatter?.id) return;
    setSelectedMatterCalibrationJobRunning(true);
    try {
      const result = await api.outcomeFeePlans.runCalibrationJob({
        days: 365,
        minSampleSize: 5,
        shadowMode: true,
        autoActivateHighConfidence: false,
        cohortScopes: ['combined', 'practice_court_arrangement', 'practice_arrangement'],
        notes: `Triggered from matter ${selectedMatter.id} detail`
      });
      await refreshSelectedMatterCalibration(selectedMatter.id);
      toast.success(`Calibration job completed (${result?.created ?? 0} snapshots, ${result?.autoActivated ?? 0} auto-activated).`);
    } catch (error) {
      console.error('Outcome-to-Fee calibration job failed', error);
      toast.error('Calibration job failed.');
    } finally {
      setSelectedMatterCalibrationJobRunning(false);
    }
  };

  const activateSelectedMatterShadowCalibration = async () => {
    const snapshotId = selectedMatterCalibration?.shadow?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterCalibrationActionRunning(true);
    try {
      await api.outcomeFeePlans.activateCalibrationSnapshot(snapshotId, {
        asShadow: false,
        reason: 'Promoted shadow calibration from matter planner panel'
      });
      await refreshSelectedMatterCalibration(selectedMatter.id);
      toast.success('Shadow calibration promoted to active.');
    } catch (error) {
      console.error('Failed to activate calibration snapshot', error);
      toast.error('Failed to promote shadow calibration.');
    } finally {
      setSelectedMatterCalibrationActionRunning(false);
    }
  };

  const rollbackSelectedMatterActiveCalibration = async () => {
    const snapshotId = selectedMatterCalibration?.active?.snapshot?.id;
    if (!snapshotId || !selectedMatter?.id) return;
    setSelectedMatterCalibrationActionRunning(true);
    try {
      await api.outcomeFeePlans.rollbackCalibrationSnapshot(snapshotId, {
        reason: 'Rollback from matter planner panel'
      });
      await refreshSelectedMatterCalibration(selectedMatter.id);
      toast.success('Calibration rollback applied.');
    } catch (error) {
      console.error('Failed to rollback calibration snapshot', error);
      toast.error('Failed to rollback calibration.');
    } finally {
      setSelectedMatterCalibrationActionRunning(false);
    }
  };

  const recordSelectedMatterPlannerOutcomeFeedback = async () => {
    if (!selectedMatterPlanner?.plan?.id) {
      toast.error('No planner plan found for this matter.');
      return;
    }

    setSelectedMatterOutcomeFeedbackSaving(true);
    try {
      const parseDecimalOrUndefined = (value: string) => {
        const parsed = parseFloat(value);
        return Number.isFinite(parsed) ? parsed : undefined;
      };

      await api.outcomeFeePlans.recordOutcomeFeedback(selectedMatterPlanner.plan.id, {
        actualOutcome: selectedMatterOutcomeFeedbackDraft.actualOutcome || undefined,
        actualFeesCollected: parseDecimalOrUndefined(selectedMatterOutcomeFeedbackDraft.actualFeesCollected),
        actualCost: parseDecimalOrUndefined(selectedMatterOutcomeFeedbackDraft.actualCost),
        actualMargin: parseDecimalOrUndefined(selectedMatterOutcomeFeedbackDraft.actualMargin),
        notes: selectedMatterOutcomeFeedbackDraft.notes || undefined
      });
      setSelectedMatterOutcomeFeedbackDraft(defaultOutcomePlannerFeedbackDraft());
      toast.success('Outcome feedback recorded for calibration.');
    } catch (error) {
      console.error('Failed to record outcome feedback', error);
      toast.error('Failed to record outcome feedback.');
    } finally {
      setSelectedMatterOutcomeFeedbackSaving(false);
    }
  };

  const recomputeSelectedMatterPlanner = async () => {
    if (!selectedMatterPlanner?.plan?.id || !selectedMatter?.id) return;

    setSelectedMatterPlannerRecomputing(true);
    try {
      const result = await api.outcomeFeePlans.recompute(selectedMatterPlanner.plan.id, {
        triggerType: 'manual_recompute_from_matter_detail',
        reason: 'User-triggered recompute from Matters detail'
      });

      if (result) {
        setSelectedMatterPlanner(result);
        if (result.plan?.id && (result.versions?.length || 0) > 1) {
          const compare = await api.outcomeFeePlans.compare(result.plan.id);
          setSelectedMatterPlannerCompare(compare || null);
        } else {
          setSelectedMatterPlannerCompare(null);
        }
        await refreshOutcomePlannerPortfolioMetrics();
        toast.success(`Planner recomputed (v${result.currentVersion?.versionNumber ?? '?'})`);
      } else {
        await refreshSelectedMatterPlanner(selectedMatter.id, true);
      }
    } catch (error) {
      console.error('Outcome-to-Fee planner recompute failed', error);
      toast.error('Planner recompute failed.');
    } finally {
      setSelectedMatterPlannerRecomputing(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const recentInlineClient = latestCreatedClientRef.current?.id === formData.partyId
      ? latestCreatedClientRef.current
      : null;
    const selectedClient = formData.partyType === 'client'
      ? clients.find((c) => c.id === formData.partyId) ?? recentInlineClient ?? undefined
      : undefined;
    const selectedLead = formData.partyType === 'lead'
      ? leads.find((l) => l.id === formData.partyId)
      : undefined;

    if (!selectedClient && !selectedLead) {
      toast.error('Select a client or lead before creating a matter.');
      return;
    }

    let resolvedClient: Client | null = selectedClient ?? null;

    if (!resolvedClient && selectedLead) {
      if (!selectedLead.email) {
        toast.error('Lead email is required to create a matter. Please add an email or select a client.');
        return;
      }
      try {
        resolvedClient = await addClient({
          name: selectedLead.name,
          email: selectedLead.email,
          phone: selectedLead.phone || '',
          type: 'Individual',
          status: 'Active'
        });
      } catch (error) {
        console.error('Failed to create client from lead', error);
        toast.error('Failed to create client from lead.');
        return;
      }
    }

    if (!resolvedClient) {
      toast.error('Unable to resolve client for this matter.');
      return;
    }

    const resolvedCaseNumber = formData.caseNumber || `24-${Math.floor(Math.random() * 10000)}`;
    const newMatter: Matter = {
      id: `m${Date.now()}`,
      name: formData.name,
      caseNumber: resolvedCaseNumber,
      practiceArea: formData.practiceArea,
      feeStructure: formData.feeStructure,
      status: CaseStatus.Open,
      openDate: new Date().toISOString(),
      responsibleAttorney: 'Partner',
      billableRate: 400,
      trustBalance: parseFloat(String(formData.trustAmount)) || 0,
      client: resolvedClient,
      courtType: formData.courtType,
      bailStatus: formData.bailStatus as any,
      bailAmount: formData.bailAmount ? parseFloat(String(formData.bailAmount)) : 0,
      outcome: formData.outcome,
      shareWithFirm: formData.shareWithFirm,
      shareBillingWithFirm: formData.shareWithFirm && formData.shareBillingWithFirm,
      shareNotesWithFirm: formData.shareWithFirm && formData.shareBillingWithFirm && formData.shareNotesWithFirm,
      entityId: formData.entityId || undefined,
      officeId: formData.officeId || undefined,
      relatedClientIds: matterSecondaryClientsEnabled ? formData.relatedClientIds : []
    };
    const plannerDraftSnapshot: OutcomePlannerDraft = { ...outcomePlannerDraft };
    const shouldAutoSavePlanner = plannerDraftSnapshot.enabled && plannerDraftSnapshot.autoSave;
    setMatterSubmitting(true);
    try {
      const createdMatter = await addMatter({
        ...newMatter,
        clientId: resolvedClient.id,
        clientName: resolvedClient.name,
        client: resolvedClient,
        sourceLeadId: selectedLead?.id,
        entityId: formData.entityId || undefined,
        officeId: formData.officeId || undefined,
        relatedClientIds: matterSecondaryClientsEnabled ? formData.relatedClientIds : []
      });

      if (shouldAutoSavePlanner) {
        if (createdMatter?.id) {
          void (async () => {
            try {
              await persistOutcomePlannerForMatter(createdMatter, plannerDraftSnapshot);
            } catch (plannerError) {
              console.error('Outcome-to-Fee planner save failed', plannerError);
              toast.warning('Matter created, but Outcome-to-Fee Planner could not be saved.');
            }
          })();
        } else {
          toast.warning('Matter was created, but planner version could not be auto-saved (matter lookup failed).');
        }
      }

      latestCreatedClientRef.current = null;
      setShowModal(false);
      resetMatterDraft();
      resetOutcomePlannerState();
    } catch (error) {
      console.error('Failed to create matter', error);
      toast.error('Failed to create matter.');
    } finally {
      setMatterSubmitting(false);
    }
  };

  const handleInlineClientCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (inlineClientSubmitting) return;

    setInlineClientSubmitting(true);
    try {
      const trimmedPassword = newClientData.password.trim();
      if (trimmedPassword) {
        const passwordResult = validatePassword(trimmedPassword, {
          email: newClientData.email,
          name: newClientData.name
        });
        if (!passwordResult.isValid) {
          toast.error(passwordResult.message);
          return;
        }
      }

      const payload = trimmedPassword
        ? { ...newClientData, password: trimmedPassword }
        : (() => {
            const { password, ...rest } = newClientData;
            return rest;
          })();

      const newClient = await addClient(payload);
      latestCreatedClientRef.current = newClient;
      setSelectedPartyName(newClient.name);
      setFormData(prev => ({
        ...prev,
        partyId: newClient.id,
        partyType: 'client',
        relatedClientIds: prev.relatedClientIds.filter((id) => id !== newClient.id)
      }));
      closeNewClientModal();
      toast.success('Client created and selected.');
    } catch (error: any) {
      const message = error?.message || 'Error creating client.';
      toast.error(message);
    } finally {
      setInlineClientSubmitting(false);
    }
  };

  // Generate a mock timeline based on matter creation + related data
  const getTimeline = (matterId: string) => {
    // FIX: Get the correct matter by ID instead of relying on selectedMatter state
    const matter = matters.find(m => m.id === matterId);
    const items = [
      // Basic Open Event - uses the correct matter's openDate
      { type: 'opened', date: matter?.openDate || new Date().toISOString(), title: 'Case Opened', detail: 'Initial file creation' },
      // Linked Docs ? FIX: Only show documents explicitly linked to this matter (removed !d.matterId to prevent privacy leak)
      ...documents.filter(d => d.matterId === matterId).slice(0, 2).map(d => ({ type: 'doc', date: d.updatedAt, title: 'Document Added', detail: d.name })),
      // Linked Time
      ...timeEntries.filter(te => te.matterId === matterId).map(te => ({ type: 'time', date: te.date, title: 'Billable Activity', detail: te.description })),
    ];
    return items.sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime());
  };

  const closeDocViewer = () => {
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
    }
    setDocObjectUrl(null);
    setViewingDoc(null);
    setDocContent('');
  };

  const handleOpenDoc = async (doc: DocumentFile) => {
    setViewingDoc(doc);
    setLoadingContent(true);
    setDocContent('');
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
      setDocObjectUrl(null);
    }

    try {
      if (doc.filePath) {
        const response = await api.downloadDocument(doc.id);
        if (!response) {
          throw new Error('Document download failed');
        }

        if (doc.type === 'pdf') {
          const url = window.URL.createObjectURL(response.blob);
          setDocObjectUrl(url);
          setDocContent(url);
        } else if (doc.type === 'txt') {
          const text = await response.blob.text();
          setDocContent(text);
        } else if (doc.type === 'docx') {
          const arrayBuffer = await response.blob.arrayBuffer();
          const result = await mammoth.convertToHtml({ arrayBuffer });
          setDocContent(result.value);
        } else {
          const url = window.URL.createObjectURL(response.blob);
          setDocObjectUrl(url);
          setDocContent(url);
        }
        return;
      }

      if (!doc.content) {
        toast.warning('No content is available for this file. Please upload it again.');
        closeDocViewer();
        return;
      }

      if (doc.type === 'txt') {
        const base64 = (doc.content as string).split(',')[1];
        const text = atob(base64);
        setDocContent(text);
      } else if (doc.type === 'docx') {
        const base64 = (doc.content as string).split(',')[1];
        const binaryString = atob(base64);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
          bytes[i] = binaryString.charCodeAt(i);
        }
        const arrayBuffer = bytes.buffer;
        const result = await mammoth.convertToHtml({ arrayBuffer });
        setDocContent(result.value);
      } else {
        setDocContent(doc.content as string);
      }
    } catch (error) {
      console.error('Error opening document:', error);
      toast.error('Unable to open the file.');
      closeDocViewer();
    } finally {
      setLoadingContent(false);
    }
  };

  const handleDownloadDoc = async (doc: DocumentFile) => {
    try {
      if (doc.filePath) {
        const response = await api.downloadDocument(doc.id);
        if (!response) {
          throw new Error('File download failed');
        }
        const blob = response.blob;
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = response.filename || doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
        return;
      }

      if (doc.content) {
        const link = document.createElement('a');
        link.href = doc.content as string;
        link.download = doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        return;
      }

      toast.warning('No content is available for this file.');
    } catch (error: any) {
      console.error('Download error:', error);
      toast.error('Failed to download file: ' + (error.message || 'Unknown error'));
    }
  };

  const filteredMatters = matters.filter((m): m is Matter => !!m && typeof m.id === 'string').filter(m => {
    if (String(m.status).toLowerCase() === 'deleted') return false;
    const q = search.toLowerCase();
    const resolvedClient = resolveMatterClient(m);
    const matchesQuery = [m.name, resolvedClient?.name, m.caseNumber].some(v => v?.toLowerCase().includes(q));
    const matchesStatus = statusFilter === 'all' ? true : m.status === statusFilter;
    const matchesEntity = !entityFilter || m.entityId === entityFilter;
    const matchesOffice = !officeFilter || m.officeId === officeFilter;
    return matchesQuery && matchesStatus && matchesEntity && matchesOffice;
  });

  const selectedMatterPlannerBaseScenario = useMemo(() => {
    if (!selectedMatterPlanner?.scenarios?.length) return null;
    return selectedMatterPlanner.scenarios.find(s => s.scenarioKey === 'base') || selectedMatterPlanner.scenarios[0];
  }, [selectedMatterPlanner]);

  const selectedMatterPlannerCompareBaseDelta = useMemo(() => {
    const deltas = (selectedMatterPlannerCompare?.scenarioDeltas || []) as Array<Record<string, any>>;
    return deltas.find(d => String(d?.scenarioKey || '').toLowerCase() === 'base') || deltas[0] || null;
  }, [selectedMatterPlannerCompare]);

  const selectedMatterPlannerPhaseDeltas = useMemo(() => {
    const rows = ((selectedMatterPlannerCompare as any)?.phaseDeltas || []) as Array<Record<string, any>>;
    return [...rows].sort((a, b) => Number(a?.phaseOrder || 0) - Number(b?.phaseOrder || 0));
  }, [selectedMatterPlannerCompare]);

  const selectedMatterPlannerPhaseDeltaMaxFeeRatio = useMemo(() => {
    if (!selectedMatterPlannerPhaseDeltas.length) return 0;
    return selectedMatterPlannerPhaseDeltas.reduce((max, row) => {
      const ratio = Math.abs(Number((row?.delta as any)?.feeDeltaRatio || 0));
      return Math.max(max, ratio);
    }, 0);
  }, [selectedMatterPlannerPhaseDeltas]);

  const selectedMatterPlannerDriftSummary = (selectedMatterPlannerCompare?.driftSummary || null) as Record<string, any> | null;
  const selectedMatterPlannerActuals = (selectedMatterPlannerCompare?.actuals || null) as Record<string, any> | null;
  const selectedMatterPlannerBaseScenarioMetadata = useMemo(() => {
    const json = selectedMatterPlannerBaseScenario?.metadataJson;
    if (!json) return null;
    try {
      return JSON.parse(json) as Record<string, any>;
    } catch {
      return null;
    }
  }, [selectedMatterPlannerBaseScenario?.metadataJson]);
  const selectedMatterPlannerCollectionsIntel = (selectedMatterPlannerBaseScenarioMetadata?.collectionsIntelligence || null) as Record<string, any> | null;
  const selectedMatterPlannerStaffingIntel = (selectedMatterPlannerBaseScenarioMetadata?.staffingIntelligence || null) as Record<string, any> | null;
  const selectedMatterPlannerMarginIntel = (selectedMatterPlannerBaseScenarioMetadata?.marginIntelligence || null) as Record<string, any> | null;
  const selectedMatterPlannerStressTests = (selectedMatterPlannerBaseScenarioMetadata?.stressTests || []) as Array<Record<string, any>>;
  const outcomePlannerPortfolioMetricsData = (outcomePlannerPortfolioMetrics?.metrics || null) as Record<string, any> | null;
  const selectedMatterCalibrationActive = (selectedMatterCalibration?.active || null) as Record<string, any> | null;
  const selectedMatterCalibrationShadow = (selectedMatterCalibration?.shadow || null) as Record<string, any> | null;
  const selectedMatterCalibrationActiveSnapshot = (selectedMatterCalibrationActive?.snapshot || null) as Record<string, any> | null;
  const selectedMatterCalibrationShadowSnapshot = (selectedMatterCalibrationShadow?.snapshot || null) as Record<string, any> | null;
  const selectedMatterCalibrationActiveMetrics = (selectedMatterCalibrationActive?.metrics || null) as Record<string, any> | null;
  const selectedMatterCalibrationShadowMetrics = (selectedMatterCalibrationShadow?.metrics || null) as Record<string, any> | null;
  const selectedMatterCalibrationActivePayload = (selectedMatterCalibrationActive?.payload || null) as Record<string, any> | null;
  const selectedMatterCalibrationShadowPayload = (selectedMatterCalibrationShadow?.payload || null) as Record<string, any> | null;

  return (
    <div className="p-8 h-full flex flex-col bg-gray-50/50 relative">
      <div className="flex justify-between items-center mb-8">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">{t('matters_title')}</h1>
          <p className="text-sm text-gray-500 mt-1">{t('matters_subtitle')}</p>
        </div>
        <Can perform="matter.create">
          <button
            onClick={() => { resetOutcomePlannerState(); setEditData(null); resetMatterDraft(); setShowModal(true); }}
            className="bg-slate-800 text-white px-5 py-2.5 rounded-lg shadow-lg hover:bg-slate-700 transition-colors text-sm font-medium flex items-center gap-2"
          >
            <Plus className="w-4 h-4" />
            <span>{t('new_matter')}</span>
          </button>
        </Can>
      </div>

      {/* Filters & Search Toolbar */}
      <div className="bg-white p-2 rounded-xl border border-gray-200 shadow-sm mb-6 flex flex-wrap gap-3 items-center">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-3 h-5 w-5 text-gray-400" />
          <input
            type="text"
            placeholder={t('search_placeholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-10 pr-4 py-2.5 border-none rounded-lg text-sm focus:outline-none focus:ring-0 text-gray-700 font-medium bg-transparent"
          />
        </div>
        <div className="h-8 w-px bg-gray-200 mx-2"></div>
        <div className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 rounded-lg transition-colors border border-gray-200 bg-white">
          <Filter className="w-4 h-4" />
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as any)}
            className="bg-transparent outline-none text-sm text-gray-700"
          >
            <option value="all">All</option>
            {Object.values(CaseStatus).map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </div>
        <EntityOfficeFilter
          entityId={entityFilter}
          officeId={officeFilter}
          onEntityChange={setEntityFilter}
          onOfficeChange={setOfficeFilter}
          allowAll
        />
      </div>

      {/* Data Table */}
      <div className="bg-white rounded-xl border border-gray-200 shadow-card flex-1 overflow-hidden flex flex-col">
        {matters.length === 0 ? (
          <div className="flex-1 flex flex-col items-center justify-center text-gray-400 p-8">
            <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mb-4">
              <Plus className="w-8 h-8 text-gray-300" />
            </div>
            <p className="text-lg font-medium text-gray-500">No matters found.</p>
            <p className="text-sm">Create a new matter to get started.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-gray-50 border-b border-gray-100">
                  <th className="pl-6 py-4 text-xs font-bold text-gray-400 uppercase tracking-wider w-10"></th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">Matter Details</th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">Client</th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">Fee Structure</th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('col_status')}</th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('col_court')}</th>
                  <th className="px-4 py-4 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('col_trust_funds')}</th>
                  <th className="pr-6 py-4"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {filteredMatters.map((matter) => {
                  const matterClient = resolveMatterClient(matter);
                  return (
                    <tr
                      key={matter.id}
                      onClick={() => setSelectedMatter(matter)}
                      className="hover:bg-gray-50/80 transition-all cursor-pointer group"
                    >
                      <td className="pl-6 py-4">
                        <div className={`w-1.5 h-10 rounded-full ${matter.status === CaseStatus.Open ? 'bg-emerald-500' :
                          matter.status === CaseStatus.Trial ? 'bg-red-500' :
                            matter.status === CaseStatus.Pending ? 'bg-amber-400' : 'bg-gray-300'
                          }`}></div>
                      </td>
                      <td className="px-4 py-4">
                        <div className="flex flex-col">
                          <span className="font-bold text-slate-800 text-sm">{matter.name}</span>
                          <span className="text-xs font-medium text-gray-400 mt-0.5">{matter.caseNumber}</span>
                        </div>
                      </td>
                      <td className="px-4 py-4">
                        <div className="flex items-center gap-3">
                          <div className="w-8 h-8 rounded-full bg-primary-50 text-primary-600 flex items-center justify-center text-xs font-bold uppercase">
                            {(matterClient?.name || '?').substring(0, 2)}
                          </div>
                          <div className="flex flex-col">
                            <span className="text-sm font-semibold text-slate-700">{matterClient?.name || 'Unknown Client'}</span>
                            {matterClient?.company && <span className="text-xs text-gray-400">{matterClient.company}</span>}
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-600 font-medium">
                        <span className="bg-gray-100 text-gray-600 px-2.5 py-1 rounded text-xs">
                          {matter.feeStructure || 'Hourly'}
                        </span>
                      </td>
                      <td className="px-4 py-4 whitespace-nowrap">
                        <span className={`px-2.5 py-1 inline-flex text-xs leading-none font-bold rounded-md 
                      ${matter.status === CaseStatus.Open ? 'bg-emerald-100 text-emerald-700' :
                            matter.status === CaseStatus.Trial ? 'bg-red-100 text-red-700' :
                              matter.status === CaseStatus.Pending ? 'bg-amber-100 text-amber-700' :
                                'bg-gray-100 text-gray-600'}`}>
                          {matter.status === CaseStatus.Open ? t('status_open') :
                            matter.status === CaseStatus.Trial ? t('status_trial') :
                              matter.status === CaseStatus.Pending ? t('status_pending') : t('status_closed')}
                        </span>
                      </td>
                      <td className="px-4 py-4 whitespace-nowrap text-sm text-gray-600">
                        {matter.courtType || '-'}
                      </td>
                      <td className="px-4 py-4 whitespace-nowrap text-sm font-mono font-medium text-gray-600">
                        {formatCurrency(matter.trustBalance)}
                      </td>
                      <td className="pr-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                        <ChevronRight className="w-5 h-5 text-gray-300 group-hover:text-slate-800 transition-colors" />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Detail Slide-over */}
      {selectedMatter && (
        <div className="absolute inset-0 z-20 flex justify-end">
          <div className="absolute inset-0 bg-slate-900/20 backdrop-blur-[1px]" onClick={() => setSelectedMatter(null)}></div>
          <div className="relative w-full max-w-lg bg-white h-full min-h-0 overflow-y-auto shadow-2xl animate-in slide-in-from-right duration-300 flex flex-col">
            {/* Header */}
            <div className="px-6 py-5 border-b border-gray-100 flex justify-between items-start bg-gray-50/50">
              <div>
                <div className="flex items-center gap-2 mb-1">
                  <span className="text-xs font-mono font-bold text-gray-400 bg-white px-1.5 py-0.5 rounded border border-gray-200">{selectedMatter.caseNumber}</span>
                  <span className="text-xs font-bold text-emerald-600 bg-emerald-50 px-2 py-0.5 rounded-full uppercase">{selectedMatter.status}</span>
                </div>
                <h2 className="text-xl font-bold text-slate-900">{selectedMatter.name}</h2>
                <p className="text-sm text-gray-500 mt-0.5">{resolveMatterClient(selectedMatter)?.name || 'Unknown Client'} - {selectedMatter.practiceArea} {selectedMatter.courtType && `- ${selectedMatter.courtType}`}</p>
              </div>
              <button onClick={() => setSelectedMatter(null)} className="p-1 hover:bg-gray-200 rounded-full text-gray-400"><X className="w-6 h-6" /></button>
            </div>

            {/* Financial Quick View */}
            <div className="p-6 grid grid-cols-2 gap-4 border-b border-gray-100">
              <div className="bg-blue-50 p-3 rounded-lg border border-blue-100">
                <p className="text-xs font-bold text-blue-600 uppercase mb-1">Trust Balance</p>
                <p className="text-xl font-mono font-bold text-slate-800">{formatCurrency(selectedMatter.trustBalance)}</p>
              </div>
              <div className="bg-gray-50 p-3 rounded-lg border border-gray-100">
                <p className="text-xs font-bold text-gray-500 uppercase mb-1">Fee Structure</p>
                <p className="text-xl font-bold text-slate-800">{selectedMatter.feeStructure}</p>
              </div>
            </div>

            <div className="px-6 py-5 border-b border-gray-100 bg-white">
              <div className="mb-3">
                <h3 className="text-sm font-bold text-slate-800 uppercase tracking-wide">Clients</h3>
                <p className="text-xs text-gray-500 mt-1">
                  The primary client remains the main billing/contact record. Additional clients can view matter-linked calendar items in the client portal.
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                {resolveMatterClient(selectedMatter) && (
                  <span className="inline-flex items-center gap-2 rounded-full border border-blue-200 bg-blue-50 px-3 py-1 text-xs font-semibold text-blue-700">
                    Primary: {resolveMatterClient(selectedMatter)?.name}
                  </span>
                )}
                {(selectedMatter.relatedClients || []).map((client) => (
                  <span key={client.id} className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-semibold text-slate-700">
                    Additional: {client.name}
                  </span>
                ))}
                {(!selectedMatter.relatedClients || selectedMatter.relatedClients.length === 0) && (
                  <span className="text-xs text-gray-500">No additional clients linked to this matter.</span>
                )}
              </div>
            </div>

            {/* Client Transparency Reviewer (Phase 3) */}
            <div className="px-6 py-5 border-b border-gray-100 bg-emerald-50/30">
              <MatterAnalysisSection
                title="Client Transparency Review"
                subtitle="Draft vs published snapshot, client-safe wording review, and publish policy"
                className="rounded-xl border border-emerald-100 bg-emerald-50/40 p-4"
                titleClassName="text-sm font-bold text-slate-800 uppercase tracking-wide"
                actions={(
                  <button
                    type="button"
                    onClick={() => selectedMatter?.id && refreshSelectedMatterTransparencyWorkspace(selectedMatter.id, true)}
                    disabled={!selectedMatter?.id || selectedMatterTransparencyLoading}
                    className="px-3 py-1.5 text-xs font-bold rounded-lg border border-emerald-200 bg-white text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                  >
                    {selectedMatterTransparencyLoading ? 'Refreshing...' : 'Refresh'}
                  </button>
                )}
              >
                {selectedMatterTransparencyLoading && !selectedMatterTransparencyWorkspace ? (
                  <div className="text-xs text-gray-500">Loading transparency review workspace...</div>
                ) : !selectedMatterTransparencyWorkspace ? (
                  <div className="text-xs text-gray-500">No transparency review workspace available yet.</div>
                ) : (
                  <div className="space-y-3">
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    <MatterAnalysisSection
                      title="Draft Snapshot"
                      className="bg-white border border-emerald-100 rounded-lg p-3"
                      titleClassName="text-[11px] font-bold uppercase text-emerald-700"
                    >
                      {selectedMatterTransparencyWorkspace?.draft?.snapshot ? (
                        <div className="space-y-1 text-[11px] text-slate-700">
                          <p className="font-semibold">
                            v{selectedMatterTransparencyWorkspace.draft.snapshot.versionNumber} • {String(selectedMatterTransparencyWorkspace.draft.snapshot.status || 'generated')}
                          </p>
                          <p>Confidence: {Math.round(Number(selectedMatterTransparencyWorkspace.draft.snapshot.confidenceScore || 0) * 100)}%</p>
                          <p>Data quality: {String(selectedMatterTransparencyWorkspace.draft.snapshot.dataQuality || 'unknown')}</p>
                          <p className="text-xs text-slate-800">{String(selectedMatterTransparencyWorkspace.draft.snapshot.snapshotSummary || '')}</p>
                        </div>
                      ) : (
                        <p className="text-[11px] text-gray-500">No draft snapshot.</p>
                      )}
                    </MatterAnalysisSection>
                    <MatterAnalysisSection
                      title="Published Snapshot"
                      className="bg-white border border-slate-200 rounded-lg p-3"
                    >
                      {selectedMatterTransparencyWorkspace?.published?.snapshot ? (
                        <div className="space-y-1 text-[11px] text-slate-700">
                          <p className="font-semibold">
                            v{selectedMatterTransparencyWorkspace.published.snapshot.versionNumber} • published
                          </p>
                          <p>
                            {selectedMatterTransparencyWorkspace.published.snapshot.publishedAt
                              ? new Date(selectedMatterTransparencyWorkspace.published.snapshot.publishedAt).toLocaleString()
                              : 'Published date unavailable'}
                          </p>
                          <p className="text-xs text-slate-800">{String(selectedMatterTransparencyWorkspace.published.snapshot.snapshotSummary || '')}</p>
                        </div>
                      ) : (
                        <p className="text-[11px] text-gray-500">No published snapshot yet.</p>
                      )}
                    </MatterAnalysisSection>
                  </div>

                  <MatterAnalysisSection
                    title="Publish Policy"
                    className="bg-white border border-slate-200 rounded-lg p-3"
                    actions={(
                      <button
                        type="button"
                        onClick={saveSelectedMatterTransparencyPolicy}
                        disabled={selectedMatterTransparencyPolicySaving}
                        className="px-2 py-1 text-[11px] font-bold rounded border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                      >
                        {selectedMatterTransparencyPolicySaving ? 'Saving...' : 'Save Policy'}
                      </button>
                    )}
                  >
                    <div className="grid grid-cols-2 gap-2">
                      <select
                        value={selectedMatterTransparencyPolicyDraft.publishPolicy}
                        onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, publishPolicy: e.target.value }))}
                        className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      >
                        <option value="warn_only">warn_only</option>
                        <option value="auto_publish_safe">auto_publish_safe</option>
                        <option value="review_required_for_delay_reason">review_required_for_delay_reason</option>
                        <option value="review_required_for_cost_impact_change_gt_x">review_required_for_cost_impact_change_gt_x</option>
                        <option value="block_on_low_confidence">block_on_low_confidence</option>
                      </select>
                      <input
                        type="number"
                        step="0.01"
                        value={selectedMatterTransparencyPolicyDraft.costImpactChangeThreshold}
                        onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, costImpactChangeThreshold: e.target.value }))}
                        className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                        placeholder="Cost impact threshold"
                      />
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        max="1"
                        value={selectedMatterTransparencyPolicyDraft.lowConfidenceThreshold}
                        onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, lowConfidenceThreshold: e.target.value }))}
                        className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                        placeholder="Low confidence threshold"
                      />
                      <div className="grid grid-cols-2 gap-1 text-[11px] text-slate-700">
                        <label className="flex items-center gap-1"><input type="checkbox" checked={selectedMatterTransparencyPolicyDraft.autoPublishSafe} onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, autoPublishSafe: e.target.checked }))} />Auto-safe</label>
                        <label className="flex items-center gap-1"><input type="checkbox" checked={selectedMatterTransparencyPolicyDraft.blockOnLowConfidence} onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, blockOnLowConfidence: e.target.checked }))} />Block low conf</label>
                        <label className="flex items-center gap-1"><input type="checkbox" checked={selectedMatterTransparencyPolicyDraft.reviewRequiredForDelayReason} onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, reviewRequiredForDelayReason: e.target.checked }))} />Delay review</label>
                        <label className="flex items-center gap-1"><input type="checkbox" checked={selectedMatterTransparencyPolicyDraft.reviewRequiredForCostImpactChange} onChange={(e) => setSelectedMatterTransparencyPolicyDraft(prev => ({ ...prev, reviewRequiredForCostImpactChange: e.target.checked }))} />Cost review</label>
                      </div>
                    </div>
                    {selectedMatterTransparencyWorkspace?.draftPolicyEvaluation && (
                      <div className="rounded border border-amber-100 bg-amber-50/40 p-2 text-[11px] text-slate-700">
                        <p className="font-semibold text-amber-700 mb-1">
                          Decision: {String(selectedMatterTransparencyWorkspace.draftPolicyEvaluation.publishDecision || 'unknown')}
                        </p>
                        <p>Requires Review: {String(!!selectedMatterTransparencyWorkspace.draftPolicyEvaluation.requiresReview)} • Blocked: {String(!!selectedMatterTransparencyWorkspace.draftPolicyEvaluation.blocked)}</p>
                        {Array.isArray(selectedMatterTransparencyWorkspace.draftPolicyEvaluation.reasons) && selectedMatterTransparencyWorkspace.draftPolicyEvaluation.reasons.length > 0 && (
                          <p className="mt-1">Reasons: {selectedMatterTransparencyWorkspace.draftPolicyEvaluation.reasons.map((r: string) => r.replaceAll('_', ' ')).join(', ')}</p>
                        )}
                      </div>
                    )}
                  </MatterAnalysisSection>

                  <MatterAnalysisSection
                    title="Draft vs Published Compare"
                    className="bg-white border border-slate-200 rounded-lg p-3"
                    actions={(
                      <span className="text-[10px] text-gray-400">
                        Review item: {selectedMatterTransparencyWorkspace?.pendingReviewItem?.status || 'none'}
                      </span>
                    )}
                  >
                    {selectedMatterTransparencyWorkspace?.draftVsPublished ? (
                      <div className="grid grid-cols-2 gap-x-3 gap-y-1 text-[11px] text-slate-700">
                        <p>Summary changed: {String(!!selectedMatterTransparencyWorkspace.draftVsPublished.summaryChanged)}</p>
                        <p>What changed updated: {String(!!selectedMatterTransparencyWorkspace.draftVsPublished.whatChangedUpdated)}</p>
                        <p>Next step changed: {String(!!selectedMatterTransparencyWorkspace.draftVsPublished.nextStepChanged)}</p>
                        <p>Cost impact changed: {String(!!selectedMatterTransparencyWorkspace.draftVsPublished.costImpactChanged)}</p>
                        <p className="col-span-2">
                          Delay added: {Array.isArray(selectedMatterTransparencyWorkspace.draftVsPublished.delayAdded) && selectedMatterTransparencyWorkspace.draftVsPublished.delayAdded.length > 0
                            ? selectedMatterTransparencyWorkspace.draftVsPublished.delayAdded.join(', ')
                            : 'none'}
                        </p>
                      </div>
                    ) : (
                      <div className="text-[11px] text-gray-500">No compare data.</div>
                    )}
                  </MatterAnalysisSection>

                  <MatterAnalysisSection
                    title="Evidence Quality (Phase 4)"
                    className="bg-white border border-slate-200 rounded-lg p-3"
                    actions={(
                      <button
                        type="button"
                        onClick={reverifySelectedMatterTransparencyEvidence}
                        disabled={selectedMatterTransparencyEvidenceActionLoading || !selectedMatter?.id}
                        className="px-2 py-1 text-[11px] font-bold rounded border border-indigo-200 bg-indigo-50 text-indigo-700 hover:bg-indigo-100 disabled:opacity-50"
                      >
                        {selectedMatterTransparencyEvidenceActionLoading ? 'Reverifying...' : 'Batch Re-verify'}
                      </button>
                    )}
                  >
                    {selectedMatterTransparencyEvidenceLoading ? (
                      <div className="text-[11px] text-gray-500">Loading evidence metrics...</div>
                    ) : (
                      <div className="space-y-2">
                        {selectedMatterTransparencyEvidenceMetrics ? (
                          <div className="grid grid-cols-2 gap-2 text-[11px] text-slate-700">
                            <div>Coverage: {Math.round(Number(selectedMatterTransparencyEvidenceMetrics.coverageRate || 0) * 100)}%</div>
                            <div>Stale rate: {Math.round(Number(selectedMatterTransparencyEvidenceMetrics.staleRate || 0) * 100)}%</div>
                            <div>Snapshots: {Number(selectedMatterTransparencyEvidenceMetrics.snapshotCount || 0)}</div>
                            <div>Pending reviews: {Number(selectedMatterTransparencyEvidenceMetrics.pendingReviewCount || 0)}</div>
                            <div>Review burden avg: {Number(selectedMatterTransparencyEvidenceMetrics.reviewBurdenAverage || 0).toFixed(1)}</div>
                            <div>Mean review hrs: {selectedMatterTransparencyEvidenceMetrics.meanReviewTurnaroundHours != null ? Number(selectedMatterTransparencyEvidenceMetrics.meanReviewTurnaroundHours).toFixed(1) : 'n/a'}</div>
                          </div>
                        ) : (
                          <div className="text-[11px] text-gray-500">No metrics available.</div>
                        )}
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                          <MatterAnalysisSection
                            title="Draft Evidence"
                            className="rounded border border-emerald-100 bg-emerald-50/30 p-2"
                            titleClassName="text-[10px] font-bold uppercase text-emerald-700"
                          >
                            {selectedMatterTransparencyDraftEvidence?.quality ? (
                              <div className="text-[11px] text-slate-700 space-y-1">
                                <p>Coverage: {Math.round(Number(selectedMatterTransparencyDraftEvidence.quality.coverage || 0) * 100)}%</p>
                                <p>Stale sources: {Number(selectedMatterTransparencyDraftEvidence.quality.staleSources || 0)}</p>
                                <p>Total sources: {Number(selectedMatterTransparencyDraftEvidence.quality.totalSources || 0)}</p>
                              </div>
                            ) : <div className="text-[11px] text-gray-500">No draft evidence.</div>}
                          </MatterAnalysisSection>
                          <MatterAnalysisSection
                            title="Published Evidence"
                            className="rounded border border-slate-200 bg-slate-50/50 p-2"
                            titleClassName="text-[10px] font-bold uppercase text-slate-600"
                          >
                            {selectedMatterTransparencyPublishedEvidence?.quality ? (
                              <div className="text-[11px] text-slate-700 space-y-1">
                                <p>Coverage: {Math.round(Number(selectedMatterTransparencyPublishedEvidence.quality.coverage || 0) * 100)}%</p>
                                <p>Stale sources: {Number(selectedMatterTransparencyPublishedEvidence.quality.staleSources || 0)}</p>
                                <p>Total sources: {Number(selectedMatterTransparencyPublishedEvidence.quality.totalSources || 0)}</p>
                              </div>
                            ) : <div className="text-[11px] text-gray-500">No published evidence.</div>}
                          </MatterAnalysisSection>
                        </div>
                      </div>
                    )}
                  </MatterAnalysisSection>

                  <MatterAnalysisSection
                    title="Client-Safe Wording Review"
                    className="bg-white border border-slate-200 rounded-lg p-3"
                  >
                    <input
                      type="text"
                      value={selectedMatterTransparencyRewriteDraft.assignedTo}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, assignedTo: e.target.value }))}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="Assign review item to user id (optional)"
                    />
                    <textarea
                      value={selectedMatterTransparencyRewriteDraft.snapshotSummary}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, snapshotSummary: e.target.value }))}
                      rows={2}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="Client-facing summary"
                    />
                    <textarea
                      value={selectedMatterTransparencyRewriteDraft.whatChangedSummary}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, whatChangedSummary: e.target.value }))}
                      rows={2}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="What changed summary"
                    />
                    <textarea
                      value={selectedMatterTransparencyRewriteDraft.nextStepActionText}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, nextStepActionText: e.target.value }))}
                      rows={2}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="Next step action text"
                    />
                    <input
                      type="text"
                      value={selectedMatterTransparencyRewriteDraft.nextStepBlockedByText}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, nextStepBlockedByText: e.target.value }))}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="Next step blocked-by text (optional)"
                    />
                    {Array.isArray(selectedMatterTransparencyWorkspace?.draft?.delayReasons) && selectedMatterTransparencyWorkspace.draft.delayReasons.length > 0 && (
                      <div className="space-y-2">
                        <p className="text-[11px] font-semibold text-slate-600">Delay reasons (client-safe text)</p>
                        {selectedMatterTransparencyWorkspace.draft.delayReasons.map((delay: any) => (
                          <div key={String(delay.id)} className="rounded border border-amber-100 bg-amber-50/30 p-2">
                            <p className="text-[10px] font-bold uppercase text-amber-700 mb-1">
                              {String(delay.reasonCode || delay.code || 'delay').replaceAll('_', ' ')}
                            </p>
                            <textarea
                              value={selectedMatterTransparencyDelayReasonEdits[String(delay.id)] ?? ''}
                              onChange={(e) => setSelectedMatterTransparencyDelayReasonEdits(prev => ({ ...prev, [String(delay.id)]: e.target.value }))}
                              rows={2}
                              className="w-full px-2 py-1.5 text-xs border border-amber-100 rounded-md bg-white"
                            />
                          </div>
                        ))}
                      </div>
                    )}
                    <textarea
                      value={selectedMatterTransparencyRewriteDraft.reason}
                      onChange={(e) => setSelectedMatterTransparencyRewriteDraft(prev => ({ ...prev, reason: e.target.value }))}
                      rows={2}
                      className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                      placeholder="Reviewer reason / publish note"
                    />
                    <div className="flex flex-wrap gap-2 justify-end">
                      <button
                        type="button"
                        onClick={rewriteSelectedMatterTransparencyDraft}
                        disabled={selectedMatterTransparencyActionSaving || !selectedMatterTransparencyWorkspace?.draft?.snapshot?.id}
                        className="px-3 py-1.5 text-xs font-bold rounded border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                      >
                        {selectedMatterTransparencyActionSaving ? 'Saving...' : 'Save Rewrite'}
                      </button>
                      <button
                        type="button"
                        onClick={rejectSelectedMatterTransparency}
                        disabled={selectedMatterTransparencyActionSaving || !selectedMatterTransparencyWorkspace?.draft?.snapshot?.id}
                        className="px-3 py-1.5 text-xs font-bold rounded border border-red-200 bg-red-50 text-red-700 hover:bg-red-100 disabled:opacity-50"
                      >
                        Reject
                      </button>
                      <button
                        type="button"
                        onClick={approveAndPublishSelectedMatterTransparency}
                        disabled={selectedMatterTransparencyActionSaving || !selectedMatterTransparencyWorkspace?.draft?.snapshot?.id}
                        className="px-3 py-1.5 text-xs font-bold rounded border border-emerald-200 bg-emerald-50 text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
                      >
                        Approve & Publish
                      </button>
                      <button
                        type="button"
                        onClick={overridePublishSelectedMatterTransparency}
                        disabled={selectedMatterTransparencyActionSaving || !selectedMatterTransparencyWorkspace?.draft?.snapshot?.id}
                        className="px-3 py-1.5 text-xs font-bold rounded border border-amber-200 bg-amber-50 text-amber-700 hover:bg-amber-100 disabled:opacity-50"
                      >
                        Override Publish
                      </button>
                    </div>
                  </MatterAnalysisSection>
                  </div>
                )}
              </MatterAnalysisSection>
            </div>

            {/* Outcome-to-Fee Planner (Phase 3 Drift View) */}
            <div className="px-6 py-5 border-b border-gray-100 bg-slate-50/60">
              <MatterAnalysisSection
                title="Outcome-to-Fee Planner"
                subtitle="Dynamic forecast drift and version deltas"
                className="rounded-xl border border-slate-200 bg-slate-50/70 p-4"
                titleClassName="text-sm font-bold text-slate-800 uppercase tracking-wide"
                actions={(
                  <button
                    type="button"
                    disabled={!selectedMatterPlanner?.plan?.id || selectedMatterPlannerRecomputing}
                    onClick={recomputeSelectedMatterPlanner}
                    className="px-3 py-1.5 text-xs font-bold rounded-lg border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {selectedMatterPlannerRecomputing ? 'Recomputing...' : 'Recompute'}
                  </button>
                )}
              >
                {selectedMatterPlannerLoading ? (
                  <div className="text-xs text-gray-500">Loading planner...</div>
                ) : !selectedMatterPlanner?.plan ? (
                  <div className="text-xs text-gray-500">No planner version saved for this matter yet.</div>
                ) : (
                  <div className="space-y-3">
                  <div className="grid grid-cols-2 gap-3">
                    <div className="bg-white border border-slate-200 rounded-lg p-3">
                      <p className="text-[11px] font-bold uppercase text-slate-500 mb-1">Current Version</p>
                      <div className="flex items-center gap-2">
                        <span className="text-lg font-bold text-slate-800">v{selectedMatterPlanner.currentVersion?.versionNumber ?? '?'}</span>
                        <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 uppercase">
                          {selectedMatterPlanner.currentVersion?.plannerMode || selectedMatterPlanner.plan.plannerMode}
                        </span>
                      </div>
                      <p className="text-[11px] text-gray-500 mt-1">
                        {selectedMatterPlanner.versions?.length || 0} version(s)
                      </p>
                    </div>
                    <div className="bg-white border border-slate-200 rounded-lg p-3">
                      <p className="text-[11px] font-bold uppercase text-slate-500 mb-1">Base Scenario</p>
                      <p className="text-sm font-semibold text-slate-800">
                        {selectedMatterPlannerBaseScenario?.name || 'Base'}
                      </p>
                      <p className="text-[11px] text-gray-500 mt-1">
                        Budget {formatCurrency(Number(selectedMatterPlannerBaseScenario?.budgetTotal || 0))} •
                        Collected {formatCurrency(Number(selectedMatterPlannerBaseScenario?.expectedCollected || 0))}
                      </p>
                    </div>
                  </div>

                  <div className="bg-white border border-slate-200 rounded-lg p-3">
                    <div className="flex items-center justify-between gap-2 mb-2">
                      <p className="text-[11px] font-bold uppercase text-slate-500">Planner KPI Dashboard (90d)</p>
                      <span className="text-[10px] text-gray-400">
                        {outcomePlannerPortfolioMetricsLoading ? 'Loading...' : (outcomePlannerPortfolioMetrics?.dataQuality || 'n/a')}
                      </span>
                    </div>
                    {outcomePlannerPortfolioMetricsData ? (
                      <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Forecast Accuracy</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(outcomePlannerPortfolioMetricsData.forecastAccuracy || 0) * 100)}%
                          </p>
                        </div>
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Collections Err</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(outcomePlannerPortfolioMetricsData.collectionsForecastError || 0) * 100)}%
                          </p>
                        </div>
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Margin Err</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(outcomePlannerPortfolioMetricsData.marginForecastError || 0) * 100)}%
                          </p>
                        </div>
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Staffing Var</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(outcomePlannerPortfolioMetricsData.staffingVariance || 0) * 100)}%
                          </p>
                        </div>
                      </div>
                    ) : (
                      <div className="text-xs text-gray-500">No portfolio planner metrics available yet.</div>
                    )}
                  </div>

                  {selectedMatterPlannerCompare && (
                    <div className="bg-white border border-slate-200 rounded-lg p-3 space-y-3">
                      <div className="flex items-center justify-between gap-2">
                        <p className="text-[11px] font-bold uppercase text-slate-500">Version Compare</p>
                        <span className={`text-[10px] px-2 py-0.5 rounded-full font-bold uppercase ${
                          String(selectedMatterPlannerDriftSummary?.severity || 'low').toLowerCase() === 'high'
                            ? 'bg-red-100 text-red-700'
                            : String(selectedMatterPlannerDriftSummary?.severity || 'low').toLowerCase() === 'medium'
                              ? 'bg-amber-100 text-amber-700'
                              : 'bg-emerald-100 text-emerald-700'
                        }`}>
                          {String(selectedMatterPlannerDriftSummary?.severity || 'low')}
                        </span>
                      </div>
                      <div className="text-xs text-gray-500">
                        v{selectedMatterPlannerCompare.fromVersionNumber ?? '?'} → v{selectedMatterPlannerCompare.toVersionNumber ?? '?'}
                      </div>
                      <div className="grid grid-cols-3 gap-2">
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Hours Drift</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(selectedMatterPlannerDriftSummary?.hoursDriftRatio || 0) * 100)}%
                          </p>
                        </div>
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Collections Drift</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(selectedMatterPlannerDriftSummary?.collectionsDriftRatio || 0) * 100)}%
                          </p>
                        </div>
                        <div className="rounded border border-gray-200 bg-gray-50 p-2">
                          <p className="text-[10px] uppercase font-bold text-gray-500">Margin Compression</p>
                          <p className="text-sm font-bold text-slate-800">
                            {Math.round(Number(selectedMatterPlannerDriftSummary?.marginCompressionRatio || 0) * 100)}%
                          </p>
                        </div>
                      </div>

                      {selectedMatterPlannerActuals && (
                        <div className="grid grid-cols-2 gap-2">
                          <div className="rounded border border-gray-200 p-2 bg-white">
                            <p className="text-[10px] uppercase font-bold text-gray-500">Actual Hours</p>
                            <p className="text-sm font-semibold text-slate-800">{Number(selectedMatterPlannerActuals.actualHours || 0).toFixed(2)}</p>
                          </div>
                          <div className="rounded border border-gray-200 p-2 bg-white">
                            <p className="text-[10px] uppercase font-bold text-gray-500">Collected Net</p>
                            <p className="text-sm font-semibold text-slate-800">{formatCurrency(Number(selectedMatterPlannerActuals.collectedNet || 0))}</p>
                          </div>
                        </div>
                      )}

                      {selectedMatterPlannerCompareBaseDelta && (
                        <div className="rounded border border-slate-200 p-2 bg-slate-50">
                          <p className="text-[10px] font-bold uppercase text-slate-500 mb-1">Base Scenario Delta</p>
                          <p className="text-xs text-slate-700">
                            Budget Δ {formatCurrency(Number((selectedMatterPlannerCompareBaseDelta as any)?.delta?.budgetTotal || 0))} •
                            Collected Δ {formatCurrency(Number((selectedMatterPlannerCompareBaseDelta as any)?.delta?.expectedCollected || 0))} •
                            Margin Δ {formatCurrency(Number((selectedMatterPlannerCompareBaseDelta as any)?.delta?.expectedMargin || 0))}
                          </p>
                        </div>
                      )}

                      {selectedMatterPlannerPhaseDeltas.length > 0 && (
                        <div className="rounded border border-slate-200 p-2 bg-white">
                          <p className="text-[10px] font-bold uppercase text-slate-500 mb-2">Phase-Level Delta Chart (Base)</p>
                          <div className="space-y-2">
                            {selectedMatterPlannerPhaseDeltas.slice(0, 7).map((row, idx) => {
                              const delta = (row?.delta || {}) as Record<string, any>;
                              const feeDelta = Number(delta.feeExpected || 0);
                              const hoursDelta = Number(delta.hoursExpected || 0);
                              const feeDeltaRatio = Math.abs(Number(delta.feeDeltaRatio || 0));
                              const widthPct = selectedMatterPlannerPhaseDeltaMaxFeeRatio > 0
                                ? Math.max(8, Math.round((feeDeltaRatio / selectedMatterPlannerPhaseDeltaMaxFeeRatio) * 100))
                                : 8;
                              const positive = feeDelta >= 0;
                              return (
                                <div key={`${String(row?.phaseCode || 'phase')}-${idx}`} className="grid grid-cols-[110px_1fr_auto] gap-2 items-center">
                                  <div>
                                    <p className="text-[11px] font-semibold text-slate-700 truncate">{String(row?.name || row?.phaseCode || 'Phase')}</p>
                                    <p className="text-[10px] text-gray-500">{hoursDelta >= 0 ? '+' : ''}{hoursDelta.toFixed(1)}h</p>
                                  </div>
                                  <div className="h-2 rounded bg-gray-100 overflow-hidden">
                                    <div className={`h-full rounded ${positive ? 'bg-emerald-400' : 'bg-red-400'}`} style={{ width: `${widthPct}%` }} />
                                  </div>
                                  <div className={`text-[10px] font-bold ${positive ? 'text-emerald-700' : 'text-red-700'}`}>
                                    {feeDelta >= 0 ? '+' : ''}{formatCurrency(feeDelta)}
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {(selectedMatterPlannerCollectionsIntel || selectedMatterPlannerStaffingIntel || selectedMatterPlannerMarginIntel || selectedMatterPlannerStressTests.length > 0) && (
                    <div className="bg-white border border-slate-200 rounded-lg p-3 space-y-3">
                      <p className="text-[11px] font-bold uppercase text-slate-500">Phase 4 Intelligence (Base Scenario)</p>

                      {selectedMatterPlannerCollectionsIntel && (
                        <div className="rounded border border-blue-100 bg-blue-50/40 p-2">
                          <p className="text-[10px] font-bold uppercase text-blue-700 mb-1">Collections Intelligence</p>
                          <div className="text-xs text-slate-700 space-y-1">
                            <p>
                              Trust vs Operating: {Math.round(Number(selectedMatterPlannerCollectionsIntel?.trustFundingBehavior?.trustFundedWeight || 0) * 100)}% /
                              {' '}{Math.round(Number(selectedMatterPlannerCollectionsIntel?.trustFundingBehavior?.operatingFundedWeight || 0) * 100)}%
                            </p>
                            {Array.isArray(selectedMatterPlannerCollectionsIntel?.payorSegments) && selectedMatterPlannerCollectionsIntel.payorSegments.length > 0 && (
                              <p>
                                Payor Mix:{' '}
                                {selectedMatterPlannerCollectionsIntel.payorSegments
                                  .slice(0, 3)
                                  .map((seg: any) => `${String(seg.segment)} ${Math.round(Number(seg.weight || 0) * 100)}%`)
                                  .join(' • ')}
                              </p>
                            )}
                            {selectedMatterPlannerCollectionsIntel?.paymentRailImpact && (
                              <p>
                                Rail Impact:{' '}
                                {['card', 'ach', 'echeck'].map((rail) => {
                                  const item = selectedMatterPlannerCollectionsIntel.paymentRailImpact?.[rail];
                                  if (!item) return null;
                                  return `${rail.toUpperCase()} speed ${Math.round(Number(item.collectionSpeedAdj || 0) * 100)}% / fee ${Math.round(Number(item.feeCostAdj || 0) * 100)}%`;
                                }).filter(Boolean).join(' • ')}
                              </p>
                            )}
                            <p className="text-[11px] text-blue-700">{String(selectedMatterPlannerCollectionsIntel?.trustFundingBehavior?.guidance || '')}</p>
                          </div>
                        </div>
                      )}

                      {selectedMatterPlannerStaffingIntel && (
                        <div className="rounded border border-violet-100 bg-violet-50/40 p-2">
                          <p className="text-[10px] font-bold uppercase text-violet-700 mb-1">Staffing Intelligence</p>
                          <div className="text-xs text-slate-700 space-y-1">
                            <p>
                              Blended Rates: Bill {formatCurrency(Number(selectedMatterPlannerStaffingIntel?.blendedRates?.bill || 0))} / Cost {formatCurrency(Number(selectedMatterPlannerStaffingIntel?.blendedRates?.cost || 0))}
                            </p>
                            <p>
                              Handoff Cost: {Number(selectedMatterPlannerStaffingIntel?.handoffCost?.expectedHours || 0).toFixed(1)}h / {formatCurrency(Number(selectedMatterPlannerStaffingIntel?.handoffCost?.expectedCost || 0))}
                            </p>
                            {Array.isArray(selectedMatterPlannerStaffingIntel?.utilizationAwareSuggestions) && selectedMatterPlannerStaffingIntel.utilizationAwareSuggestions.length > 0 && (
                              <div className="space-y-1">
                                {selectedMatterPlannerStaffingIntel.utilizationAwareSuggestions.slice(0, 3).map((s: any, idx: number) => (
                                  <p key={`${String(s.role || 'role')}-${idx}`} className="text-[11px]">
                                    {String(s.role)}: {Number(s.suggestedHoursDelta || 0) >= 0 ? '+' : ''}{Number(s.suggestedHoursDelta || 0).toFixed(1)}h — {String(s.reason || '')}
                                  </p>
                                ))}
                              </div>
                            )}
                          </div>
                        </div>
                      )}

                      {selectedMatterPlannerMarginIntel && (
                        <div className="rounded border border-emerald-100 bg-emerald-50/40 p-2">
                          <p className="text-[10px] font-bold uppercase text-emerald-700 mb-1">Margin Intelligence</p>
                          <div className="grid grid-cols-2 gap-2 text-xs text-slate-700">
                            <p>Blended Realization: {Math.round(Number(selectedMatterPlannerMarginIntel?.blendedRateRealization || 0) * 100)}%</p>
                            <p>Gross Margin: {Math.round(Number(selectedMatterPlannerMarginIntel?.grossMargin || 0) * 100)}%</p>
                            <p>Write-off Risk: {Math.round(Number(selectedMatterPlannerMarginIntel?.writeOffRisk || 0) * 100)}%</p>
                            <p>Prebill Adj Risk: {Math.round(Number(selectedMatterPlannerMarginIntel?.prebillAdjustmentRisk || 0) * 100)}%</p>
                          </div>
                        </div>
                      )}

                      {selectedMatterPlannerStressTests.length > 0 && (
                        <div className="rounded border border-amber-100 bg-amber-50/40 p-2">
                          <p className="text-[10px] font-bold uppercase text-amber-700 mb-1">Stress Tests</p>
                          <div className="space-y-1">
                            {selectedMatterPlannerStressTests.slice(0, 4).map((test, idx) => (
                              <p key={`${String(test.key || 'stress')}-${idx}`} className="text-[11px] text-slate-700">
                                <span className="font-semibold">{String(test.key || '').replaceAll('_', ' ')}</span>:
                                {' '}Δ Margin {formatCurrency(Number(test.deltaMargin || 0))}
                                {typeof test.deltaHours !== 'undefined' ? ` • Δ Hours ${Number(test.deltaHours || 0).toFixed(1)}` : ''}
                              </p>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  <div className="bg-white border border-slate-200 rounded-lg p-3 space-y-3">
                    <div className="flex items-center justify-between gap-2">
                      <div>
                        <p className="text-[11px] font-bold uppercase text-slate-500">Phase 5 Calibration &amp; Learning Loop</p>
                        <p className="text-[11px] text-gray-500">Cohort calibration snapshots, shadow rollout, and outcome feedback</p>
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          type="button"
                          onClick={() => selectedMatter?.id && refreshSelectedMatterCalibration(selectedMatter.id, true)}
                          disabled={!selectedMatter?.id || selectedMatterCalibrationLoading}
                          className="px-2 py-1 text-[11px] font-bold rounded border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                        >
                          {selectedMatterCalibrationLoading ? 'Refreshing...' : 'Refresh'}
                        </button>
                        <button
                          type="button"
                          onClick={runOutcomePlannerCalibrationShadowJob}
                          disabled={!selectedMatter?.id || selectedMatterCalibrationJobRunning}
                          className="px-2 py-1 text-[11px] font-bold rounded border border-blue-200 bg-blue-50 text-blue-700 hover:bg-blue-100 disabled:opacity-50"
                        >
                          {selectedMatterCalibrationJobRunning ? 'Running...' : 'Run Shadow Calibration'}
                        </button>
                      </div>
                    </div>

                    {selectedMatterCalibrationLoading && !selectedMatterCalibration ? (
                      <div className="text-xs text-gray-500">Loading calibration...</div>
                    ) : (
                      <>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                          <div className="rounded border border-emerald-100 bg-emerald-50/40 p-2">
                            <div className="flex items-center justify-between gap-2 mb-1">
                              <p className="text-[10px] font-bold uppercase text-emerald-700">Active Calibration</p>
                              <button
                                type="button"
                                onClick={rollbackSelectedMatterActiveCalibration}
                                disabled={!selectedMatterCalibrationActiveSnapshot?.id || selectedMatterCalibrationActionRunning}
                                className="px-2 py-0.5 text-[10px] font-bold rounded border border-emerald-200 bg-white text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                              >
                                Rollback
                              </button>
                            </div>
                            {selectedMatterCalibrationActiveSnapshot ? (
                              <div className="text-[11px] text-slate-700 space-y-1">
                                <p className="font-semibold truncate">{String(selectedMatterCalibrationActiveSnapshot.cohortKey || 'n/a')}</p>
                                <p>Status: {String(selectedMatterCalibrationActiveSnapshot.status || 'unknown')} • Sample {Number(selectedMatterCalibrationActiveSnapshot.sampleSize || 0)}</p>
                                <p>As Of: {String(selectedMatterCalibrationActiveSnapshot.asOfDate || '').slice(0, 10) || 'n/a'}</p>
                                <p>
                                  Confidence: {Math.round(Number((selectedMatterCalibrationActiveMetrics?.confidenceScore as number) || 0) * 100)}%
                                  {' '}• Hours x{Number((((selectedMatterCalibrationActivePayload?.tuningSuggestions as any)?.globalHoursMultiplier) || 1)).toFixed(2)}
                                  {' '}• Collections x{Number((((selectedMatterCalibrationActivePayload?.tuningSuggestions as any)?.globalCollectionsMultiplier) || 1)).toFixed(2)}
                                </p>
                              </div>
                            ) : (
                              <p className="text-[11px] text-gray-500">No active calibration snapshot for this cohort.</p>
                            )}
                          </div>

                          <div className="rounded border border-amber-100 bg-amber-50/40 p-2">
                            <div className="flex items-center justify-between gap-2 mb-1">
                              <p className="text-[10px] font-bold uppercase text-amber-700">Shadow Calibration</p>
                              <button
                                type="button"
                                onClick={activateSelectedMatterShadowCalibration}
                                disabled={!selectedMatterCalibrationShadowSnapshot?.id || selectedMatterCalibrationActionRunning}
                                className="px-2 py-0.5 text-[10px] font-bold rounded border border-amber-200 bg-white text-amber-700 hover:bg-amber-50 disabled:opacity-50"
                              >
                                Promote to Active
                              </button>
                            </div>
                            {selectedMatterCalibrationShadowSnapshot ? (
                              <div className="text-[11px] text-slate-700 space-y-1">
                                <p className="font-semibold truncate">{String(selectedMatterCalibrationShadowSnapshot.cohortKey || 'n/a')}</p>
                                <p>Status: {String(selectedMatterCalibrationShadowSnapshot.status || 'unknown')} • Sample {Number(selectedMatterCalibrationShadowSnapshot.sampleSize || 0)}</p>
                                <p>As Of: {String(selectedMatterCalibrationShadowSnapshot.asOfDate || '').slice(0, 10) || 'n/a'}</p>
                                <p>
                                  Confidence: {Math.round(Number((selectedMatterCalibrationShadowMetrics?.confidenceScore as number) || 0) * 100)}%
                                  {' '}• Hours x{Number((((selectedMatterCalibrationShadowPayload?.tuningSuggestions as any)?.globalHoursMultiplier) || 1)).toFixed(2)}
                                  {' '}• Collections x{Number((((selectedMatterCalibrationShadowPayload?.tuningSuggestions as any)?.globalCollectionsMultiplier) || 1)).toFixed(2)}
                                </p>
                              </div>
                            ) : (
                              <p className="text-[11px] text-gray-500">No shadow calibration snapshot available yet.</p>
                            )}
                          </div>
                        </div>

                        {Array.isArray(selectedMatterCalibration?.candidateCohorts) && selectedMatterCalibration.candidateCohorts.length > 0 && (
                          <div className="rounded border border-slate-200 bg-slate-50 p-2">
                            <p className="text-[10px] font-bold uppercase text-slate-500 mb-1">Candidate Cohorts</p>
                            <div className="space-y-1">
                              {selectedMatterCalibration.candidateCohorts.slice(0, 5).map((row, idx) => (
                                <p key={`${String(row?.cohortKey || 'cohort')}-${idx}`} className="text-[11px] text-slate-700 truncate">
                                  {String(row?.scope || 'scope')}: {String(row?.cohortKey || '')}
                                </p>
                              ))}
                            </div>
                          </div>
                        )}

                        <div className="rounded border border-slate-200 p-3 bg-white space-y-2">
                          <p className="text-[10px] font-bold uppercase text-slate-500">Outcome Feedback (Calibration Labels)</p>
                          <div className="grid grid-cols-2 gap-2">
                            <select
                              value={selectedMatterOutcomeFeedbackDraft.actualOutcome}
                              onChange={(e) => setSelectedMatterOutcomeFeedbackDraft(prev => ({ ...prev, actualOutcome: e.target.value }))}
                              className="px-2 py-1.5 text-xs border border-gray-200 rounded-md bg-white text-slate-700"
                            >
                              <option value="">Outcome (optional)</option>
                              <option value="settled">Settled</option>
                              <option value="dismissed">Dismissed</option>
                              <option value="trial_win">Trial Win</option>
                              <option value="trial_loss">Trial Loss</option>
                              <option value="adverse">Adverse</option>
                              <option value="other">Other</option>
                            </select>
                            <input
                              type="number"
                              step="0.01"
                              placeholder="Actual Fees Collected"
                              value={selectedMatterOutcomeFeedbackDraft.actualFeesCollected}
                              onChange={(e) => setSelectedMatterOutcomeFeedbackDraft(prev => ({ ...prev, actualFeesCollected: e.target.value }))}
                              className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                            />
                            <input
                              type="number"
                              step="0.01"
                              placeholder="Actual Cost"
                              value={selectedMatterOutcomeFeedbackDraft.actualCost}
                              onChange={(e) => setSelectedMatterOutcomeFeedbackDraft(prev => ({ ...prev, actualCost: e.target.value }))}
                              className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                            />
                            <input
                              type="number"
                              step="0.01"
                              placeholder="Actual Margin"
                              value={selectedMatterOutcomeFeedbackDraft.actualMargin}
                              onChange={(e) => setSelectedMatterOutcomeFeedbackDraft(prev => ({ ...prev, actualMargin: e.target.value }))}
                              className="px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                            />
                          </div>
                          <textarea
                            value={selectedMatterOutcomeFeedbackDraft.notes}
                            onChange={(e) => setSelectedMatterOutcomeFeedbackDraft(prev => ({ ...prev, notes: e.target.value }))}
                            placeholder="Calibration notes (optional)"
                            rows={2}
                            className="w-full px-2 py-1.5 text-xs border border-gray-200 rounded-md"
                          />
                          <div className="flex justify-end">
                            <button
                              type="button"
                              onClick={recordSelectedMatterPlannerOutcomeFeedback}
                              disabled={!selectedMatterPlanner?.plan?.id || selectedMatterOutcomeFeedbackSaving}
                              className="px-3 py-1.5 text-xs font-bold rounded border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                            >
                              {selectedMatterOutcomeFeedbackSaving ? 'Saving...' : 'Record Outcome Feedback'}
                            </button>
                          </div>
                        </div>
                      </>
                    )}
                  </div>
                  </div>
                )}
              </MatterAnalysisSection>
            </div>

            {/* Timeline */}
            <div className="p-6 bg-white">
              <h3 className="font-bold text-slate-800 mb-6 text-sm uppercase tracking-wide flex items-center gap-2">
                <Clock className="w-4 h-4 text-gray-400" /> Case History
              </h3>

              <div className="relative border-l-2 border-gray-100 ml-3 space-y-8 pb-8">
                {getTimeline(selectedMatter.id).map((event, idx) => (
                  <div key={idx} className="relative pl-8 group">
                    {/* Dot */}
                    <div className={`absolute -left-[9px] top-0 w-4 h-4 rounded-full border-2 border-white shadow-sm flex items-center justify-center 
                                      ${event.type === 'opened' ? 'bg-emerald-500' : event.type === 'doc' ? 'bg-blue-500' : 'bg-amber-500'}`}>
                    </div>

                    <div className="flex flex-col">
                      <span className="text-[10px] font-bold text-gray-400 uppercase tracking-wide mb-0.5">
                        {formatDate(event.date)}
                      </span>
                      <span className="text-sm font-bold text-slate-800">{event.title}</span>
                      <p className="text-xs text-gray-500 mt-1 bg-gray-50 p-2 rounded border border-gray-100 inline-block group-hover:bg-white group-hover:shadow-sm transition-all">
                        {event.detail}
                      </p>
                    </div>
                  </div>
                ))}

                {/* "Start" dot */}
                <div className="relative pl-8">
                  <div className="absolute -left-[5px] top-0 w-2 h-2 rounded-full bg-gray-200"></div>
                </div>
              </div>

              {/* Matter Tasks Section */}
              <div className="mt-8">
                <h3 className="font-bold text-slate-800 mb-4 text-sm uppercase tracking-wide flex items-center gap-2">
                  <span className="w-4 h-4 text-purple-500">📋</span> Related Tasks
                </h3>
                {tasks.filter(t => t.matterId === selectedMatter.id).length === 0 ? (
                  <p className="text-sm text-gray-400 italic">No tasks assigned to this matter.</p>
                ) : (
                  <div className="space-y-2">
                    {tasks.filter(t => t.matterId === selectedMatter.id).map(task => (
                      <div key={task.id} className={`p-3 rounded-lg border ${task.status === 'Done' ? 'bg-gray-50 border-gray-200' : 'bg-white border-gray-200'}`}>
                        <div className="flex justify-between items-start">
                          <div>
                            <p className={`font-medium text-sm ${task.status === 'Done' ? 'text-gray-400 line-through' : 'text-slate-800'}`}>{task.title}</p>
                            {task.dueDate && (
                              <p className="text-xs text-gray-500 mt-1">
                                Due: {formatDate(task.dueDate)}
                              </p>
                            )}
                          </div>
                          <span className={`text-xs px-2 py-0.5 rounded-full font-bold ${task.priority === 'High' ? 'bg-red-100 text-red-700' :
                            task.priority === 'Medium' ? 'bg-amber-100 text-amber-700' :
                              'bg-gray-100 text-gray-600'
                            }`}>{task.priority}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            <MatterNotesPanel matterId={selectedMatter.id} />

            {/* Actions Footer */}
            <div className="p-4 border-t border-gray-100 bg-gray-50 flex gap-2">
              <Can perform="matter.edit">
                <button
                  className="flex-1 py-2 bg-white border border-gray-200 rounded-lg text-sm font-bold text-gray-600 hover:bg-gray-100 shadow-sm"
                  onClick={() => {
                    setEditData({
                      ...selectedMatter,
                      relatedClientIds: Array.isArray(selectedMatter.relatedClientIds)
                        ? selectedMatter.relatedClientIds
                        : (selectedMatter.relatedClients || []).map((client) => client.id)
                    });
                    resetOutcomePlannerState();
                    setShowModal(true);
                  }}
                >
                  Edit Matter
                </button>
              </Can>
              <Can perform="matter.delete">
                <button
                  className="py-2 px-3 bg-white border border-red-200 text-red-600 rounded-lg text-sm font-bold hover:bg-red-50 shadow-sm flex items-center gap-2"
                  onClick={async () => {
                    if (selectedMatter) {
                      try {
                        await deleteMatter(selectedMatter.id);
                        toast.success('Matter deleted.');
                        setSelectedMatter(null);
                      } catch (error: any) {
                        const message = error?.message || 'Matter could not be deleted.';
                        toast.error(message);
                      }
                    }
                  }}
                >
                  <Trash className="w-4 h-4" /> Delete
                </button>
              </Can>
              <button
                className="flex-1 py-2 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 shadow-lg"
                onClick={() => setShowDocs(true)}
              >
                Open Folder
              </button>
              <button
                className="py-2 px-3 bg-white border border-gray-200 text-slate-600 rounded-lg text-sm font-bold hover:bg-gray-100 shadow-sm flex items-center gap-2"
                onClick={() => {
                  if (selectedMatter) {
                    updateMatter(selectedMatter.id, { status: CaseStatus.Archived });
                    toast.success(t('matter_archived'));
                    setSelectedMatter(null);
                  }
                }}
              >
                Archive
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create Modal */}
      {showModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg max-h-[90vh] overflow-hidden animate-in fade-in zoom-in duration-200 flex flex-col">
            <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <h3 className="font-bold text-lg text-slate-800">{editData ? 'Edit Matter' : t('create_matter_modal')}</h3>
              <button type="button" onClick={closeMatterModal} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
            </div>
            <form onSubmit={editData ? (e) => {
              e.preventDefault();
              if (editData) {
                updateMatter(editData.id!, {
                  name: editData.name,
                  caseNumber: editData.caseNumber,
                  practiceArea: editData.practiceArea,
                  feeStructure: editData.feeStructure,
                  status: editData.status,
                  billableRate: editData.billableRate,
                  trustBalance: editData.trustBalance,
                  courtType: editData.courtType,
                  bailStatus: editData.bailStatus,
                  bailAmount: editData.bailAmount,
                  outcome: editData.outcome,
                  shareWithFirm: !!editData.shareWithFirm,
                  shareBillingWithFirm: !!editData.shareWithFirm && !!editData.shareBillingWithFirm,
                  shareNotesWithFirm: !!editData.shareWithFirm && !!editData.shareBillingWithFirm && !!editData.shareNotesWithFirm,
                  entityId: editData.entityId,
                  officeId: editData.officeId,
                  relatedClientIds: []
                });
                setShowModal(false);
                setEditData(null);
                resetOutcomePlannerState();
              }
            } : handleSubmit} className="p-6 space-y-4 overflow-y-auto flex-1">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">{t('case_name')}</label>
                <input required type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none" value={editData ? editData.name || '' : formData.name} onChange={e => editData ? setEditData({ ...editData, name: e.target.value }) : setFormData({ ...formData, name: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">{t('case_number') || 'Case Number'}</label>
                <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none" placeholder="e.g. 2024/123" value={editData ? editData.caseNumber || '' : formData.caseNumber} onChange={e => editData ? setEditData({ ...editData, caseNumber: e.target.value }) : setFormData({ ...formData, caseNumber: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Entity & Office</label>
                <EntityOfficeFilter
                  entityId={formEntityId || ''}
                  officeId={formOfficeId || ''}
                  onEntityChange={(value) => {
                    if (editData) {
                      setEditData({ ...editData, entityId: value, officeId: '' });
                    } else {
                      setFormData({ ...formData, entityId: value, officeId: '' });
                    }
                  }}
                  onOfficeChange={(value) => {
                    if (editData) {
                      setEditData({ ...editData, officeId: value });
                    } else {
                      setFormData({ ...formData, officeId: value });
                    }
                  }}
                />
              </div>
              {!editData && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">{t('select_client_or_lead')}</label>
                  <button
                    type="button"
                    onClick={() => setShowClientSelector(true)}
                    className={`w-full border rounded-lg p-2.5 text-sm text-left flex items-center justify-between transition-colors ${formData.partyId
                      ? 'border-primary-300 bg-primary-50 text-slate-900'
                      : 'border-gray-300 bg-white text-gray-500'
                      } hover:border-primary-400 focus:ring-2 focus:ring-primary-500 outline-none`}
                  >
                    <div className="flex items-center gap-2">
                      <Users className="w-4 h-4" />
                      {formData.partyId && selectedPartyName ? (
                        <span className="font-medium text-slate-800">
                          {selectedPartyName}
                          <span className={`ml-2 text-xs px-1.5 py-0.5 rounded ${formData.partyType === 'client' ? 'bg-blue-100 text-blue-700' : 'bg-amber-100 text-amber-700'
                            }`}>
                            {formData.partyType === 'client' ? 'Client' : 'Lead'}
                          </span>
                        </span>
                      ) : (
                        <span>{t('select_client_placeholder')}</span>
                      )}
                    </div>
                    <ChevronRight className="w-4 h-4 text-gray-400" />
                  </button>
                  {formData.partyType === 'lead' && formData.partyId && (
                    <p className="text-xs text-amber-600 mt-1">{t('lead_selection_hint')}</p>
                  )}
                  <button
                    type="button"
                    onClick={() => { resetInlineClientForm(); setShowNewClientModal(true); }}
                    className="mt-2 text-xs text-primary-600 hover:underline font-medium"
                  >
                    + Add New Client
                  </button>
                </div>
              )}
              {matterSecondaryClientsEnabled && (
              <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-3">
                <div>
                  <h4 className="text-sm font-bold text-slate-800">Client Access</h4>
                  <p className="text-xs text-slate-500 mt-1">
                    Primary client stays as the main billing/contact record. Additional clients are optional and can view the linked case calendar in the client portal.
                  </p>
                </div>
                {editData && (
                  <div>
                    <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Primary Client</label>
                    <div className="rounded-lg border border-gray-200 bg-white px-3 py-2.5 text-sm font-medium text-slate-800">
                      {resolveMatterClient(editData as Matter)?.name || 'Unknown Client'}
                    </div>
                  </div>
                )}
                <div>
                  <label className="block text-xs font-semibold text-gray-600 mb-2 uppercase">Additional Clients (optional)</label>
                  {additionalClientOptions.length === 0 ? (
                    <div className="rounded-lg border border-dashed border-gray-200 bg-white px-3 py-3 text-sm text-gray-500">
                      No other clients available to link.
                    </div>
                  ) : (
                    <div className="max-h-40 space-y-2 overflow-y-auto rounded-lg border border-gray-200 bg-white p-3">
                      {additionalClientOptions.map((client) => {
                        const selectedIds = editData
                          ? (Array.isArray(editData.relatedClientIds) ? editData.relatedClientIds : [])
                          : formData.relatedClientIds;
                        const checked = selectedIds.includes(client.id);
                        return (
                          <label key={client.id} className="flex items-start gap-3">
                            <input
                              type="checkbox"
                              className="mt-1 h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                              checked={checked}
                              onChange={(e) => toggleRelatedClientId(client.id, e.target.checked)}
                            />
                            <div>
                              <p className="text-sm font-medium text-slate-800">{client.name}</p>
                              <p className="text-xs text-gray-500">{client.email}</p>
                            </div>
                          </label>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>
              )}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">{t('practice_area')}</label>
                  <select className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none" value={editData ? editData.practiceArea : formData.practiceArea} onChange={e => editData ? setEditData({ ...editData, practiceArea: e.target.value as PracticeArea }) : setFormData({ ...formData, practiceArea: e.target.value as PracticeArea })}>
                    {Object.values(PracticeArea).map(pa => <option key={pa} value={pa}>{pa}</option>)}
                  </select>
                </div>
                <div>
                  <Combobox
                    label={t('court_type') || 'Court Type'}
                    value={editData ? editData.courtType || '' : formData.courtType}
                    options={courtOptions}
                    onChange={(val) => editData ? setEditData({ ...editData, courtType: val }) : setFormData({ ...formData, courtType: val })}
                    placeholder="Select Court Type"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">{t('fee_structure')}</label>
                  <select className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none" value={editData ? editData.feeStructure : formData.feeStructure} onChange={e => editData ? setEditData({ ...editData, feeStructure: e.target.value as FeeStructure }) : setFormData({ ...formData, feeStructure: e.target.value as FeeStructure })}>
                    <option value={FeeStructure.Hourly}>{t('fee_hourly')}</option>
                    <option value={FeeStructure.FlatFee}>{t('fee_flat')}</option>
                    <option value={FeeStructure.Contingency}>{t('fee_contingency')}</option>
                  </select>
                </div>
              </div>

              {!editData && (
                <div className="pt-4 border-t border-gray-100 space-y-3">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <h4 className="text-sm font-bold text-slate-800">Outcome-to-Fee Planner (MVP)</h4>
                      <p className="text-xs text-gray-500 mt-1">
                        Deterministic intake forecast preview (budget, collections, margin). Can be auto-saved as version after matter creation.
                      </p>
                    </div>
                    <label className="flex items-center gap-2 text-xs font-medium text-gray-700">
                      <input
                        type="checkbox"
                        checked={outcomePlannerDraft.enabled}
                        onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, enabled: e.target.checked }))}
                      />
                      Enable
                    </label>
                  </div>

                  {outcomePlannerDraft.enabled && (
                    <>
                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Complexity</label>
                          <select
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                            value={outcomePlannerDraft.complexity}
                            onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, complexity: e.target.value as PlannerComplexity }))}
                          >
                            <option value="low">Low</option>
                            <option value="medium">Medium</option>
                            <option value="high">High</option>
                          </select>
                        </div>
                        <div>
                          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Claim Size Band</label>
                          <select
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                            value={outcomePlannerDraft.claimSizeBand}
                            onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, claimSizeBand: e.target.value as PlannerClaimSizeBand }))}
                          >
                            <option value="small">Small</option>
                            <option value="medium">Medium</option>
                            <option value="large">Large</option>
                            <option value="enterprise">Enterprise</option>
                          </select>
                        </div>
                        <div>
                          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Payor Profile</label>
                          <select
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                            value={outcomePlannerDraft.primaryPayorProfile}
                            onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, primaryPayorProfile: e.target.value as PlannerPayorProfile }))}
                          >
                            <option value="client">Client</option>
                            <option value="corporate">Corporate</option>
                            <option value="third_party">Third Party</option>
                          </select>
                        </div>
                        <div>
                          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Jurisdiction Code</label>
                          <input
                            type="text"
                            placeholder="e.g. us-ca-state"
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                            value={outcomePlannerDraft.jurisdictionCode}
                            onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, jurisdictionCode: e.target.value }))}
                          />
                        </div>
                        <div>
                          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Billable Rate Override</label>
                          <input
                            type="number"
                            placeholder="Optional"
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                            value={outcomePlannerDraft.baseBillableRateOverride}
                            onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, baseBillableRateOverride: e.target.value }))}
                          />
                        </div>
                        <div className="flex items-end">
                          <label className="flex items-center gap-2 text-sm text-gray-700 font-medium">
                            <input
                              type="checkbox"
                              checked={outcomePlannerDraft.autoSave}
                              onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, autoSave: e.target.checked }))}
                            />
                            Save planner version on matter creation
                          </label>
                        </div>
                      </div>

                      <div>
                        <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Planner Notes (optional)</label>
                        <textarea
                          rows={2}
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                          placeholder="Manual assumptions / client-specific context"
                          value={outcomePlannerDraft.notes}
                          onChange={(e) => setOutcomePlannerDraft(prev => ({ ...prev, notes: e.target.value }))}
                        />
                      </div>

                      {outcomePlannerPreview && (
                        <div className="space-y-3">
                          <div className="flex items-center justify-between gap-3">
                            <p className="text-xs font-bold text-slate-700 uppercase tracking-wide">Planner Preview</p>
                            <button
                              type="button"
                              onClick={() => setShowOutcomePlannerPreview(prev => !prev)}
                              className="inline-flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
                            >
                              {showOutcomePlannerPreview ? 'Hide' : 'Show'}
                            </button>
                          </div>
                          {showOutcomePlannerPreview ? (
                            <>
                              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                                {outcomePlannerPreview.scenarios.map((scenario) => (
                                  <div key={scenario.key} className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                                    <div className="flex items-center justify-between mb-2">
                                      <p className="text-sm font-bold text-slate-800">{scenario.name}</p>
                                      <span className="text-[10px] font-bold text-gray-500 uppercase">{Math.round(scenario.probability * 100)}%</span>
                                    </div>
                                    <div className="space-y-1 text-xs">
                                      <div className="flex justify-between"><span className="text-gray-500">Budget</span><span className="font-semibold text-slate-800">{formatCurrency(scenario.budgetTotal)}</span></div>
                                      <div className="flex justify-between"><span className="text-gray-500">Collected</span><span className="font-semibold text-slate-800">{formatCurrency(scenario.expectedCollected)}</span></div>
                                      <div className="flex justify-between"><span className="text-gray-500">Margin</span><span className={`font-semibold ${scenario.expectedMargin >= 0 ? 'text-emerald-700' : 'text-red-700'}`}>{formatCurrency(scenario.expectedMargin)}</span></div>
                                      <div className="flex justify-between"><span className="text-gray-500">Confidence</span><span className="font-semibold text-slate-800">{Math.round(scenario.confidenceScore * 100)}% ({scenario.confidenceBand})</span></div>
                                      <div className="flex justify-between"><span className="text-gray-500">Coverage</span><span className="font-semibold text-slate-800">{Math.round(scenario.dataCoverageScore * 100)}%</span></div>
                                    </div>
                                    <p className="text-[11px] text-gray-500 mt-2">{scenario.driverSummary}</p>
                                    <div className="mt-2 space-y-1">
                                      <p className="text-[10px] font-bold text-gray-500 uppercase tracking-wide">Outcome Mix</p>
                                      <div className="grid grid-cols-2 gap-x-2 gap-y-1 text-[10px] text-gray-600">
                                        <span>Settle {Math.round(scenario.outcomeProbabilities.settle * 100)}%</span>
                                        <span>Dismiss {Math.round(scenario.outcomeProbabilities.dismiss * 100)}%</span>
                                        <span>Trial {Math.round(scenario.outcomeProbabilities.trial * 100)}%</span>
                                        <span>Adverse {Math.round(scenario.outcomeProbabilities.adverse * 100)}%</span>
                                      </div>
                                    </div>
                                    {scenario.riskFlags.length > 0 && (
                                      <div className="mt-2 flex flex-wrap gap-1">
                                        {scenario.riskFlags.slice(0, 3).map(flag => (
                                          <span key={flag} className="text-[10px] font-medium px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 border border-amber-200">
                                            {flag}
                                          </span>
                                        ))}
                                      </div>
                                    )}
                                    <p className="text-[10px] text-gray-500 mt-2">Sensitivity: {scenario.inputSensitivitySummary}</p>
                                  </div>
                                ))}
                              </div>

                              <div className="rounded-lg border border-gray-200 bg-white p-3">
                                <p className="text-xs font-bold text-slate-700 uppercase tracking-wide mb-2">Assumptions</p>
                                <div className="grid grid-cols-1 md:grid-cols-2 gap-x-4 gap-y-1">
                                  {outcomePlannerPreview.assumptions.map((assumption) => (
                                    <div key={assumption.key} className="flex justify-between gap-3 text-xs">
                                      <span className="text-gray-500">{assumption.key}</span>
                                      <span className="font-medium text-slate-700 text-right">{assumption.value}</span>
                                    </div>
                                  ))}
                                </div>
                                <p className="text-[11px] text-gray-500 mt-2">
                                  Preview uses deterministic planner assumptions. If enabled, a version is persisted immediately after matter creation.
                                </p>
                              </div>
                            </>
                          ) : (
                            <div className="rounded-lg border border-dashed border-gray-200 bg-gray-50 px-3 py-4 text-xs text-gray-500">
                              Planner preview is hidden. Use Show to inspect the scenario cards and assumptions again.
                            </div>
                          )}
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
              {editData && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
                  <select className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none" value={editData.status} onChange={e => setEditData({ ...editData, status: e.target.value as CaseStatus })}>
                    {Object.values(CaseStatus).map(s => <option key={s} value={s}>{s}</option>)}
                  </select>
                </div>
              )}
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">{t('trust_account')} (Initial Deposit)</label>
                <input
                  type="number"
                  placeholder="0.00"
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none"
                  value={editData ? editData.trustBalance ?? 0 : formData.trustAmount}
                  onChange={e => {
                    const val = e.target.value;
                    if (editData) {
                      setEditData({ ...editData, trustBalance: val === '' ? 0 : parseFloat(val) });
                    } else {
                      setFormData({ ...formData, trustAmount: val === '' ? '' : parseFloat(val) });
                    }
                  }}
                />
              </div>

              {/* Bail System (Kefalet) */}
              <div className="pt-4 border-t border-gray-100">
                <h4 className="text-sm font-bold text-slate-800 mb-3">{t('bail_info') || 'Bail / Bond Information'}</h4>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('bail_status')}</label>
                    <select
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                      value={editData ? editData.bailStatus || 'None' : formData.bailStatus}
                      onChange={e => editData ? setEditData({ ...editData, bailStatus: e.target.value as any }) : setFormData({ ...formData, bailStatus: e.target.value })}
                    >
                      <option value="None">None</option>
                      <option value="Set">Set</option>
                      <option value="Posted">Posted</option>
                      <option value="Returned">Returned</option>
                      <option value="Forfeited">Forfeited</option>
                      <option value="Exonerated">Exonerated</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('bail_amount')}</label>
                    <div className="relative">
                      <span className="absolute left-3 top-2.5 text-gray-400 text-xs">$</span>
                      <input
                        type="number"
                        placeholder="0.00"
                        className="w-full pl-6 border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                        value={editData ? editData.bailAmount || '' : formData.bailAmount}
                        onChange={e => {
                          const val = e.target.value;
                          if (editData) setEditData({ ...editData, bailAmount: val ? parseFloat(val) : 0 });
                          else setFormData({ ...formData, bailAmount: val });
                        }}
                      />
                    </div>
                  </div>
                </div>
                <div className="mt-3">
                  <label className="block text-sm font-medium text-gray-700 mb-1">{t('case_outcome')}</label>
                  <input
                    type="text"
                    placeholder="e.g. Acquitted, Convicted, Dismissed..."
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                    value={editData ? editData.outcome || '' : formData.outcome}
                    onChange={e => editData ? setEditData({ ...editData, outcome: e.target.value }) : setFormData({ ...formData, outcome: e.target.value })}
                  />
                </div>
              </div>

              <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-3">
                <div>
                  <h4 className="text-sm font-bold text-slate-800">Firm Sharing</h4>
                  <p className="text-xs text-slate-500 mt-1">Choose what other users in this firm can see for this matter.</p>
                </div>

                <label className="flex items-start gap-3">
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                    checked={editData ? !!editData.shareWithFirm : formData.shareWithFirm}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      if (editData) {
                        setEditData({
                          ...editData,
                          shareWithFirm: checked,
                          shareBillingWithFirm: checked ? !!editData.shareBillingWithFirm : false,
                          shareNotesWithFirm: checked ? !!editData.shareNotesWithFirm && !!editData.shareBillingWithFirm : false
                        });
                      } else {
                        setFormData({
                          ...formData,
                          shareWithFirm: checked,
                          shareBillingWithFirm: checked ? formData.shareBillingWithFirm : false,
                          shareNotesWithFirm: checked ? formData.shareNotesWithFirm && formData.shareBillingWithFirm : false
                        });
                      }
                    }}
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-800">Share matter with firm</p>
                    <p className="text-xs text-slate-500">Other users in this tenant can view the matter and linked work items.</p>
                  </div>
                </label>

                <label className="flex items-start gap-3">
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 disabled:opacity-50"
                    disabled={editData ? !editData.shareWithFirm : !formData.shareWithFirm}
                    checked={editData ? !!editData.shareBillingWithFirm : formData.shareBillingWithFirm}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      if (editData) {
                        setEditData({
                          ...editData,
                          shareBillingWithFirm: checked,
                          shareNotesWithFirm: checked ? !!editData.shareNotesWithFirm : false
                        });
                      } else {
                        setFormData({
                          ...formData,
                          shareBillingWithFirm: checked,
                          shareNotesWithFirm: checked ? formData.shareNotesWithFirm : false
                        });
                      }
                    }}
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-800">Share invoices and billing</p>
                    <p className="text-xs text-slate-500">Use this only when the team needs access to billing totals and invoice records.</p>
                  </div>
                </label>

                <label className="flex items-start gap-3">
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 disabled:opacity-50"
                    disabled={editData ? (!editData.shareWithFirm || !editData.shareBillingWithFirm) : (!formData.shareWithFirm || !formData.shareBillingWithFirm)}
                    checked={editData ? !!editData.shareNotesWithFirm : formData.shareNotesWithFirm}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      if (editData) {
                        setEditData({ ...editData, shareNotesWithFirm: checked });
                      } else {
                        setFormData({ ...formData, shareNotesWithFirm: checked });
                      }
                    }}
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-800">Share free-text notes</p>
                    <p className="text-xs text-slate-500">Default stays off. Free-text billing notes can contain strategy or sensitive commentary.</p>
                  </div>
                </label>
              </div>

              {/* Opposing Party Section */}
              {!editData && (
                <div className="pt-4 border-t border-gray-100">
                  <h4 className="text-sm font-bold text-slate-800 mb-3 flex items-center gap-2">
                    Opposing Party
                  </h4>
                  <div className="space-y-3">
                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">Opposing Party Name *</label>
                        <input
                          type="text"
                          placeholder="e.g. John Smith, ABC Corporation"
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none"
                          value={formData.opposingPartyName}
                          onChange={e => setFormData({ ...formData, opposingPartyName: e.target.value })}
                        />
                      </div>
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">Party Type</label>
                        <select
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
                          value={formData.opposingPartyType}
                          onChange={e => setFormData({ ...formData, opposingPartyType: e.target.value as any })}
                        >
                          <option value="Individual">Individual</option>
                          <option value="Corporation">Corporation</option>
                          <option value="Government">Government</option>
                        </select>
                      </div>
                    </div>
                    {formData.opposingPartyType === 'Corporation' && (
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">Company Name</label>
                        <input
                          type="text"
                          placeholder="Corporation legal name"
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 focus:ring-2 focus:ring-primary-500 outline-none"
                          value={formData.opposingPartyCompany}
                          onChange={e => setFormData({ ...formData, opposingPartyCompany: e.target.value })}
                        />
                      </div>
                    )}
                    <div className="bg-gray-50 p-3 rounded-lg space-y-3">
                      <p className="text-xs font-bold text-gray-600">Opposing Counsel</p>
                      <div className="grid grid-cols-2 gap-3">
                        <input
                          type="text"
                          placeholder="Counsel Name"
                          className="border border-gray-200 rounded-lg p-2 text-sm bg-white outline-none"
                          value={formData.opposingCounselName}
                          onChange={e => setFormData({ ...formData, opposingCounselName: e.target.value })}
                        />
                        <input
                          type="text"
                          placeholder="Law Firm"
                          className="border border-gray-200 rounded-lg p-2 text-sm bg-white outline-none"
                          value={formData.opposingCounselFirm}
                          onChange={e => setFormData({ ...formData, opposingCounselFirm: e.target.value })}
                        />
                      </div>
                      <input
                        type="email"
                        placeholder="Counsel Email"
                        className="w-full border border-gray-200 rounded-lg p-2 text-sm bg-white outline-none"
                        value={formData.opposingCounselEmail}
                        onChange={e => setFormData({ ...formData, opposingCounselEmail: e.target.value })}
                      />
                    </div>
                  </div>
                </div>
              )}

              <div className="flex justify-end gap-3 mt-6">
                <button type="button" onClick={closeMatterModal} className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg">{t('cancel')}</button>
                <button type="submit" disabled={matterSubmitting} className="px-4 py-2 text-sm font-bold text-white bg-slate-800 hover:bg-slate-900 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed">
                  {matterSubmitting ? t('saving') : t('save')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Matter Documents Drawer */}
      {showDocs && selectedMatter && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-3xl overflow-hidden animate-in fade-in zoom-in duration-200">
            <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">Matter Folder</h3>
                <p className="text-xs text-gray-500 mt-1">{selectedMatter.caseNumber} - {selectedMatter.name}</p>
              </div>
              <button onClick={() => setShowDocs(false)} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
            </div>
            <div className="p-6 max-h-[70vh] overflow-y-auto space-y-3">
              {documents.filter(d => d.matterId === selectedMatter.id).length === 0 ? (
                <div className="text-center text-gray-500 py-8">
                  <p className="text-sm font-medium">No documents found for this matter.</p>
                  <p className="text-xs text-gray-400 mt-1">You can upload files from the Documents tab.</p>
                </div>
              ) : (
                documents.filter(d => d.matterId === selectedMatter.id).map(doc => (
                  <div key={doc.id} className="border border-gray-200 rounded-lg p-3 flex justify-between items-center">
                    <div>
                      <p className="font-semibold text-slate-800 text-sm">{doc.name}</p>
                      <p className="text-xs text-gray-500">{doc.type.toUpperCase()} - {doc.size || 'Unknown'}</p>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="text-xs text-gray-400">{formatDate(doc.updatedAt)}</span>
                      {(doc.filePath || doc.content) && (
                        <>
                          <button onClick={() => handleOpenDoc(doc)} className="text-xs text-primary-600 hover:underline">Open</button>
                          <button onClick={() => handleDownloadDoc(doc)} className="text-xs text-gray-500 hover:underline">Download</button>
                        </>
                      )}
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}

      {/* Document Viewer Modal */}
      {viewingDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-4xl h-[90vh] flex flex-col">
            <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">{viewingDoc.name}</h3>
                <p className="text-xs text-gray-500 mt-1">{viewingDoc.size} - {formatDate(viewingDoc.updatedAt)}</p>
              </div>
              <button
                onClick={closeDocViewer}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-6 h-6" />
              </button>
            </div>
            <div className="flex-1 overflow-auto p-6 bg-white">
              {loadingContent ? (
                <div className="flex items-center justify-center h-full">
                  <div className="text-gray-400">Loading...</div>
                </div>
              ) : viewingDoc.type === 'pdf' ? (
                <iframe
                  src={docContent}
                  className="w-full h-full border-0"
                  title={viewingDoc.name}
                />
              ) : viewingDoc.type === 'txt' ? (
                <pre className="whitespace-pre-wrap font-mono text-sm text-slate-800 bg-gray-50 p-4 rounded-lg border border-gray-200 max-h-full overflow-auto">
                  {docContent}
                </pre>
              ) : viewingDoc.type === 'docx' ? (
                <div
                  className="prose max-w-none text-slate-800"
                  dangerouslySetInnerHTML={{ __html: docContent }}
                />
              ) : (
                <img
                  src={docContent}
                  alt={viewingDoc.name}
                  className="max-w-full h-auto mx-auto"
                />
              )}
            </div>
            <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex justify-end gap-3">
              <button
                onClick={() => handleDownloadDoc(viewingDoc)}
                className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900"
              >Download</button>
            </div>
          </div>
        </div>
      )}

      {/* New Client Modal */}
      {showNewClientModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
            <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50 sticky top-0">
              <h3 className="font-bold text-lg text-slate-800">Add New Client</h3>
              <button type="button" onClick={closeNewClientModal} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
            </div>
            <form onSubmit={handleInlineClientCreate} className="p-6 space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Full Name / Company Name *</label>
                  <input required type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.name} onChange={e => setNewClientData(prev => ({ ...prev, name: e.target.value }))} />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Email *</label>
                  <input required type="email" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.email} onChange={e => setNewClientData(prev => ({ ...prev, email: e.target.value }))} />
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Portal Password</label>
                <input type="password" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Optional: leave blank to keep portal access disabled" value={newClientData.password} onChange={e => setNewClientData(prev => ({ ...prev, password: e.target.value }))} />
                <p className="mt-1 text-xs text-gray-500">Optional. If you set one, {passwordRequirementsText.toLowerCase()}</p>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                  <input type="tel" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.phone} onChange={e => setNewClientData(prev => ({ ...prev, phone: e.target.value }))} />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Mobile Phone</label>
                  <input type="tel" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.mobile} onChange={e => setNewClientData(prev => ({ ...prev, mobile: e.target.value }))} />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
                  <select className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.type} onChange={e => setNewClientData(prev => ({ ...prev, type: e.target.value as 'Individual' | 'Corporate' }))}>
                    <option value="Individual">Individual</option>
                    <option value="Corporate">Corporate</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Company (if Corporate)</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.company} onChange={e => setNewClientData(prev => ({ ...prev, company: e.target.value }))} />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
                <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.address} onChange={e => setNewClientData(prev => ({ ...prev, address: e.target.value }))} />
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.city} onChange={e => setNewClientData(prev => ({ ...prev, city: e.target.value }))} />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.state} onChange={e => setNewClientData(prev => ({ ...prev, state: e.target.value }))} />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.zipCode} onChange={e => setNewClientData(prev => ({ ...prev, zipCode: e.target.value }))} />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Country</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.country} onChange={e => setNewClientData(prev => ({ ...prev, country: e.target.value }))} />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Tax ID / SSN</label>
                  <input type="text" className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.taxId} onChange={e => setNewClientData(prev => ({ ...prev, taxId: e.target.value }))} />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Notes</label>
                <textarea rows={3} className="w-full border border-gray-300 rounded-lg p-2.5 text-sm" value={newClientData.notes} onChange={e => setNewClientData(prev => ({ ...prev, notes: e.target.value }))} />
              </div>
              <div className="flex justify-end gap-3 pt-4">
                <button type="button" onClick={closeNewClientModal} className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg" disabled={inlineClientSubmitting}>Cancel</button>
                <button type="submit" className="px-4 py-2 text-sm font-bold text-white bg-slate-800 hover:bg-slate-900 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed" disabled={inlineClientSubmitting}>{inlineClientSubmitting ? 'Saving...' : 'Save'}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Client Selector Modal */}
      <ClientSelectorModal
        isOpen={showClientSelector}
        onClose={() => setShowClientSelector(false)}
        clients={clients}
        leads={leads}
        onSelect={(type, id, name) => {
          latestCreatedClientRef.current = type === 'client'
            ? clients.find((client) => client.id === id) ?? null
            : null;
          setFormData(prev => ({
            ...prev,
            partyId: id,
            partyType: type,
            relatedClientIds: prev.relatedClientIds.filter((clientId) => clientId !== id)
          }));
          setSelectedPartyName(name);
          setShowClientSelector(false);
        }}
      />
    </div>
  );
};

export default Matters;
