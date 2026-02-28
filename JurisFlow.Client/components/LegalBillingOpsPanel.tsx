import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../services/api';
import { Matter } from '../types';
import { toast } from './Toast';

type Props = { refreshKey?: string };
type MatterOption = Pick<Matter, 'id' | 'name' | 'caseNumber' | 'client'>;
type SplitRowDraft = {
  payorClientId: string;
  responsibilityType: 'primary' | 'split_percent' | 'split_amount' | 'third_party';
  percent: string;
  amountCap: string;
  priority: string;
  isPrimary: boolean;
  status: 'active' | 'inactive' | 'closed';
};
type LineDraft = {
  approvedAmount: string;
  status: string;
  reviewerNotes: string;
  thirdPartyPayorClientId: string;
  splitAllocations: SplitRowDraft[];
};

const fmtDate = (v?: string | null) => (v ? new Date(v).toLocaleString('en-US') : '-');
const fmtMoney = (v?: number | string | null) => {
  const n = Number(v ?? 0);
  return Number.isFinite(n) ? n.toLocaleString('en-US', { style: 'currency', currency: 'USD' }) : '$0.00';
};

const emptySplitRow = (): SplitRowDraft => ({
  payorClientId: '',
  responsibilityType: 'primary',
  percent: '',
  amountCap: '',
  priority: '',
  isPrimary: false,
  status: 'active'
});

const parseSplitAllocations = (json?: string | null): SplitRowDraft[] => {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    const rows = Array.isArray(parsed) ? parsed : Array.isArray(parsed?.allocations) ? parsed.allocations : [];
    return rows.map((r: any) => ({
      payorClientId: String(r?.payorClientId || r?.clientId || ''),
      responsibilityType: ['primary', 'split_percent', 'split_amount', 'third_party'].includes(String(r?.responsibilityType || r?.type || 'primary'))
        ? (String(r?.responsibilityType || r?.type || 'primary') as SplitRowDraft['responsibilityType'])
        : 'primary',
      percent: r?.percent == null ? '' : String(r.percent),
      amountCap: r?.amountCap == null ? '' : String(r.amountCap),
      priority: r?.priority == null ? '' : String(r.priority),
      isPrimary: !!r?.isPrimary,
      status: ['active', 'inactive', 'closed'].includes(String(r?.status || 'active'))
        ? (String(r?.status || 'active') as SplitRowDraft['status'])
        : 'active'
    }));
  } catch {
    return [];
  }
};

const sanitizeSplitRowsForApi = (rows: SplitRowDraft[]) =>
  rows
    .map(r => ({
      payorClientId: r.payorClientId.trim(),
      responsibilityType: r.responsibilityType,
      percent: r.percent.trim() === '' ? null : Number(r.percent),
      amountCap: r.amountCap.trim() === '' ? null : Number(r.amountCap),
      priority: r.priority.trim() === '' ? null : Number(r.priority),
      isPrimary: !!r.isPrimary,
      status: r.status
    }))
    .filter(r => r.payorClientId);

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded border border-gray-200 bg-white p-2">
    <div className="text-[11px] text-gray-500">{label}</div>
    <div className="text-sm font-semibold text-slate-800">{value}</div>
  </div>
);

const LegalBillingOpsPanel: React.FC<Props> = ({ refreshKey }) => {
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [matters, setMatters] = useState<MatterOption[]>([]);
  const [rateCards, setRateCards] = useState<any[]>([]);
  const [prebills, setPrebills] = useState<any[]>([]);
  const [prebillDetail, setPrebillDetail] = useState<any | null>(null);
  const [trustRec, setTrustRec] = useState<any | null>(null);
  const [reviewItems, setReviewItems] = useState<any[]>([]);
  const [ledesPreview, setLedesPreview] = useState<any | null>(null);
  const [policy, setPolicy] = useState<any | null>(null);
  const [historicalKpis, setHistoricalKpis] = useState<any | null>(null);
  const [payorAging, setPayorAging] = useState<any | null>(null);
  const [ebillingTransmissions, setEbillingTransmissions] = useState<any[]>([]);
  const [ebillingEvents, setEbillingEvents] = useState<any[]>([]);

  const [selectedMatterId, setSelectedMatterId] = useState('');
  const [selectedPrebillId, setSelectedPrebillId] = useState('');
  const [selectedRateCardId, setSelectedRateCardId] = useState('');
  const [decisionNotes, setDecisionNotes] = useState<Record<string, string>>({});
  const [prebillNotes, setPrebillNotes] = useState('');
  const [ebillingProviderFilter, setEbillingProviderFilter] = useState('billing-engine');
  const [ebillingRepairNotes, setEbillingRepairNotes] = useState<Record<string, string>>({});

  const [policyDraft, setPolicyDraft] = useState({
    arrangementType: 'hourly',
    billingCycle: 'monthly',
    currency: 'USD',
    ebillingFormat: 'none',
    ebillingStatus: 'disabled',
    enforceUtbmsCodes: false,
    enforceTrustOperatingSplit: true,
    requirePrebillApproval: true,
    splitAllocations: [] as SplitRowDraft[]
  });
  const [prebillDraft, setPrebillDraft] = useState(() => {
    const now = new Date();
    const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
    const end = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 0));
    return { periodStart: start.toISOString().slice(0, 10), periodEnd: end.toISOString().slice(0, 10) };
  });
  const [lineDrafts, setLineDrafts] = useState<Record<string, LineDraft>>({});

  const selectedMatter = useMemo(() => matters.find(m => m.id === selectedMatterId) || null, [matters, selectedMatterId]);

  const metrics = useMemo(() => {
    const lines = Array.isArray(prebillDetail?.lines) ? prebillDetail.lines : [];
    const adjusted = lines.filter((l: any) =>
      Number(l.approvedAmount ?? 0) !== Number(l.proposedAmount ?? 0) || (l.status || '').toLowerCase() === 'excluded'
    ).length;
    const trustAccounts = Array.isArray(trustRec?.accounts) ? trustRec.accounts : [];
    const trustMismatch = trustAccounts.filter((a: any) =>
      Number(a.bankVsTrustLedgerDiff || 0) !== 0 || Number(a.bankVsClientLedgerDiff || 0) !== 0 || Number(a.clientLedgerVsTrustLedgerDiff || 0) !== 0
    ).length;
    return {
      prebills: prebills.length,
      reviewPending: reviewItems.filter(i => (i.status || '').toLowerCase() === 'pending').length,
      trustMismatch,
      lineAdjustRate: lines.length ? `${((adjusted / lines.length) * 100).toFixed(1)}%` : '-'
    };
  }, [prebills, reviewItems, trustRec, prebillDetail]);

  const collectionSummary = historicalKpis?.collectionCycle?.summary;
  const collectionPayorSegments = Array.isArray(collectionSummary?.payorSegments) ? collectionSummary.payorSegments : [];
  const prebillAdjustmentSummary = historicalKpis?.prebillAdjustment?.summary;
  const ebillingSummary = historicalKpis?.ebillingRejection?.summary;
  const trustHistorySummary = historicalKpis?.trustReconciliationHistory?.summary;
  const payorAgingBuckets = Array.isArray(payorAging?.buckets) ? payorAging.buckets : [];
  const payorAgingSegments = Array.isArray(payorAging?.payorSegments) ? payorAging.payorSegments : [];

  const loadBase = async () => {
    setLoading(true);
    try {
      const [m, rc, rv, tr, kpi, aging, txs, evs] = await Promise.all([
        api.getMatters() as Promise<MatterOption[] | null>,
        api.legalBilling.getRateCards({ limit: 100 }),
        api.integrationsOps.getReviewQueue({ providerKey: 'billing-engine', limit: 100 }),
        api.legalBilling.getTrustReconciliation(),
        api.integrationsOps.getKpiAnalytics({ days: 90, bucket: 'week' }),
        api.legalBilling.getPayorAging({ limit: 40 }),
        api.legalBilling.getEbillingTransmissions({ providerKey: ebillingProviderFilter || undefined, limit: 40 }),
        api.legalBilling.getEbillingEvents({ providerKey: ebillingProviderFilter || undefined, limit: 80 })
      ]);
      const matterRows = Array.isArray(m) ? m : [];
      setMatters(matterRows);
      setRateCards(Array.isArray(rc) ? rc : []);
      setReviewItems(Array.isArray(rv) ? rv : []);
      setTrustRec(tr || null);
      setHistoricalKpis(kpi || null);
      setPayorAging(aging || null);
      setEbillingTransmissions(Array.isArray(txs) ? txs : []);
      setEbillingEvents(Array.isArray(evs) ? evs : []);
      if (!selectedMatterId && matterRows[0]) setSelectedMatterId(matterRows[0].id);
      if (!selectedRateCardId && Array.isArray(rc) && rc[0]) setSelectedRateCardId(rc[0].id);
    } catch (e) {
      console.error(e);
      toast.error('Failed to load legal billing ops.');
    } finally {
      setLoading(false);
    }
  };

  const loadMatterData = async (matterId: string) => {
    if (!matterId) return;
    try {
      const [p, batches] = await Promise.all([
        api.legalBilling.getMatterPolicy(matterId).catch(() => null),
        api.legalBilling.getPrebills({ matterId, limit: 100 })
      ]);
      setPolicy(p || null);
      if (p) {
        setPolicyDraft(prev => ({
          ...prev,
          arrangementType: p.arrangementType || 'hourly',
          billingCycle: p.billingCycle || 'monthly',
          currency: p.currency || 'USD',
          ebillingFormat: p.ebillingFormat || 'none',
          ebillingStatus: p.ebillingStatus || 'disabled',
          enforceUtbmsCodes: !!p.enforceUtbmsCodes,
          enforceTrustOperatingSplit: p.enforceTrustOperatingSplit !== false,
          requirePrebillApproval: p.requirePrebillApproval !== false,
          splitAllocations: parseSplitAllocations(p.splitBillingJson)
        }));
      } else {
        setPolicyDraft(prev => ({ ...prev, splitAllocations: [] }));
      }
      const rows = Array.isArray(batches) ? batches : [];
      setPrebills(rows);
      if (!rows.some(r => r.id === selectedPrebillId)) setSelectedPrebillId(rows[0]?.id || '');
    } catch (e) {
      console.error(e);
      toast.error('Failed to load matter billing data.');
    }
  };

  const loadPrebill = async (prebillId: string) => {
    if (!prebillId) {
      setPrebillDetail(null);
      setLedesPreview(null);
      return;
    }
    try {
      const detail = await api.legalBilling.getPrebill(prebillId);
      setPrebillDetail(detail || null);
      const next: Record<string, LineDraft> = {};
      (detail?.lines || []).forEach((l: any) => {
        next[l.id] = {
          approvedAmount: String(l.approvedAmount ?? l.proposedAmount ?? 0),
          status: l.status || 'draft',
          reviewerNotes: l.reviewerNotes || '',
          thirdPartyPayorClientId: l.thirdPartyPayorClientId || '',
          splitAllocations: parseSplitAllocations(l.splitAllocationJson)
        };
      });
      setLineDrafts(next);
    } catch (e) {
      console.error(e);
      toast.error('Failed to load prebill.');
    }
  };

  useEffect(() => { void loadBase(); }, [refreshKey, ebillingProviderFilter]);
  useEffect(() => { if (selectedMatterId) void loadMatterData(selectedMatterId); }, [selectedMatterId]);
  useEffect(() => { if (selectedPrebillId) void loadPrebill(selectedPrebillId); else setPrebillDetail(null); }, [selectedPrebillId]);

  const savePolicy = async () => {
    if (!selectedMatter) return toast.error('Select a matter.');
    const clientId = (selectedMatter as any)?.client?.id || (selectedMatter as any)?.clientId;
    if (!clientId) return toast.error('Matter client id missing.');
    setBusy('policy');
    try {
      const saved = await api.legalBilling.upsertMatterPolicy({
        id: policy?.id,
        matterId: selectedMatter.id,
        clientId,
        rateCardId: selectedRateCardId || null,
        arrangementType: policyDraft.arrangementType,
        billingCycle: policyDraft.billingCycle,
        currency: policyDraft.currency,
        ebillingFormat: policyDraft.ebillingFormat,
        ebillingStatus: policyDraft.ebillingStatus,
        enforceUtbmsCodes: policyDraft.enforceUtbmsCodes,
        enforceTrustOperatingSplit: policyDraft.enforceTrustOperatingSplit,
        requirePrebillApproval: policyDraft.requirePrebillApproval,
        splitAllocations: sanitizeSplitRowsForApi(policyDraft.splitAllocations),
        status: 'active'
      });
      setPolicy(saved || null);
      toast.success('Billing policy saved.');
      await loadMatterData(selectedMatter.id);
    } catch (e) {
      console.error(e);
      toast.error('Failed to save policy.');
    } finally {
      setBusy(null);
    }
  };

  const generatePrebill = async () => {
    if (!selectedMatterId) return toast.error('Select a matter.');
    setBusy('generate');
    try {
      const result = await api.legalBilling.generatePrebill({
        matterId: selectedMatterId,
        periodStart: new Date(`${prebillDraft.periodStart}T00:00:00Z`).toISOString(),
        periodEnd: new Date(`${prebillDraft.periodEnd}T23:59:59Z`).toISOString()
      });
      toast.success(`Prebill generated${Array.isArray((result as any)?.warnings) ? ` (${(result as any).warnings.length} warnings)` : ''}.`);
      await loadMatterData(selectedMatterId);
      if ((result as any)?.batch?.id) setSelectedPrebillId((result as any).batch.id);
    } catch (e) {
      console.error(e);
      toast.error('Failed to generate prebill.');
    } finally {
      setBusy(null);
    }
  };

  const transitionPrebill = async (action: 'submit' | 'approve' | 'reject' | 'finalize') => {
    if (!selectedPrebillId) return toast.error('Select a prebill.');
    setBusy(`prebill:${action}`);
    try {
      if (action === 'submit') await api.legalBilling.submitPrebillForReview(selectedPrebillId, prebillNotes);
      if (action === 'approve') await api.legalBilling.approvePrebill(selectedPrebillId, prebillNotes);
      if (action === 'reject') await api.legalBilling.rejectPrebill(selectedPrebillId, prebillNotes);
      if (action === 'finalize') await api.legalBilling.finalizePrebill(selectedPrebillId, { markAsSent: false, notes: prebillNotes || null });
      toast.success(`Prebill ${action} complete.`);
      if (selectedMatterId) await loadMatterData(selectedMatterId);
      await loadPrebill(selectedPrebillId);
      await loadBase();
    } catch (e) {
      console.error(e);
      toast.error(`Prebill ${action} failed.`);
    } finally {
      setBusy(null);
    }
  };

  const adjustLine = async (lineId: string) => {
    const d = lineDrafts[lineId];
    if (!d) return;
    setBusy(`line:${lineId}`);
    try {
      await api.legalBilling.adjustPrebillLine(lineId, {
        approvedAmount: Number(d.approvedAmount || 0),
        status: d.status,
        reviewerNotes: d.reviewerNotes || null,
        thirdPartyPayorClientId: d.thirdPartyPayorClientId || null,
        splitAllocations: sanitizeSplitRowsForApi(d.splitAllocations)
      });
      toast.success('Line adjusted.');
      if (selectedPrebillId) await loadPrebill(selectedPrebillId);
    } catch (e) {
      console.error(e);
      toast.error('Line adjustment failed.');
    } finally {
      setBusy(null);
    }
  };

  const loadLedes = async () => {
    if (!selectedPrebillId) return toast.error('Select a prebill.');
    setBusy('ledes');
    try {
      setLedesPreview(await api.legalBilling.getLedesPreview(selectedPrebillId));
      toast.success('LEDES preview loaded.');
    } catch (e) {
      console.error(e);
      toast.error('Failed to load LEDES preview.');
    } finally {
      setBusy(null);
    }
  };

  const decideReview = async (id: string, decision: 'approve' | 'reject' | 'retry') => {
    setBusy(`review:${id}`);
    try {
      await api.integrationsOps.decideReviewItem(id, { decision, notes: decisionNotes[id] || null, status: 'resolved' } as any);
      toast.success(`Review ${decision}d.`);
      const rows = await api.integrationsOps.getReviewQueue({ providerKey: 'billing-engine', limit: 100 });
      setReviewItems(Array.isArray(rows) ? rows : []);
    } catch (e) {
      console.error(e);
      toast.error('Failed to update review item.');
    } finally {
      setBusy(null);
    }
  };

  const repairTransmission = async (transmissionId: string) => {
    setBusy(`ebilling-repair:${transmissionId}`);
    try {
      await api.legalBilling.repairEbillingTransmission(transmissionId, {
        notes: ebillingRepairNotes[transmissionId] || null,
        retryable: true
      });
      toast.success('E-billing transmission moved to repair/retry queue.');
      const [txs, evs] = await Promise.all([
        api.legalBilling.getEbillingTransmissions({ providerKey: ebillingProviderFilter || undefined, limit: 40 }),
        api.legalBilling.getEbillingEvents({ providerKey: ebillingProviderFilter || undefined, limit: 80 })
      ]);
      setEbillingTransmissions(Array.isArray(txs) ? txs : []);
      setEbillingEvents(Array.isArray(evs) ? evs : []);
    } catch (e) {
      console.error(e);
      toast.error('Failed to request e-billing repair.');
    } finally {
      setBusy(null);
    }
  };

  const updatePolicySplitRow = (index: number, patch: Partial<SplitRowDraft>) => {
    setPolicyDraft(prev => ({
      ...prev,
      splitAllocations: prev.splitAllocations.map((row, i) => (i === index ? { ...row, ...patch } : row))
    }));
  };

  const addPolicySplitRow = () => {
    setPolicyDraft(prev => ({ ...prev, splitAllocations: [...prev.splitAllocations, emptySplitRow()] }));
  };

  const removePolicySplitRow = (index: number) => {
    setPolicyDraft(prev => ({ ...prev, splitAllocations: prev.splitAllocations.filter((_, i) => i !== index) }));
  };

  const updateLineSplitRow = (lineId: string, index: number, patch: Partial<SplitRowDraft>) => {
    setLineDrafts(prev => {
      const current = prev[lineId];
      if (!current) return prev;
      return {
        ...prev,
        [lineId]: {
          ...current,
          splitAllocations: current.splitAllocations.map((row, i) => (i === index ? { ...row, ...patch } : row))
        }
      };
    });
  };

  const addLineSplitRow = (lineId: string) => {
    setLineDrafts(prev => {
      const current = prev[lineId];
      if (!current) return prev;
      return {
        ...prev,
        [lineId]: {
          ...current,
          splitAllocations: [...current.splitAllocations, emptySplitRow()]
        }
      };
    });
  };

  const removeLineSplitRow = (lineId: string, index: number) => {
    setLineDrafts(prev => {
      const current = prev[lineId];
      if (!current) return prev;
      return {
        ...prev,
        [lineId]: {
          ...current,
          splitAllocations: current.splitAllocations.filter((_, i) => i !== index)
        }
      };
    });
  };

  return (
    <div className="mt-6 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="text-base font-bold text-slate-900">Legal Billing Ops</h3>
          <p className="text-xs text-gray-500">Billing policy, prebill review workflow, trust reconciliation, and billing review queue.</p>
        </div>
        <button onClick={loadBase} disabled={loading} className="px-3 py-1.5 text-xs font-semibold rounded border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-50">{loading ? 'Refreshing...' : 'Refresh'}</button>
      </div>

      <div className="mt-4 grid gap-2 md:grid-cols-4">
        <Metric label="Prebills" value={String(metrics.prebills)} />
        <Metric label="Billing Review Pending" value={String(metrics.reviewPending)} />
        <Metric label="Trust Mismatch Accts" value={String(metrics.trustMismatch)} />
        <Metric label="Prebill Line Adjustment Rate" value={metrics.lineAdjustRate} />
      </div>

      <div className="mt-3 grid gap-2 md:grid-cols-5">
        <Metric label="Hist Prebill Adj %" value={prebillAdjustmentSummary ? `${Number(prebillAdjustmentSummary.adjustmentRatePct || 0).toFixed(2)}%` : '-'} />
        <Metric label="Avg Collection Days" value={collectionSummary ? Number(collectionSummary.avgFirstPaymentDays || 0).toFixed(2) : '-'} />
        <Metric label="P90 Collection Days" value={collectionSummary ? Number(collectionSummary.p90FirstPaymentDays || 0).toFixed(2) : '-'} />
        <Metric
          label={historicalKpis?.ebillingRejection?.dataQuality === 'exact_provider_event' ? 'E-billing Reject %' : 'E-billing Reject % (proxy)'}
          value={ebillingSummary ? `${Number(ebillingSummary.rejectionRatePct || 0).toFixed(2)}%` : 'n/a'}
        />
        <Metric label="Trust Snapshots" value={trustHistorySummary ? String(trustHistorySummary.snapshots || 0) : '0'} />
      </div>

      {collectionPayorSegments.length > 0 && (
        <div className="mt-2 rounded border border-slate-200 bg-slate-50 p-2">
          <div className="text-[11px] font-semibold uppercase tracking-wide text-slate-600">Collection Cycle by Payor Segment</div>
          <div className="mt-1 flex flex-wrap gap-2 text-xs text-slate-700">
            {collectionPayorSegments.map((s: any) => (
              <div key={String(s.segment)} className="rounded border border-slate-300 bg-white px-2 py-1">
                <span className="font-semibold">{String(s.segment)}</span>
                <span className="ml-2">n={Number(s.invoiceCount || 0)}</span>
                <span className="ml-2">avg first {Number(s.avgFirstPaymentDays || 0).toFixed(1)}d</span>
                <span className="ml-2">p90 {Number(s.p90FirstPaymentDays || 0).toFixed(1)}d</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {payorAging && (
        <div className="mt-2 rounded border border-indigo-200 bg-indigo-50/40 p-2">
          <div className="text-[11px] font-semibold uppercase tracking-wide text-indigo-700">Collections Payor Aging</div>
          <div className="mt-2 grid gap-2 md:grid-cols-5">
            {payorAgingBuckets.length === 0 ? (
              <div className="col-span-full text-xs text-slate-600">No aging rows.</div>
            ) : payorAgingBuckets.map((b: any) => (
              <div key={String(b.bucketKey)} className="rounded border border-indigo-200 bg-white px-2 py-2 text-xs">
                <div className="font-semibold text-slate-800">{String(b.bucketLabel || b.bucketKey)}</div>
                <div className="mt-1 text-slate-700">{fmtMoney(b.totalOutstanding)}</div>
                <div className="text-[11px] text-slate-500">rows {Number(b.rowCount || 0)} • payors {Number(b.payorCount || 0)}</div>
              </div>
            ))}
          </div>
          {payorAgingSegments.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-2 text-xs">
              {payorAgingSegments.map((s: any) => (
                <div key={String(s.segment)} className="rounded border border-indigo-200 bg-white px-2 py-1 text-slate-700">
                  <span className="font-semibold">{String(s.segment)}</span>
                  <span className="ml-2">{fmtMoney(s.totalOutstanding)}</span>
                  <span className="ml-2 text-slate-500">rows {Number(s.rowCount || 0)}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {historicalKpis?.ebillingRejection?.dataQuality === 'partial' && (
        <div className="mt-2 rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">
          E-billing rejection KPI is currently a proxy from billing review items (LEDES/UTBMS markers). Exact provider-grade rejection tracking requires persisted e-billing submission/result events.
        </div>
      )}
      {historicalKpis?.ebillingRejection?.dataQuality === 'partial_engine_event' && (
        <div className="mt-2 rounded border border-blue-200 bg-blue-50 p-2 text-xs text-blue-800">
          E-billing events are being persisted from billing-engine precheck/finalize flow. Provider adapter result events (accept/reject/error) have not been observed yet, so KPI is not yet provider-grade exact.
        </div>
      )}
      {historicalKpis?.ebillingRejection?.dataQuality === 'exact_provider_event' && (
        <div className="mt-2 rounded border border-emerald-200 bg-emerald-50 p-2 text-xs text-emerald-800">
          E-billing rejection KPI is using persisted provider-grade submission/result events.
        </div>
      )}

      <div className="mt-4 grid gap-3 lg:grid-cols-3">
        <select className="rounded border border-gray-300 p-2 text-sm bg-white" value={selectedMatterId} onChange={e => setSelectedMatterId(e.target.value)}>
          <option value="">Select matter...</option>
          {matters.map(m => <option key={m.id} value={m.id}>{m.caseNumber || m.id} | {m.name}</option>)}
        </select>
        <select className="rounded border border-gray-300 p-2 text-sm bg-white" value={selectedRateCardId} onChange={e => setSelectedRateCardId(e.target.value)}>
          <option value="">Select rate card...</option>
          {rateCards.map(rc => <option key={rc.id} value={rc.id}>{rc.name} | {rc.scope || 'firm'}</option>)}
        </select>
        <select className="rounded border border-gray-300 p-2 text-sm bg-white" value={selectedPrebillId} onChange={e => setSelectedPrebillId(e.target.value)}>
          <option value="">Select prebill...</option>
          {prebills.map(p => <option key={p.id} value={p.id}>{(p.status || 'draft').toUpperCase()} | {fmtDate(p.generatedAt)} | {fmtMoney(p.total)}</option>)}
        </select>
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-2">
        <div className="space-y-4">
          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 text-sm font-semibold">Billing Policy + Prebill Controls</div>
            <div className="grid gap-2 md:grid-cols-2">
              <select className="rounded border border-gray-300 p-2 text-sm" value={policyDraft.arrangementType} onChange={e => setPolicyDraft(p => ({ ...p, arrangementType: e.target.value }))}><option value="hourly">hourly</option><option value="fixed">fixed</option><option value="contingency">contingency</option><option value="hybrid">hybrid</option></select>
              <select className="rounded border border-gray-300 p-2 text-sm" value={policyDraft.billingCycle} onChange={e => setPolicyDraft(p => ({ ...p, billingCycle: e.target.value }))}><option value="monthly">monthly</option><option value="milestone">milestone</option><option value="ad_hoc">ad_hoc</option></select>
              <input className="rounded border border-gray-300 p-2 text-sm" value={policyDraft.currency} onChange={e => setPolicyDraft(p => ({ ...p, currency: e.target.value.toUpperCase() }))} placeholder="USD" />
              <select className="rounded border border-gray-300 p-2 text-sm" value={policyDraft.ebillingFormat} onChange={e => setPolicyDraft(p => ({ ...p, ebillingFormat: e.target.value }))}><option value="none">none</option><option value="ledes98b">ledes98b</option><option value="ledes1998bi">ledes1998bi</option></select>
            </div>
            <div className="mt-2 flex flex-wrap gap-3 text-xs">
              <label className="inline-flex items-center gap-2"><input type="checkbox" checked={policyDraft.requirePrebillApproval} onChange={e => setPolicyDraft(p => ({ ...p, requirePrebillApproval: e.target.checked }))} /> Require prebill approval</label>
              <label className="inline-flex items-center gap-2"><input type="checkbox" checked={policyDraft.enforceUtbmsCodes} onChange={e => setPolicyDraft(p => ({ ...p, enforceUtbmsCodes: e.target.checked }))} /> Enforce UTBMS</label>
              <label className="inline-flex items-center gap-2"><input type="checkbox" checked={policyDraft.enforceTrustOperatingSplit} onChange={e => setPolicyDraft(p => ({ ...p, enforceTrustOperatingSplit: e.target.checked }))} /> Enforce IOLTA split</label>
            </div>
            <div className="mt-2 flex justify-end">
              <button onClick={savePolicy} disabled={busy === 'policy'} className="px-3 py-1.5 text-xs font-semibold rounded border border-blue-300 bg-blue-50 text-blue-800 disabled:opacity-50">{busy === 'policy' ? 'Saving...' : 'Save Policy'}</button>
            </div>
            <div className="mt-3 rounded border border-gray-200 p-2">
              <div className="mb-2 flex items-center justify-between gap-2">
                <div className="text-xs font-semibold text-slate-700">Default Split Billing Rules (typed)</div>
                <button type="button" onClick={addPolicySplitRow} className="px-2 py-1 text-[11px] font-semibold rounded border border-gray-300 bg-white">Add Split Row</button>
              </div>
              {policyDraft.splitAllocations.length === 0 ? (
                <p className="text-[11px] text-gray-500">No default split rows. Finalize falls back to primary payor (`Invoice.ClientId`).</p>
              ) : (
                <div className="space-y-2">
                  {policyDraft.splitAllocations.map((row, idx) => (
                    <div key={`policy-split-${idx}`} className="grid gap-2 rounded border border-gray-100 p-2 md:grid-cols-[1.1fr,140px,100px,100px,90px,70px,auto]">
                      <input className="rounded border border-gray-300 px-2 py-1 text-xs" placeholder="PayorClientId" value={row.payorClientId} onChange={e => updatePolicySplitRow(idx, { payorClientId: e.target.value })} />
                      <select className="rounded border border-gray-300 px-2 py-1 text-xs" value={row.responsibilityType} onChange={e => updatePolicySplitRow(idx, { responsibilityType: e.target.value as SplitRowDraft['responsibilityType'] })}>
                        <option value="primary">primary</option>
                        <option value="split_percent">split_percent</option>
                        <option value="split_amount">split_amount</option>
                        <option value="third_party">third_party</option>
                      </select>
                      <input className="rounded border border-gray-300 px-2 py-1 text-xs" placeholder="% (opt)" value={row.percent} onChange={e => updatePolicySplitRow(idx, { percent: e.target.value })} />
                      <input className="rounded border border-gray-300 px-2 py-1 text-xs" placeholder="Cap (opt)" value={row.amountCap} onChange={e => updatePolicySplitRow(idx, { amountCap: e.target.value })} />
                      <input className="rounded border border-gray-300 px-2 py-1 text-xs" placeholder="Priority" value={row.priority} onChange={e => updatePolicySplitRow(idx, { priority: e.target.value })} />
                      <label className="inline-flex items-center gap-1 text-[11px]"><input type="checkbox" checked={row.isPrimary} onChange={e => updatePolicySplitRow(idx, { isPrimary: e.target.checked })} />Primary</label>
                      <button type="button" onClick={() => removePolicySplitRow(idx)} className="px-2 py-1 text-[11px] font-semibold rounded border border-rose-300 bg-rose-50 text-rose-800">Remove</button>
                    </div>
                  ))}
                </div>
              )}
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-3">
              <input type="date" className="rounded border border-gray-300 p-2 text-sm" value={prebillDraft.periodStart} onChange={e => setPrebillDraft(p => ({ ...p, periodStart: e.target.value }))} />
              <input type="date" className="rounded border border-gray-300 p-2 text-sm" value={prebillDraft.periodEnd} onChange={e => setPrebillDraft(p => ({ ...p, periodEnd: e.target.value }))} />
              <button onClick={generatePrebill} disabled={busy === 'generate'} className="px-3 py-2 text-xs font-semibold rounded border border-indigo-300 bg-indigo-50 text-indigo-800 disabled:opacity-50">{busy === 'generate' ? 'Generating...' : 'Generate Prebill'}</button>
            </div>
          </div>

          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 text-sm font-semibold">Prebill Review</div>
            <textarea className="w-full rounded border border-gray-300 p-2 text-xs" rows={2} placeholder="Review/finalize notes" value={prebillNotes} onChange={e => setPrebillNotes(e.target.value)} />
            <div className="mt-2 flex flex-wrap gap-2">
              <button onClick={() => transitionPrebill('submit')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-amber-300 bg-amber-50 text-amber-800 disabled:opacity-50">Submit Review</button>
              <button onClick={() => transitionPrebill('approve')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-green-300 bg-green-50 text-green-800 disabled:opacity-50">Approve</button>
              <button onClick={() => transitionPrebill('reject')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-rose-300 bg-rose-50 text-rose-800 disabled:opacity-50">Reject</button>
              <button onClick={() => transitionPrebill('finalize')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-slate-300 bg-slate-100 text-slate-800 disabled:opacity-50">Finalize</button>
              <button onClick={loadLedes} disabled={busy === 'ledes'} className="px-2 py-1 text-xs font-semibold rounded border border-blue-300 bg-blue-50 text-blue-800 disabled:opacity-50">{busy === 'ledes' ? 'Loading...' : 'LEDES'}</button>
            </div>
            <div className="mt-3 max-h-72 overflow-auto rounded border border-gray-200">
              {!prebillDetail ? <p className="p-3 text-sm text-gray-500">Select a prebill.</p> : (prebillDetail.lines || []).length === 0 ? <p className="p-3 text-sm text-gray-500">No lines.</p> : (prebillDetail.lines || []).slice(0, 60).map((line: any) => {
                const d = lineDrafts[line.id] || { approvedAmount: String(line.approvedAmount ?? line.proposedAmount ?? 0), status: line.status || 'draft', reviewerNotes: line.reviewerNotes || '', thirdPartyPayorClientId: line.thirdPartyPayorClientId || '', splitAllocations: parseSplitAllocations(line.splitAllocationJson) };
                return (
                  <div key={line.id} className="border-b border-gray-100 p-2 text-xs">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="font-medium text-slate-800">{line.lineType} | {line.description || '(no description)'}</div>
                      <div className="text-gray-600">{fmtMoney(line.proposedAmount)} {'->'} {fmtMoney(line.approvedAmount)}</div>
                    </div>
                    <div className="mt-1 grid gap-2 md:grid-cols-[110px,120px,1fr,auto]">
                      <input type="number" className="rounded border border-gray-300 px-2 py-1" value={d.approvedAmount} onChange={e => setLineDrafts(prev => ({ ...prev, [line.id]: { ...d, approvedAmount: e.target.value } }))} />
                      <select className="rounded border border-gray-300 px-2 py-1" value={d.status} onChange={e => setLineDrafts(prev => ({ ...prev, [line.id]: { ...d, status: e.target.value } }))}><option value="draft">draft</option><option value="reviewed">reviewed</option><option value="approved">approved</option><option value="excluded">excluded</option></select>
                      <input className="rounded border border-gray-300 px-2 py-1" value={d.reviewerNotes} onChange={e => setLineDrafts(prev => ({ ...prev, [line.id]: { ...d, reviewerNotes: e.target.value } }))} placeholder="Reviewer notes" />
                      <button onClick={() => adjustLine(line.id)} disabled={busy === `line:${line.id}`} className="px-2 py-1 font-semibold rounded border border-gray-300 bg-white disabled:opacity-50">{busy === `line:${line.id}` ? 'Saving' : 'Apply'}</button>
                    </div>
                    <div className="mt-2 rounded border border-gray-100 p-2">
                      <div className="mb-1 flex items-center justify-between gap-2">
                        <div className="text-[11px] font-semibold text-slate-700">Line Split Allocation</div>
                        <button type="button" onClick={() => addLineSplitRow(line.id)} className="px-2 py-1 text-[11px] font-semibold rounded border border-gray-300 bg-white">Add Row</button>
                      </div>
                      <div className="mb-2">
                        <input className="w-full rounded border border-gray-300 px-2 py-1 text-[11px]" placeholder="Third-party payor client id (optional)" value={d.thirdPartyPayorClientId} onChange={e => setLineDrafts(prev => ({ ...prev, [line.id]: { ...d, thirdPartyPayorClientId: e.target.value } }))} />
                      </div>
                      {d.splitAllocations.length === 0 ? (
                        <p className="text-[11px] text-gray-500">No line split rows. Policy/default primary payor will be used.</p>
                      ) : (
                        <div className="space-y-2">
                          {d.splitAllocations.map((row, idx) => (
                            <div key={`${line.id}-split-${idx}`} className="grid gap-2 md:grid-cols-[1.1fr,130px,88px,88px,72px,60px,auto]">
                              <input className="rounded border border-gray-300 px-2 py-1 text-[11px]" placeholder="PayorClientId" value={row.payorClientId} onChange={e => updateLineSplitRow(line.id, idx, { payorClientId: e.target.value })} />
                              <select className="rounded border border-gray-300 px-2 py-1 text-[11px]" value={row.responsibilityType} onChange={e => updateLineSplitRow(line.id, idx, { responsibilityType: e.target.value as SplitRowDraft['responsibilityType'] })}>
                                <option value="primary">primary</option>
                                <option value="split_percent">percent</option>
                                <option value="split_amount">amount</option>
                                <option value="third_party">third_party</option>
                              </select>
                              <input className="rounded border border-gray-300 px-2 py-1 text-[11px]" placeholder="%" value={row.percent} onChange={e => updateLineSplitRow(line.id, idx, { percent: e.target.value })} />
                              <input className="rounded border border-gray-300 px-2 py-1 text-[11px]" placeholder="Cap" value={row.amountCap} onChange={e => updateLineSplitRow(line.id, idx, { amountCap: e.target.value })} />
                              <input className="rounded border border-gray-300 px-2 py-1 text-[11px]" placeholder="Pri" value={row.priority} onChange={e => updateLineSplitRow(line.id, idx, { priority: e.target.value })} />
                              <label className="inline-flex items-center gap-1 text-[10px]"><input type="checkbox" checked={row.isPrimary} onChange={e => updateLineSplitRow(line.id, idx, { isPrimary: e.target.checked })} />P</label>
                              <button type="button" onClick={() => removeLineSplitRow(line.id, idx)} className="px-2 py-1 text-[11px] font-semibold rounded border border-rose-300 bg-rose-50 text-rose-800">X</button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
            {ledesPreview && <pre className="mt-2 max-h-36 overflow-auto rounded bg-slate-900 p-2 text-[11px] text-slate-100 whitespace-pre-wrap">{ledesPreview.previewText || ''}</pre>}
          </div>
        </div>

        <div className="space-y-4">
          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 flex items-center justify-between gap-2">
              <div className="text-sm font-semibold">Trust 3-Way Reconciliation</div>
              <button onClick={loadBase} className="px-2 py-1 text-xs font-semibold rounded border border-gray-300 bg-white">Refresh</button>
            </div>
            <div className="grid gap-2 md:grid-cols-3">
              <Metric label="Accounts" value={String((trustRec?.accounts || []).length)} />
              <Metric label="Mismatch" value={String(metrics.trustMismatch)} />
              <Metric label="Bank vs Trust Diff" value={fmtMoney(trustRec?.totals?.bankVsTrustLedgerDiff || 0)} />
            </div>
            <div className="mt-2 max-h-48 overflow-auto">
              {(trustRec?.accounts || []).length === 0 ? <p className="text-xs text-gray-500">No rows.</p> : (trustRec.accounts || []).map((a: any) => (
                <div key={a.trustAccountId} className="border-t border-gray-100 py-2 text-xs">
                  <div className="font-medium text-slate-700">{a.trustAccountName || a.trustAccountId}</div>
                  <div className="text-gray-500">Bank {fmtMoney(a.bankBalance)} | Billing Trust {fmtMoney(a.billingTrustLedgerTotal)} | Diff {fmtMoney(a.bankVsTrustLedgerDiff)}</div>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 text-sm font-semibold">Billing Review Queue (billing-engine)</div>
            <div className="max-h-80 overflow-auto">
              {reviewItems.length === 0 ? <p className="text-xs text-gray-500">No billing review items.</p> : reviewItems.slice(0, 60).map(item => (
                <div key={item.id} className="border-t border-gray-100 py-2 text-xs">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="font-medium text-slate-700">{item.itemType || 'review_item'} | {(item.status || '').toUpperCase()}</div>
                    <div className="text-gray-500">{fmtDate(item.createdAt)}</div>
                  </div>
                  <div className="mt-1 text-gray-600">{item.title || item.summary || item.description || '-'}</div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    <input className="min-w-[220px] flex-1 rounded border border-gray-300 px-2 py-1" placeholder="Decision notes" value={decisionNotes[item.id] || ''} onChange={e => setDecisionNotes(prev => ({ ...prev, [item.id]: e.target.value }))} />
                    <button onClick={() => decideReview(item.id, 'approve')} disabled={!!busy} className="px-2 py-1 font-semibold rounded border border-green-300 bg-green-50 text-green-800 disabled:opacity-50">Approve</button>
                    <button onClick={() => decideReview(item.id, 'retry')} disabled={!!busy} className="px-2 py-1 font-semibold rounded border border-blue-300 bg-blue-50 text-blue-800 disabled:opacity-50">Retry</button>
                    <button onClick={() => decideReview(item.id, 'reject')} disabled={!!busy} className="px-2 py-1 font-semibold rounded border border-rose-300 bg-rose-50 text-rose-800 disabled:opacity-50">Reject</button>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <div className="text-sm font-semibold">LEDES / E-billing Timeline</div>
              <div className="flex items-center gap-2">
                <input
                  className="rounded border border-gray-300 px-2 py-1 text-xs"
                  placeholder="Provider (billing-engine / onelaw / x)"
                  value={ebillingProviderFilter}
                  onChange={e => setEbillingProviderFilter(e.target.value)}
                />
                <button onClick={loadBase} disabled={loading} className="px-2 py-1 text-xs font-semibold rounded border border-gray-300 bg-white">Reload</button>
              </div>
            </div>

            <div className="grid gap-2 md:grid-cols-3 mb-3">
              <Metric label="Transmissions" value={String(ebillingTransmissions.length)} />
              <Metric label="Events" value={String(ebillingEvents.length)} />
              <Metric label="Rejected/Error Tx" value={String(ebillingTransmissions.filter(t => ['rejected', 'error', 'partial'].includes(String(t?.status || '').toLowerCase())).length)} />
            </div>

            <div className="rounded border border-gray-200">
              <div className="px-2 py-1 border-b border-gray-200 bg-gray-50 text-[11px] font-semibold text-slate-700">Transmissions</div>
              <div className="max-h-56 overflow-auto">
                {ebillingTransmissions.length === 0 ? (
                  <p className="p-3 text-xs text-gray-500">No transmissions.</p>
                ) : ebillingTransmissions.map((tx: any) => {
                  const status = String(tx?.status || '').toLowerCase();
                  const canRepair = ['rejected', 'error', 'partial'].includes(status);
                  const relatedEvents = ebillingEvents.filter((ev: any) => (ev?.transmissionId && ev.transmissionId === tx.id) || (tx.externalTransmissionId && ev?.externalTransmissionId === tx.externalTransmissionId)).slice(0, 4);
                  return (
                    <div key={tx.id} className="border-t border-gray-100 p-2 text-xs">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="font-medium text-slate-800">{tx.providerKey || 'provider'} • {(tx.format || 'n/a').toUpperCase()}</div>
                        <div className="text-gray-500">{fmtDate(tx.updatedAt || tx.submittedAt || tx.createdAt)}</div>
                      </div>
                      <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px]">
                        <span className={`rounded px-2 py-0.5 font-semibold ${status === 'accepted' ? 'bg-emerald-100 text-emerald-700' : status === 'queued' ? 'bg-slate-100 text-slate-700' : status === 'submitted' ? 'bg-blue-100 text-blue-700' : status === 'rejected' ? 'bg-rose-100 text-rose-700' : 'bg-amber-100 text-amber-700'}`}>{status || 'queued'}</span>
                        <span className="text-gray-600">Invoice {tx.invoiceId || '-'}</span>
                        {tx.payorClientId && <span className="text-gray-600">Payor {tx.payorClientId}</span>}
                        {tx.externalTransmissionId && <span className="text-gray-500">Ext {tx.externalTransmissionId}</span>}
                      </div>
                      {(tx.errorCode || tx.errorMessage) && (
                        <div className="mt-1 rounded border border-rose-200 bg-rose-50 p-2 text-[11px] text-rose-800">
                          {tx.errorCode ? `${tx.errorCode}: ` : ''}{tx.errorMessage || 'Transmission error'}
                        </div>
                      )}
                      {relatedEvents.length > 0 && (
                        <div className="mt-2 rounded border border-gray-100 bg-gray-50 p-2">
                          <div className="text-[10px] font-semibold uppercase tracking-wide text-gray-500">Recent Events</div>
                          <div className="mt-1 space-y-1">
                            {relatedEvents.map((ev: any) => (
                              <div key={ev.id} className="text-[11px] text-slate-700">
                                <span className="font-semibold">{String(ev.status || 'received')}</span>
                                <span className="ml-2">{String(ev.eventType || 'event')}</span>
                                <span className="ml-2 text-slate-500">{fmtDate(ev.occurredAt || ev.recordedAt)}</span>
                                {(ev.errorCode || ev.resultCode) && <span className="ml-2 text-slate-500">{ev.errorCode || ev.resultCode}</span>}
                              </div>
                            ))}
                          </div>
                        </div>
                      )}
                      {canRepair && (
                        <div className="mt-2 flex flex-wrap gap-2">
                          <input
                            className="min-w-[220px] flex-1 rounded border border-gray-300 px-2 py-1"
                            placeholder="Repair notes (optional)"
                            value={ebillingRepairNotes[tx.id] || ''}
                            onChange={e => setEbillingRepairNotes(prev => ({ ...prev, [tx.id]: e.target.value }))}
                          />
                          <button
                            onClick={() => repairTransmission(tx.id)}
                            disabled={!!busy}
                            className="px-2 py-1 font-semibold rounded border border-indigo-300 bg-indigo-50 text-indigo-800 disabled:opacity-50"
                          >
                            {busy === `ebilling-repair:${tx.id}` ? 'Repairing...' : 'Request Repair / Retry'}
                          </button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default LegalBillingOpsPanel;
