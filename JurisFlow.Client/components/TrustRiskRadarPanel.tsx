import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../services/api';
import { toast } from './Toast';

type Props = { refreshKey?: string };

const fmtDate = (v?: string | null) => (v ? new Date(v).toLocaleString('en-US') : '-');
const fmtNum = (v: unknown, digits = 2) => {
  const n = Number(v ?? 0);
  return Number.isFinite(n) ? n.toFixed(digits) : '0.00';
};

const safeJson = (value?: string | null) => {
  if (!value) return null;
  try { return JSON.parse(value); } catch { return null; }
};

const numOr = (v: unknown, fallback: number) => {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
};

const Metric: React.FC<{ label: string; value: string; hint?: string }> = ({ label, value, hint }) => (
  <div className="rounded border border-gray-200 bg-white p-2">
    <div className="text-[11px] text-gray-500">{label}</div>
    <div className="text-sm font-semibold text-slate-800">{value}</div>
    {hint ? <div className="text-[10px] text-gray-400 mt-0.5">{hint}</div> : null}
  </div>
);

const badge = (v?: string | null) => {
  const t = String(v || '').toLowerCase();
  if (t.includes('critical') || t.includes('hard_hold') || t.includes('hold_placed')) return 'bg-red-100 text-red-700 border-red-200';
  if (t.includes('high') || t.includes('soft_hold') || t.includes('review')) return 'bg-amber-100 text-amber-700 border-amber-200';
  if (t.includes('warn') || t.includes('medium')) return 'bg-yellow-100 text-yellow-700 border-yellow-200';
  if (t.includes('released') || t.includes('resolved')) return 'bg-emerald-100 text-emerald-700 border-emerald-200';
  return 'bg-slate-100 text-slate-700 border-slate-200';
};

const TrustRiskRadarPanel: React.FC<Props> = ({ refreshKey }) => {
  const [loading, setLoading] = useState(false);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [policy, setPolicy] = useState<any | null>(null);
  const [policyTemplates, setPolicyTemplates] = useState<any[]>([]);
  const [policyVersions, setPolicyVersions] = useState<any[]>([]);
  const [events, setEvents] = useState<any[]>([]);
  const [holds, setHolds] = useState<any[]>([]);
  const [metrics, setMetrics] = useState<any | null>(null);
  const [baselines, setBaselines] = useState<any | null>(null);
  const [tuning, setTuning] = useState<any | null>(null);
  const [impact30, setImpact30] = useState<any | null>(null);
  const [impact60, setImpact60] = useState<any | null>(null);
  const [policyImpactCompare, setPolicyImpactCompare] = useState<any | null>(null);
  const [compareDays, setCompareDays] = useState<number>(60);
  const [compareFromVersion, setCompareFromVersion] = useState<number | ''>('');
  const [compareToVersion, setCompareToVersion] = useState<number | ''>('');
  const [newEventIds, setNewEventIds] = useState<string[]>([]);
  const [selectedEventId, setSelectedEventId] = useState('');
  const [selectedEventDetail, setSelectedEventDetail] = useState<any | null>(null);
  const [lastRefreshedAt, setLastRefreshedAt] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [releaseReason, setReleaseReason] = useState<Record<string, string>>({});
  const [releaseApproverReason, setReleaseApproverReason] = useState<Record<string, string>>({});
  const [escalateReason, setEscalateReason] = useState<Record<string, string>>({});
  const [escalateApproverReason, setEscalateApproverReason] = useState<Record<string, string>>({});
  const [underReviewReason, setUnderReviewReason] = useState<Record<string, string>>({});
  const [policyDraft, setPolicyDraft] = useState<any | null>(null);
  const [ackNote, setAckNote] = useState('');
  const [assignUserId, setAssignUserId] = useState('');
  const [assignNote, setAssignNote] = useState('');
  const [reviewDisposition, setReviewDisposition] = useState<'true_positive' | 'false_positive' | 'acceptable_exception'>('true_positive');
  const [reviewDispositionReason, setReviewDispositionReason] = useState('');
  const [reviewDispositionApproverReason, setReviewDispositionApproverReason] = useState('');

  const [filters, setFilters] = useState({
    severity: '',
    decision: '',
    status: '',
    matterId: '',
    clientId: '',
    sourceType: '',
    onlyOpenHolds: true
  });

  const visibleHolds = useMemo(
    () => (filters.onlyOpenHolds ? holds.filter((h: any) => ['placed', 'under_review', 'escalated'].includes(String(h?.status || '').toLowerCase())) : holds),
    [holds, filters.onlyOpenHolds]
  );

  const parsePolicyDraft = (p: any) => {
    if (!p) return null;
    const meta = safeJson(p.metadataJson) || {};
    const opsAlertChannels = Array.isArray(meta.opsAlertChannels)
      ? meta.opsAlertChannels.map((v: any) => String(v))
      : (typeof meta.opsAlertChannels === 'string' ? String(meta.opsAlertChannels).split(',').map((v) => v.trim()).filter(Boolean) : ['outbox']);
    const toStrArray = (v: any, fallback: string[] = []) =>
      Array.isArray(v) ? v.map((x: any) => String(x)) :
      (typeof v === 'string' ? String(v).split(',').map((x) => x.trim()).filter(Boolean) : fallback);
    return {
      policyKey: p.policyKey || 'default',
      templateKey: String(meta.policyTemplate || 'balanced'),
      name: p.name || 'Trust Risk Radar Policy',
      description: p.description || '',
      warnThreshold: numOr(p.warnThreshold, 35),
      reviewThreshold: numOr(p.reviewThreshold, 60),
      softHoldThreshold: numOr(p.softHoldThreshold, 80),
      hardHoldThreshold: numOr(p.hardHoldThreshold, 95),
      behavioralSignalsEnabled: Boolean(meta.behavioralSignalsEnabled ?? true),
      behavioralShadowMode: Boolean(meta.behavioralShadowMode ?? true),
      behavioralLookbackDays: numOr(meta.behavioralLookbackDays, 45),
      behavioralMinSamples: numOr(meta.behavioralMinSamples, 10),
      behavioralAmountRatioThreshold: numOr(meta.behavioralAmountRatioThreshold, 4),
      behavioralTimePatternDeltaThreshold: numOr(meta.behavioralTimePatternDeltaThreshold, 0.35),
      behavioralReversalRateDeltaThreshold: numOr(meta.behavioralReversalRateDeltaThreshold, 0.2),
      behavioralContributionCap: numOr(meta.behavioralContributionCap, 18),
      preflightStrictModeEnabled: Boolean(meta.preflightStrictModeEnabled ?? false),
      preflightStrictRolloutMode: String(meta.preflightStrictRolloutMode || 'warn'),
      preflightHighConfidenceOnly: Boolean(meta.preflightHighConfidenceOnly ?? true),
      preflightMinSeverity: String(meta.preflightMinSeverity || 'critical'),
      preflightRecentEventWindowMinutes: numOr(meta.preflightRecentEventWindowMinutes, 30),
      preflightDuplicateSuppressionEnabled: Boolean(meta.preflightDuplicateSuppressionEnabled ?? true),
      operationFailModesJson: JSON.stringify(meta.operationFailModes || {
        manual_ledger_post: 'fail_open',
        ledger_reversal: 'fail_open',
        payment_allocation_apply: 'fail_open',
        payment_allocation_reverse: 'fail_open'
      }, null, 2),
      opsAlertsEnabled: Boolean(meta.opsAlertsEnabled ?? false),
      opsAlertChannels,
      overrideRoles: toStrArray(meta.overrideRoles, ['SecurityAdmin', 'Admin']),
      releaseRoles: toStrArray(meta.releaseRoles, ['FinanceAdmin', 'Admin']),
      criticalDualApprovalEnabled: Boolean(meta.criticalDualApprovalEnabled ?? false),
      criticalDualApprovalSecondaryRoles: toStrArray(meta.criticalDualApprovalSecondaryRoles, ['SecurityAdmin', 'Admin']),
      holdEscalationSlaMinutes: numOr(meta.holdEscalationSlaMinutes, 120),
      softHoldExpiryHours: numOr(meta.softHoldExpiryHours, 24),
      hardHoldExpiryHours: numOr(meta.hardHoldExpiryHours, 72),
      requireCriticalThresholdChangeReview: Boolean(meta.criticalThresholdChangeReviewRequired ?? true),
      criticalThresholdChangeReason: '',
      trustAccountOverridesJson: JSON.stringify(meta.trustAccountPolicyOverrides || {}, null, 2)
    };
  };

  const loadRadar = async (keepLoading = false) => {
    if (!keepLoading) setLoading(true);
    try {
      const previousRefresh = lastRefreshedAt;
      const [p, templates, versions, evs, hds, m, b, t] = await Promise.all([
        api.legalBilling.getTrustRiskPolicy(),
        api.legalBilling.getTrustRiskPolicyTemplates(),
        api.legalBilling.getTrustRiskPolicyVersions({ limit: 10 }),
        api.legalBilling.getTrustRiskEvents({
          severity: filters.severity || undefined,
          decision: filters.decision || undefined,
          status: filters.status || undefined,
          matterId: filters.matterId || undefined,
          clientId: filters.clientId || undefined,
          sourceType: filters.sourceType || undefined,
          limit: 80
        }),
        api.legalBilling.getTrustRiskHolds({
          limit: 80
        }),
        api.legalBilling.getTrustRiskMetrics({ days: 30 }),
        api.legalBilling.getTrustRiskBaselines({ days: 60, top: 6 }),
        api.legalBilling.getTrustRiskTuning({ days: 45 })
      ]);

      setPolicy(p || null);
      setPolicyTemplates(Array.isArray(templates) ? templates : []);
      setPolicyVersions(Array.isArray(versions) ? versions : []);
      setEvents(Array.isArray(evs) ? evs : []);
      const eventList = Array.isArray(evs) ? evs : [];
      const newIds = previousRefresh
        ? eventList.filter((ev: any) => {
            const ts = ev?.createdAt ? new Date(ev.createdAt).getTime() : 0;
            const cutoff = new Date(previousRefresh).getTime();
            return Number.isFinite(ts) && Number.isFinite(cutoff) && ts > cutoff;
          }).map((ev: any) => String(ev.id))
        : [];
      setNewEventIds(newIds);
      setHolds(Array.isArray(hds) ? hds : []);
      setMetrics(m || null);
      setBaselines(b || null);
      setTuning(t || null);
      setPolicyDraft(parsePolicyDraft(p || null));
      setLastRefreshedAt(new Date().toISOString());
    } catch (e) {
      console.error(e);
      if (!keepLoading) toast.error('Failed to load trust risk radar.');
    } finally {
      if (!keepLoading) setLoading(false);
    }
  };

  const loadEventDetail = async (eventId: string) => {
    if (!eventId) { setSelectedEventDetail(null); return; }
    try {
      const detail = await api.legalBilling.getTrustRiskEventDetail(eventId);
      setSelectedEventDetail(detail || null);
    } catch (e) {
      console.error(e);
      toast.error('Failed to load trust risk event detail.');
    }
  };

  useEffect(() => { void loadRadar(); }, [refreshKey, filters.severity, filters.decision, filters.status, filters.matterId, filters.clientId, filters.sourceType, filters.onlyOpenHolds]);
  useEffect(() => { if (selectedEventId) void loadEventDetail(selectedEventId); }, [selectedEventId]);
  useEffect(() => { setPolicyDraft(parsePolicyDraft(policy)); }, [policy]);
  useEffect(() => {
    if (!Array.isArray(policyVersions) || policyVersions.length === 0) return;
    setCompareToVersion((prev) => {
      if (typeof prev === 'number') return prev;
      const first = policyVersions[0]?.versionNumber;
      return typeof first === 'number' ? first : '';
    });
    setCompareFromVersion((prev) => {
      if (typeof prev === 'number') return prev;
      const second = policyVersions.find((p: any, idx: number) => idx > 0 && typeof p?.versionNumber === 'number')?.versionNumber;
      const fallback = policyVersions[0]?.versionNumber;
      return typeof second === 'number' ? second : (typeof fallback === 'number' ? fallback : '');
    });
  }, [policyVersions]);

  useEffect(() => {
    if (!autoRefresh) return;
    const timer = setInterval(() => {
      void loadRadar(true);
      if (selectedEventId) void loadEventDetail(selectedEventId);
    }, 10000);
    return () => clearInterval(timer);
  }, [autoRefresh, selectedEventId, filters]);

  const parsedReasons = useMemo(() => {
    return Array.isArray(safeJson(selectedEventDetail?.riskEvent?.riskReasonsJson))
      ? safeJson(selectedEventDetail?.riskEvent?.riskReasonsJson)
      : [];
  }, [selectedEventDetail]);

  const parsedEvidence = useMemo(() => safeJson(selectedEventDetail?.riskEvent?.evidenceJson) || {}, [selectedEventDetail]);
  const parsedFeatures = useMemo(() => safeJson(selectedEventDetail?.riskEvent?.featuresJson) || {}, [selectedEventDetail]);

  const trustAccountId = selectedEventDetail?.related?.trustTransaction?.trustAccountId || parsedEvidence?.trustAccountId || null;
  const openHolds = useMemo(() => holds.filter(h => ['placed', 'under_review', 'escalated'].includes(String(h?.status || '').toLowerCase())), [holds]);

  const applyDrilldown = (kind: 'matter' | 'client', id?: string | null) => {
    if (!id) return;
    setFilters(prev => ({ ...prev, matterId: kind === 'matter' ? id : prev.matterId, clientId: kind === 'client' ? id : prev.clientId }));
  };

  const rescoreEvent = async (eventId: string) => {
    setBusyKey(`rescore:${eventId}`);
    try {
      const result = await api.legalBilling.rescoreTrustRiskEvent(eventId, { reason: 'Manual rescore from Trust Risk Radar panel' });
      toast.success(`Rescored. New event: ${result?.rescoredEventId || 'created'}`);
      await loadRadar(true);
      if (selectedEventId) await loadEventDetail(selectedEventId);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to rescore event.');
    } finally {
      setBusyKey(null);
    }
  };

  const actOnHold = async (holdId: string, action: 'under_review' | 'release' | 'escalate') => {
    const reason =
      action === 'under_review' ? (underReviewReason[holdId] || '') :
      action === 'release' ? (releaseReason[holdId] || '') :
      (escalateReason[holdId] || '');
    const approverReason =
      action === 'release' ? (releaseApproverReason[holdId] || '') :
      action === 'escalate' ? (escalateApproverReason[holdId] || '') :
      '';

    if ((action === 'release' || action === 'escalate') && !reason.trim()) {
      toast.error('Reason is required.');
      return;
    }
    if ((action === 'release' || action === 'escalate') && !approverReason.trim()) {
      toast.error('Approver reason is required.');
      return;
    }

    setBusyKey(`${action}:${holdId}`);
    try {
      if (action === 'under_review') {
        await api.legalBilling.markTrustRiskHoldUnderReview(holdId, { reason: reason.trim() || null });
      } else if (action === 'release') {
        await api.legalBilling.releaseTrustRiskHold(holdId, { reason: reason.trim(), approverReason: approverReason.trim() });
      } else {
        await api.legalBilling.escalateTrustRiskHold(holdId, { reason: reason.trim(), approverReason: approverReason.trim() });
      }

      toast.success(`Hold ${action.replace('_', ' ')} action completed.`);
      await loadRadar(true);
      if (selectedEventId) await loadEventDetail(selectedEventId);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Hold action failed.');
    } finally {
      setBusyKey(null);
    }
  };

  const batchRescoreVisible = async () => {
    setBusyKey('batch-rescore');
    try {
      const result = await api.legalBilling.rescoreTrustRiskEventsBatch({
        eventIds: events.map((e: any) => e.id),
        limit: Math.min(events.length || 0, 100),
        reason: 'Batch rescore from Trust Risk Radar panel'
      });
      toast.success(`Batch rescore completed. Rescored=${result?.rescoredCount ?? 0}, Skipped=${result?.skippedCount ?? 0}`);
      await loadRadar(true);
      if (selectedEventId) await loadEventDetail(selectedEventId);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Batch rescore failed.');
    } finally {
      setBusyKey(null);
    }
  };

  const acknowledgeSelectedEvent = async () => {
    if (!selectedEventDetail?.riskEvent?.id) return;
    setBusyKey(`ack:${selectedEventDetail.riskEvent.id}`);
    try {
      await api.legalBilling.acknowledgeTrustRiskEvent(selectedEventDetail.riskEvent.id, { note: ackNote || null });
      toast.success('Trust risk event acknowledged.');
      await loadRadar(true);
      await loadEventDetail(selectedEventDetail.riskEvent.id);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to acknowledge event.');
    } finally {
      setBusyKey(null);
    }
  };

  const assignSelectedEvent = async () => {
    if (!selectedEventDetail?.riskEvent?.id) return;
    if (!assignUserId.trim()) {
      toast.error('Assignee user id is required.');
      return;
    }
    setBusyKey(`assign:${selectedEventDetail.riskEvent.id}`);
    try {
      await api.legalBilling.assignTrustRiskEvent(selectedEventDetail.riskEvent.id, { assigneeUserId: assignUserId.trim(), note: assignNote || null });
      toast.success('Trust risk event assigned.');
      await loadRadar(true);
      await loadEventDetail(selectedEventDetail.riskEvent.id);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to assign event.');
    } finally {
      setBusyKey(null);
    }
  };

  const submitReviewDisposition = async () => {
    if (!selectedEventDetail?.riskEvent?.id) return;
    setBusyKey(`disposition:${selectedEventDetail.riskEvent.id}`);
    try {
      await api.legalBilling.setTrustRiskReviewDisposition(selectedEventDetail.riskEvent.id, {
        disposition: reviewDisposition,
        reason: reviewDispositionReason.trim(),
        approverReason: reviewDispositionApproverReason.trim()
      });
      toast.success('Review disposition recorded.');
      await loadRadar(true);
      await loadEventDetail(selectedEventDetail.riskEvent.id);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to set review disposition.');
    } finally {
      setBusyKey(null);
    }
  };

  const applySuggestedThresholds = () => {
    if (!policyDraft || !tuning?.suggestedThresholds) return;
    setPolicyDraft((prev: any) => prev ? ({
      ...prev,
      warnThreshold: numOr(tuning.suggestedThresholds.warn, prev.warnThreshold),
      reviewThreshold: numOr(tuning.suggestedThresholds.review, prev.reviewThreshold),
      softHoldThreshold: numOr(tuning.suggestedThresholds.softHold, prev.softHoldThreshold),
      hardHoldThreshold: numOr(tuning.suggestedThresholds.hardHold, prev.hardHoldThreshold)
    }) : prev);
  };

  const runThresholdImpactSimulation = async (days: 30 | 60) => {
    if (!policyDraft) return;
    setBusyKey(`impact:${days}`);
    try {
      const result = await api.legalBilling.getTrustRiskTuningImpact({
        days,
        warn: Number(policyDraft.warnThreshold),
        review: Number(policyDraft.reviewThreshold),
        softHold: Number(policyDraft.softHoldThreshold),
        hardHold: Number(policyDraft.hardHoldThreshold)
      });
      if (days === 30) setImpact30(result || null);
      else setImpact60(result || null);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || `Failed to run ${days}d impact simulation.`);
    } finally {
      setBusyKey(null);
    }
  };

  const runPolicyVersionCompare = async () => {
    setBusyKey('policy-compare');
    try {
      const toVersion = typeof compareToVersion === 'number' ? compareToVersion : undefined;
      const fromVersion = typeof compareFromVersion === 'number' ? compareFromVersion : undefined;
      const toPolicy = Array.isArray(policyVersions) && typeof toVersion === 'number'
        ? policyVersions.find((p: any) => Number(p?.versionNumber) === toVersion)
        : null;
      const fromPolicy = Array.isArray(policyVersions) && typeof fromVersion === 'number'
        ? policyVersions.find((p: any) => Number(p?.versionNumber) === fromVersion)
        : null;
      const result = await api.legalBilling.compareTrustRiskPolicyImpact({
        days: compareDays,
        fromPolicyId: fromPolicy?.id,
        fromVersion,
        toPolicyId: toPolicy?.id,
        toVersion
      });
      setPolicyImpactCompare(result || null);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to compare policy versions.');
    } finally {
      setBusyKey(null);
    }
  };

  const savePolicyTuning = async () => {
    if (!policyDraft) return;
    const warn = Number(policyDraft.warnThreshold);
    const review = Number(policyDraft.reviewThreshold);
    const soft = Number(policyDraft.softHoldThreshold);
    const hard = Number(policyDraft.hardHoldThreshold);
    if (!(warn <= review && review <= soft && soft <= hard)) {
      toast.error('Thresholds must satisfy Warn <= Review <= Soft <= Hard.');
      return;
    }

    let trustAccountOverrides: any[] | undefined;
    let operationFailModes: Record<string, string> | undefined;
    try {
      const raw = String(policyDraft.trustAccountOverridesJson || '').trim();
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
          trustAccountOverrides = Object.entries(parsed).map(([trustAccountId, cfg]: [string, any]) => ({
            trustAccountId,
            warnThreshold: cfg?.warnThreshold,
            reviewThreshold: cfg?.reviewThreshold,
            softHoldThreshold: cfg?.softHoldThreshold,
            hardHoldThreshold: cfg?.hardHoldThreshold,
            actionMap: cfg?.actionMap,
            overrideRoles: Array.isArray(cfg?.overrideRoles) ? cfg.overrideRoles : undefined,
            releaseRoles: Array.isArray(cfg?.releaseRoles) ? cfg.releaseRoles : undefined,
            criticalDualApprovalSecondaryRoles: Array.isArray(cfg?.criticalDualApprovalSecondaryRoles) ? cfg.criticalDualApprovalSecondaryRoles : undefined,
            criticalDualApprovalEnabled: typeof cfg?.criticalDualApprovalEnabled === 'boolean' ? cfg.criticalDualApprovalEnabled : undefined,
            holdEscalationSlaMinutes: cfg?.holdEscalationSlaMinutes,
            softHoldExpiryHours: cfg?.softHoldExpiryHours,
            hardHoldExpiryHours: cfg?.hardHoldExpiryHours,
            opsAlertsEnabled: typeof cfg?.opsAlertsEnabled === 'boolean' ? cfg.opsAlertsEnabled : undefined,
            opsAlertChannels: Array.isArray(cfg?.opsAlertChannels) ? cfg.opsAlertChannels : undefined,
            note: cfg?.note
          }));
        } else {
          throw new Error('Trust account overrides must be a JSON object keyed by trustAccountId.');
        }
      }
    } catch (e: any) {
      toast.error(e?.message || 'Invalid trust account override JSON.');
      return;
    }

    try {
      const rawOps = String(policyDraft.operationFailModesJson || '').trim();
      if (rawOps) {
        const parsedOps = JSON.parse(rawOps);
        if (!parsedOps || typeof parsedOps !== 'object' || Array.isArray(parsedOps)) {
          throw new Error('Operation fail modes must be a JSON object keyed by operation type.');
        }
        operationFailModes = Object.fromEntries(
          Object.entries(parsedOps)
            .filter(([k]) => Boolean(String(k || '').trim()))
            .map(([k, v]) => [String(k).trim(), String(v ?? '').trim().toLowerCase() === 'fail_closed' ? 'fail_closed' : 'fail_open'])
        );
      }
    } catch (e: any) {
      toast.error(e?.message || 'Invalid operation fail mode JSON.');
      return;
    }

    setBusyKey('policy-save');
    try {
      await api.legalBilling.upsertTrustRiskPolicy({
        policyKey: policyDraft.policyKey || policy?.policyKey || 'default',
        name: policyDraft.name || policy?.name || 'Trust Risk Radar Policy',
        description: policyDraft.description || policy?.description || null,
        templateKey: policyDraft.templateKey || 'balanced',
        policyTemplate: policyDraft.templateKey || 'balanced',
        warnThreshold: warn,
        reviewThreshold: review,
        softHoldThreshold: soft,
        hardHoldThreshold: hard,
        overrideRoles: Array.isArray(policyDraft.overrideRoles) ? policyDraft.overrideRoles : ['SecurityAdmin', 'Admin'],
        releaseRoles: Array.isArray(policyDraft.releaseRoles) ? policyDraft.releaseRoles : ['FinanceAdmin', 'Admin'],
        criticalDualApprovalEnabled: Boolean(policyDraft.criticalDualApprovalEnabled),
        criticalDualApprovalSecondaryRoles: Array.isArray(policyDraft.criticalDualApprovalSecondaryRoles) ? policyDraft.criticalDualApprovalSecondaryRoles : ['SecurityAdmin', 'Admin'],
        holdEscalationSlaMinutes: Number(policyDraft.holdEscalationSlaMinutes),
        softHoldExpiryHours: Number(policyDraft.softHoldExpiryHours),
        hardHoldExpiryHours: Number(policyDraft.hardHoldExpiryHours),
        requireCriticalThresholdChangeReview: Boolean(policyDraft.requireCriticalThresholdChangeReview),
        criticalThresholdChangeReason: String(policyDraft.criticalThresholdChangeReason || '').trim() || undefined,
        behavioralSignalsEnabled: Boolean(policyDraft.behavioralSignalsEnabled),
        behavioralShadowMode: Boolean(policyDraft.behavioralShadowMode),
        behavioralLookbackDays: Number(policyDraft.behavioralLookbackDays),
        behavioralMinSamples: Number(policyDraft.behavioralMinSamples),
        behavioralAmountRatioThreshold: Number(policyDraft.behavioralAmountRatioThreshold),
        behavioralTimePatternDeltaThreshold: Number(policyDraft.behavioralTimePatternDeltaThreshold),
        behavioralReversalRateDeltaThreshold: Number(policyDraft.behavioralReversalRateDeltaThreshold),
        behavioralContributionCap: Number(policyDraft.behavioralContributionCap),
        preflightStrictModeEnabled: Boolean(policyDraft.preflightStrictModeEnabled),
        preflightStrictRolloutMode: String(policyDraft.preflightStrictRolloutMode || 'warn'),
        preflightHighConfidenceOnly: Boolean(policyDraft.preflightHighConfidenceOnly),
        preflightMinSeverity: String(policyDraft.preflightMinSeverity || 'critical'),
        preflightRecentEventWindowMinutes: Number(policyDraft.preflightRecentEventWindowMinutes),
        preflightDuplicateSuppressionEnabled: Boolean(policyDraft.preflightDuplicateSuppressionEnabled),
        operationFailModes,
        opsAlertsEnabled: Boolean(policyDraft.opsAlertsEnabled),
        opsAlertChannels: Array.isArray(policyDraft.opsAlertChannels) ? policyDraft.opsAlertChannels : ['outbox'],
        trustAccountOverrides
      });
      toast.success('Trust risk policy tuning saved.');
      await loadRadar(true);
      if (selectedEventId) await loadEventDetail(selectedEventId);
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to save trust risk policy tuning.');
    } finally {
      setBusyKey(null);
    }
  };

  const applyPolicyTemplate = (templateKey: string) => {
    const template = (policyTemplates || []).find((t: any) => String(t?.templateKey || '').toLowerCase() === String(templateKey || '').toLowerCase());
    if (!template || !policyDraft) return;
    setPolicyDraft((prev: any) => ({
      ...(prev || {}),
      templateKey: template.templateKey || templateKey,
      warnThreshold: numOr(template?.thresholds?.warn, prev?.warnThreshold ?? 35),
      reviewThreshold: numOr(template?.thresholds?.review, prev?.reviewThreshold ?? 60),
      softHoldThreshold: numOr(template?.thresholds?.softHold, prev?.softHoldThreshold ?? 80),
      hardHoldThreshold: numOr(template?.thresholds?.hardHold, prev?.hardHoldThreshold ?? 95),
      holdEscalationSlaMinutes: numOr(template?.holdEscalationSlaMinutes, prev?.holdEscalationSlaMinutes ?? 120),
      softHoldExpiryHours: numOr(template?.softHoldExpiryHours, prev?.softHoldExpiryHours ?? 24),
      hardHoldExpiryHours: numOr(template?.hardHoldExpiryHours, prev?.hardHoldExpiryHours ?? 72),
      criticalDualApprovalEnabled: Boolean(template?.criticalDualApprovalEnabled),
      overrideRoles: Array.isArray(template?.overrideRoles) ? template.overrideRoles : prev?.overrideRoles,
      releaseRoles: Array.isArray(template?.releaseRoles) ? template.releaseRoles : prev?.releaseRoles,
      criticalDualApprovalSecondaryRoles: Array.isArray(template?.criticalDualApprovalSecondaryRoles) ? template.criticalDualApprovalSecondaryRoles : prev?.criticalDualApprovalSecondaryRoles
    }));
  };

  const exportEvidence = async () => {
    setBusyKey('evidence-export');
    try {
      const payload = await api.legalBilling.getTrustRiskEvidenceExport({
        days: 90,
        policyLimit: 50,
        eventLimit: 1500,
        holdLimit: 1500,
        actionLimit: 4000,
        auditLimit: 4000,
        includeAuditLogs: true,
        includeEvents: true
      });

      if (!payload) {
        toast.error('No trust risk evidence export payload returned.');
        return;
      }

      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `trust-risk-radar-evidence-export-${stamp}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);

      toast.success('Trust risk evidence export downloaded.');
    } catch (e: any) {
      console.error(e);
      toast.error(e?.message || 'Failed to export trust risk evidence.');
    } finally {
      setBusyKey(null);
    }
  };

  return (
    <section className="rounded-xl border border-slate-200 bg-slate-50 p-4 mt-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-slate-900">Trust Risk Radar</h3>
          <p className="text-xs text-gray-500 mt-1">
            Real-time trust risk alerts, hold queue, explainable reasons, and human-in-the-loop overrides.
          </p>
          <p className="text-[11px] text-amber-700 mt-1">
            Hard holds are typically created post-commit; same-transaction blocking is intentionally conservative to avoid false-positive operational lockups.
          </p>
        </div>
        <div className="flex items-center gap-2 text-xs">
          <label className="inline-flex items-center gap-1 text-gray-600">
            <input type="checkbox" checked={autoRefresh} onChange={e => setAutoRefresh(e.target.checked)} />
            Auto refresh (10s)
          </label>
          <button
            onClick={() => void batchRescoreVisible()}
            disabled={busyKey === 'batch-rescore' || events.length === 0}
            className="rounded border border-purple-200 bg-white px-2 py-1 text-xs font-semibold text-purple-700 hover:bg-purple-50 disabled:opacity-50"
          >
            {busyKey === 'batch-rescore' ? 'Rescoring...' : `Batch Re-score (${Math.min(events.length, 100)})`}
          </button>
          <button
            onClick={() => void exportEvidence()}
            disabled={busyKey === 'evidence-export'}
            className="rounded border border-emerald-200 bg-white px-2 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
          >
            {busyKey === 'evidence-export' ? 'Exporting...' : 'Export Evidence JSON'}
          </button>
          <button
            onClick={() => void loadRadar()}
            className="rounded border border-gray-300 bg-white px-2 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-50"
          >
            {loading ? 'Loading...' : 'Refresh'}
          </button>
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 xl:grid-cols-8 gap-2 mt-3">
        <Metric label="Events (30d)" value={String(metrics?.summary?.eventCount ?? events.length)} />
        <Metric label="Alerts" value={String(metrics?.summary?.alertCount ?? 0)} />
        <Metric label="New Since Refresh" value={String(newEventIds.length)} hint="poll delta" />
        <Metric label="Open Holds" value={String(metrics?.summary?.openHolds ?? openHolds.length)} />
        <Metric label="Hard Holds" value={String(metrics?.summary?.hardHoldsOpen ?? 0)} />
        <Metric label="Soft Holds" value={String(metrics?.summary?.softHoldsOpen ?? 0)} />
        <Metric label="Mean Review (min)" value={metrics?.summary?.meanTimeToReviewMinutes == null ? 'n/a' : fmtNum(metrics.summary.meanTimeToReviewMinutes, 1)} hint={metrics?.dataQuality?.meanTimeToReview || 'proxy'} />
        <Metric label="Mean Hold Release (min)" value={metrics?.summary?.meanHoldReleaseMinutes == null ? 'n/a' : fmtNum(metrics.summary.meanHoldReleaseMinutes, 1)} />
        <Metric label="False Positive %" value={metrics?.summary?.falsePositiveRatePct == null ? 'n/a' : `${fmtNum(metrics.summary.falsePositiveRatePct, 1)}%`} hint={metrics?.dataQuality?.falsePositiveRate || 'proxy'} />
      </div>

      <div className="rounded border border-slate-200 bg-white p-3 mt-3">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-2 text-xs">
          <div>
            <div className="font-semibold text-slate-800">X3 Review Timing Accuracy</div>
            <div className="text-gray-600 mt-1">
              Exact: {metrics?.x3?.meanTimeToReview?.exactMinutes == null ? 'n/a' : `${fmtNum(metrics.x3.meanTimeToReview.exactMinutes, 1)} min`} ({metrics?.x3?.meanTimeToReview?.exactSampleCount ?? 0} samples)
            </div>
            <div className="text-gray-500">
              Proxy: {metrics?.x3?.meanTimeToReview?.proxyMinutes == null ? 'n/a' : `${fmtNum(metrics.x3.meanTimeToReview.proxyMinutes, 1)} min`} ({metrics?.x3?.meanTimeToReview?.proxySampleCount ?? 0} samples)
            </div>
          </div>
          <div className="md:col-span-2">
            <div className="font-semibold text-slate-800">Hold Release Time by Severity</div>
            <div className="mt-1 flex flex-wrap gap-2">
              {metrics?.x3?.holdReleaseTimeBySeverity && Object.keys(metrics.x3.holdReleaseTimeBySeverity).length > 0 ? (
                Object.entries(metrics.x3.holdReleaseTimeBySeverity).map(([severity, row]: [string, any]) => (
                  <div key={severity} className="rounded border border-gray-200 bg-slate-50 px-2 py-1 text-[11px] text-gray-700">
                    <span className="font-medium">{severity}</span> | n={row?.count ?? 0} | mean={row?.meanMinutes == null ? 'n/a' : fmtNum(row.meanMinutes, 1)}m | p90={row?.p90Minutes == null ? 'n/a' : fmtNum(row.p90Minutes, 1)}m
                  </div>
                ))
              ) : (
                <div className="text-[11px] text-gray-500">No released hold severity samples yet.</div>
              )}
            </div>
          </div>
        </div>
      </div>

      <div className="rounded border border-slate-200 bg-white p-3 mt-3">
        <div className="grid grid-cols-2 md:grid-cols-4 xl:grid-cols-8 gap-2">
          <select className="rounded border border-gray-300 p-2 text-xs" value={filters.severity} onChange={e => setFilters(f => ({ ...f, severity: e.target.value }))}>
            <option value="">All severities</option>
            <option value="low">low</option>
            <option value="medium">medium</option>
            <option value="high">high</option>
            <option value="critical">critical</option>
          </select>
          <select className="rounded border border-gray-300 p-2 text-xs" value={filters.decision} onChange={e => setFilters(f => ({ ...f, decision: e.target.value }))}>
            <option value="">All decisions</option>
            <option value="warn">warn</option>
            <option value="review_required">review_required</option>
            <option value="soft_hold">soft_hold</option>
            <option value="hard_hold">hard_hold</option>
            <option value="record">record</option>
          </select>
          <select className="rounded border border-gray-300 p-2 text-xs" value={filters.status} onChange={e => setFilters(f => ({ ...f, status: e.target.value }))}>
            <option value="">All statuses</option>
            <option value="recorded">recorded</option>
            <option value="warned">warned</option>
            <option value="review_queued">review_queued</option>
            <option value="hold_placed">hold_placed</option>
            <option value="under_review">under_review</option>
            <option value="closed">closed</option>
          </select>
          <select className="rounded border border-gray-300 p-2 text-xs" value={filters.sourceType} onChange={e => setFilters(f => ({ ...f, sourceType: e.target.value }))}>
            <option value="">All sources</option>
            <option value="billing_ledger_entry">billing_ledger_entry</option>
            <option value="billing_payment_allocation">billing_payment_allocation</option>
            <option value="trust_transaction">trust_transaction</option>
            <option value="billing_operation_attempt">billing_operation_attempt</option>
          </select>
          <input className="rounded border border-gray-300 p-2 text-xs" placeholder="MatterId filter" value={filters.matterId} onChange={e => setFilters(f => ({ ...f, matterId: e.target.value }))} />
          <input className="rounded border border-gray-300 p-2 text-xs" placeholder="ClientId filter" value={filters.clientId} onChange={e => setFilters(f => ({ ...f, clientId: e.target.value }))} />
          <label className="inline-flex items-center gap-2 text-xs text-gray-600 border border-gray-300 rounded px-2 py-2 bg-white">
            <input type="checkbox" checked={filters.onlyOpenHolds} onChange={e => setFilters(f => ({ ...f, onlyOpenHolds: e.target.checked }))} />
            Open holds only
          </label>
          <div className="text-[11px] text-gray-500 flex items-center">
            Last refresh: {fmtDate(lastRefreshedAt)}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-[1.2fr_1fr] gap-4 mt-4">
        <div className="space-y-4">
          <div className="rounded border border-slate-200 bg-white p-3">
            <div className="flex items-center justify-between mb-2">
              <h4 className="text-sm font-semibold text-slate-900">Recent Alerts / Event Stream</h4>
              <span className="text-xs text-gray-500">{events.length} items</span>
            </div>
            <div className="space-y-2 max-h-[420px] overflow-auto">
              {events.length === 0 ? (
                <p className="text-xs text-gray-500">No trust risk events for current filters.</p>
              ) : events.map((ev: any) => (
                <button
                  key={ev.id}
                  onClick={() => setSelectedEventId(ev.id)}
                  className={`w-full text-left rounded border p-2 hover:bg-slate-50 ${selectedEventId === ev.id ? 'border-slate-400 bg-slate-50' : 'border-gray-200 bg-white'}`}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    {newEventIds.includes(String(ev.id)) ? (
                      <span className="inline-flex items-center rounded border px-1.5 py-0.5 text-[10px] font-semibold bg-cyan-100 text-cyan-800 border-cyan-200">NEW</span>
                    ) : null}
                    <span className={`inline-flex items-center rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(ev.severity)}`}>{String(ev.severity || 'unknown')}</span>
                    <span className={`inline-flex items-center rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(ev.decision)}`}>{String(ev.decision || 'record')}</span>
                    <span className="text-[11px] text-gray-600">{ev.sourceType}</span>
                    <span className="text-[11px] text-gray-400">{fmtDate(ev.createdAt)}</span>
                  </div>
                  <div className="mt-1 text-sm font-medium text-slate-800">
                    Score {fmtNum(ev.riskScore)} • {ev.triggerType}
                  </div>
                  <div className="mt-1 text-[11px] text-gray-500 break-all">
                    {ev.sourceId} {ev.matterId ? `• matter:${ev.matterId}` : ''} {ev.clientId ? `• client:${ev.clientId}` : ''}
                  </div>
                </button>
              ))}
            </div>
          </div>

          <div className="rounded border border-slate-200 bg-white p-3">
            <div className="flex items-center justify-between mb-2">
              <h4 className="text-sm font-semibold text-slate-900">Hold Queue</h4>
              <span className="text-xs text-gray-500">{openHolds.length} open</span>
            </div>
            <div className="space-y-2 max-h-[420px] overflow-auto">
                  {visibleHolds.length === 0 ? (
                <p className="text-xs text-gray-500">No holds in current window.</p>
              ) : visibleHolds.map((hold: any) => (
                <div key={hold.id} className="rounded border border-gray-200 bg-white p-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`inline-flex rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(hold.holdType)}`}>{hold.holdType}</span>
                    <span className={`inline-flex rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(hold.status)}`}>{hold.status}</span>
                    <span className="text-[11px] text-gray-600">{hold.targetType}</span>
                    <span className="text-[11px] text-gray-400">{fmtDate(hold.placedAt)}</span>
                  </div>
                  <div className="mt-1 text-[11px] text-gray-600 break-all">{hold.targetId}</div>
                  <div className="mt-2 grid grid-cols-1 md:grid-cols-3 gap-2">
                    <input
                      className="rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Under review note (optional)"
                      value={underReviewReason[hold.id] || ''}
                      onChange={e => setUnderReviewReason(prev => ({ ...prev, [hold.id]: e.target.value }))}
                    />
                    <button
                      onClick={() => void actOnHold(hold.id, 'under_review')}
                      disabled={busyKey === `under_review:${hold.id}` || String(hold.status).toLowerCase() === 'released'}
                      className="rounded border border-blue-200 px-2 py-1 text-xs font-semibold text-blue-700 hover:bg-blue-50 disabled:opacity-50"
                    >
                      {busyKey === `under_review:${hold.id}` ? '...' : 'Under Review'}
                    </button>
                    <button
                      onClick={() => setSelectedEventId(String(hold.trustRiskEventId || ''))}
                      className="rounded border border-gray-300 px-2 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-50"
                    >
                      Open Event
                    </button>
                  </div>
                  <div className="mt-2 grid grid-cols-1 md:grid-cols-2 gap-2">
                    <input
                      className="rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Escalate reason (required)"
                      value={escalateReason[hold.id] || ''}
                      onChange={e => setEscalateReason(prev => ({ ...prev, [hold.id]: e.target.value }))}
                    />
                    <input
                      className="rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Escalate approver reason (required)"
                      value={escalateApproverReason[hold.id] || ''}
                      onChange={e => setEscalateApproverReason(prev => ({ ...prev, [hold.id]: e.target.value }))}
                    />
                    <button
                      onClick={() => void actOnHold(hold.id, 'escalate')}
                      disabled={busyKey === `escalate:${hold.id}` || String(hold.status).toLowerCase() === 'released'}
                      className="rounded border border-amber-200 px-2 py-1 text-xs font-semibold text-amber-700 hover:bg-amber-50 disabled:opacity-50"
                    >
                      {busyKey === `escalate:${hold.id}` ? '...' : 'Escalate'}
                    </button>
                    <input
                      className="rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Release reason (required)"
                      value={releaseReason[hold.id] || ''}
                      onChange={e => setReleaseReason(prev => ({ ...prev, [hold.id]: e.target.value }))}
                    />
                    <input
                      className="rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Release approver reason (required)"
                      value={releaseApproverReason[hold.id] || ''}
                      onChange={e => setReleaseApproverReason(prev => ({ ...prev, [hold.id]: e.target.value }))}
                    />
                    <button
                      onClick={() => void actOnHold(hold.id, 'release')}
                      disabled={busyKey === `release:${hold.id}` || String(hold.status).toLowerCase() === 'released'}
                      className="rounded border border-emerald-200 px-2 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                    >
                      {busyKey === `release:${hold.id}` ? '...' : 'Release'}
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="space-y-4">
          <div className="rounded border border-slate-200 bg-white p-3">
            <h4 className="text-sm font-semibold text-slate-900 mb-2">Policy Snapshot</h4>
            {policy ? (
              <div className="space-y-2 text-xs text-gray-700">
                <div className="grid grid-cols-2 gap-2">
                  <div><span className="text-gray-500">Policy</span><div className="font-medium">{policy.name || policy.policyKey}</div></div>
                  <div><span className="text-gray-500">Version</span><div className="font-medium">{policy.versionNumber}</div></div>
                  <div><span className="text-gray-500">Warn/Review</span><div className="font-medium">{policy.warnThreshold} / {policy.reviewThreshold}</div></div>
                  <div><span className="text-gray-500">Soft/Hard</span><div className="font-medium">{policy.softHoldThreshold} / {policy.hardHoldThreshold}</div></div>
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <div><span className="text-gray-500">Behavioral</span><div className="font-medium">{String(policyDraft?.behavioralSignalsEnabled ? 'enabled' : 'disabled')}</div></div>
                  <div><span className="text-gray-500">Shadow Mode</span><div className="font-medium">{String(policyDraft?.behavioralShadowMode ? 'on' : 'off')}</div></div>
                  <div><span className="text-gray-500">Ops Alerts</span><div className="font-medium">{String(policyDraft?.opsAlertsEnabled ? 'enabled' : 'disabled')}</div></div>
                  <div><span className="text-gray-500">Routes</span><div className="font-medium">{Array.isArray(policyDraft?.opsAlertChannels) ? policyDraft.opsAlertChannels.join(', ') : 'n/a'}</div></div>
                  <div><span className="text-gray-500">Lookback/Min Samples</span><div className="font-medium">{policyDraft?.behavioralLookbackDays ?? '-'}d / {policyDraft?.behavioralMinSamples ?? '-'}</div></div>
                  <div><span className="text-gray-500">Ratio/Cap</span><div className="font-medium">{policyDraft?.behavioralAmountRatioThreshold ?? '-'}x / {policyDraft?.behavioralContributionCap ?? '-'}</div></div>
                </div>
                <div className="text-[11px] text-gray-500">Recent versions: {policyVersions.map((p: any) => `v${p.versionNumber}`).join(', ') || 'n/a'}</div>
              </div>
            ) : <p className="text-xs text-gray-500">No policy loaded.</p>}
          </div>

          <div className="rounded border border-slate-200 bg-white p-3">
            <div className="flex items-center justify-between mb-2">
              <h4 className="text-sm font-semibold text-slate-900">Behavioral Baselines & Tuning (Phase 4)</h4>
              <button
                onClick={applySuggestedThresholds}
                disabled={!tuning?.suggestedThresholds || !policyDraft}
                className="rounded border border-indigo-200 px-2 py-1 text-xs font-semibold text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
              >
                Apply Suggested Thresholds
              </button>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs mb-3">
              <Metric label="Tuning Window" value={`${tuning?.windowDays ?? 45}d`} />
              <Metric label="Baseline Window" value={`${baselines?.windowDays ?? 60}d`} />
              <Metric label="Shadow Events" value={String(tuning?.behavioralSignalStats?.shadowEventCount ?? 0)} />
              <Metric label="Behav Cand Avg" value={tuning?.behavioralSignalStats?.candidateContributionAvg == null ? 'n/a' : fmtNum(tuning.behavioralSignalStats.candidateContributionAvg)} hint={tuning?.dataQuality?.behavioralContribution} />
            </div>

            <div className="rounded border border-gray-200 bg-slate-50 p-2 mb-3">
              <div className="text-xs font-semibold text-slate-800 mb-2">Threshold Tuning</div>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                <label className="text-xs text-gray-600">Warn
                  <input type="number" min={0} max={100} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.warnThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), warnThreshold: numOr(e.target.value, 0) }))} />
                </label>
                <label className="text-xs text-gray-600">Review
                  <input type="number" min={0} max={100} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.reviewThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), reviewThreshold: numOr(e.target.value, 0) }))} />
                </label>
                <label className="text-xs text-gray-600">Soft Hold
                  <input type="number" min={0} max={100} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.softHoldThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), softHoldThreshold: numOr(e.target.value, 0) }))} />
                </label>
                <label className="text-xs text-gray-600">Hard Hold
                  <input type="number" min={0} max={100} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.hardHoldThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), hardHoldThreshold: numOr(e.target.value, 0) }))} />
                </label>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2 mt-2">
                <label className="text-xs text-gray-600">Lookback Days
                  <input type="number" min={7} max={365} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralLookbackDays ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralLookbackDays: numOr(e.target.value, 45) }))} />
                </label>
                <label className="text-xs text-gray-600">Min Samples
                  <input type="number" min={3} max={200} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralMinSamples ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralMinSamples: numOr(e.target.value, 10) }))} />
                </label>
                <label className="text-xs text-gray-600">Amount Ratio Threshold
                  <input type="number" min={1.5} max={25} step={0.1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralAmountRatioThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralAmountRatioThreshold: numOr(e.target.value, 4) }))} />
                </label>
                <label className="text-xs text-gray-600">Contribution Cap
                  <input type="number" min={1} max={40} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralContributionCap ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralContributionCap: numOr(e.target.value, 18) }))} />
                </label>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-4 gap-2 mt-2">
                <label className="text-xs text-gray-600">Time Pattern Delta
                  <input type="number" min={0.05} max={1} step={0.01} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralTimePatternDeltaThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralTimePatternDeltaThreshold: numOr(e.target.value, 0.35) }))} />
                </label>
                <label className="text-xs text-gray-600">Reversal Delta
                  <input type="number" min={0.05} max={1} step={0.01} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                    value={policyDraft?.behavioralReversalRateDeltaThreshold ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralReversalRateDeltaThreshold: numOr(e.target.value, 0.2) }))} />
                </label>
                <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-gray-300 rounded px-2 py-2 bg-white">
                  <input type="checkbox" checked={Boolean(policyDraft?.behavioralSignalsEnabled)} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralSignalsEnabled: e.target.checked }))} />
                  Behavioral Enabled
                </label>
                <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-gray-300 rounded px-2 py-2 bg-white">
                  <input type="checkbox" checked={Boolean(policyDraft?.behavioralShadowMode)} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), behavioralShadowMode: e.target.checked }))} />
                  Shadow Mode
                </label>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-5 gap-2 mt-2">
                <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-gray-300 rounded px-2 py-2 bg-white">
                  <input type="checkbox" checked={Boolean(policyDraft?.opsAlertsEnabled)} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), opsAlertsEnabled: e.target.checked }))} />
                  Ops Alerts Enabled
                </label>
                {['in_app', 'outbox', 'webhook', 'email'].map((channel) => (
                  <label key={channel} className="inline-flex items-center gap-2 text-xs text-gray-700 border border-gray-300 rounded px-2 py-2 bg-white">
                    <input
                      type="checkbox"
                      checked={Array.isArray(policyDraft?.opsAlertChannels) && policyDraft.opsAlertChannels.includes(channel)}
                      onChange={e => setPolicyDraft((p: any) => {
                        const current = Array.isArray(p?.opsAlertChannels) ? [...p.opsAlertChannels] : [];
                        const next = e.target.checked
                          ? Array.from(new Set([...current, channel]))
                          : current.filter((v: string) => v !== channel);
                        return { ...(p || {}), opsAlertChannels: next };
                      })}
                    />
                    {channel}
                  </label>
                ))}
              </div>

              <div className="rounded border border-gray-200 bg-white p-2 mt-2">
                <div className="text-xs font-semibold text-slate-800 mb-2">Phase X2: Policy Templates + Yetki Ayrımı</div>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                  <label className="text-xs text-gray-600">Policy Template
                    <select
                      className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={String(policyDraft?.templateKey || 'balanced')}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), templateKey: e.target.value }))}
                    >
                      <option value="balanced">balanced</option>
                      <option value="conservative">conservative</option>
                      <option value="aggressive">aggressive</option>
                    </select>
                  </label>
                  <button
                    type="button"
                    onClick={() => applyPolicyTemplate(String(policyDraft?.templateKey || 'balanced'))}
                    className="self-end rounded border border-indigo-200 px-2 py-1.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-50"
                  >
                    Apply Template to Draft
                  </button>
                  <div className="self-end text-[11px] text-gray-500">
                    {policyTemplates.length > 0 ? `${policyTemplates.length} templates loaded` : 'Template defaults available'}
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-2 mt-2">
                  <label className="text-xs text-gray-600">Hold Escalation SLA (minutes)
                    <input type="number" min={0} max={10080} step={5} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={policyDraft?.holdEscalationSlaMinutes ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), holdEscalationSlaMinutes: numOr(e.target.value, 120) }))} />
                  </label>
                  <label className="text-xs text-gray-600">Soft Hold Expiry (hours)
                    <input type="number" min={0} max={8760} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={policyDraft?.softHoldExpiryHours ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), softHoldExpiryHours: numOr(e.target.value, 24) }))} />
                  </label>
                  <label className="text-xs text-gray-600">Hard Hold Expiry (hours)
                    <input type="number" min={0} max={8760} step={1} className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={policyDraft?.hardHoldExpiryHours ?? ''} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), hardHoldExpiryHours: numOr(e.target.value, 72) }))} />
                  </label>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
                  <div className="rounded border border-gray-200 p-2">
                    <div className="text-[11px] font-semibold text-gray-700 mb-1">Escalate Roles (Security / Override)</div>
                    <div className="flex flex-wrap gap-2">
                      {['SecurityAdmin', 'Admin', 'FinanceAdmin'].map((role) => (
                        <label key={`ovr-${role}`} className="inline-flex items-center gap-1 text-[11px] text-gray-700">
                          <input
                            type="checkbox"
                            checked={Array.isArray(policyDraft?.overrideRoles) && policyDraft.overrideRoles.includes(role)}
                            onChange={e => setPolicyDraft((p: any) => {
                              const current = Array.isArray(p?.overrideRoles) ? [...p.overrideRoles] : ['SecurityAdmin', 'Admin'];
                              const next = e.target.checked ? Array.from(new Set([...current, role])) : current.filter((v: string) => v !== role);
                              return { ...(p || {}), overrideRoles: next };
                            })}
                          />
                          {role}
                        </label>
                      ))}
                    </div>
                  </div>
                  <div className="rounded border border-gray-200 p-2">
                    <div className="text-[11px] font-semibold text-gray-700 mb-1">Release Roles (Finance / Release)</div>
                    <div className="flex flex-wrap gap-2">
                      {['FinanceAdmin', 'Admin', 'SecurityAdmin'].map((role) => (
                        <label key={`rel-${role}`} className="inline-flex items-center gap-1 text-[11px] text-gray-700">
                          <input
                            type="checkbox"
                            checked={Array.isArray(policyDraft?.releaseRoles) && policyDraft.releaseRoles.includes(role)}
                            onChange={e => setPolicyDraft((p: any) => {
                              const current = Array.isArray(p?.releaseRoles) ? [...p.releaseRoles] : ['FinanceAdmin', 'Admin'];
                              const next = e.target.checked ? Array.from(new Set([...current, role])) : current.filter((v: string) => v !== role);
                              return { ...(p || {}), releaseRoles: next };
                            })}
                          />
                          {role}
                        </label>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
                  <div className="rounded border border-gray-200 p-2">
                    <label className="inline-flex items-center gap-2 text-xs text-gray-700">
                      <input type="checkbox" checked={Boolean(policyDraft?.criticalDualApprovalEnabled)} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), criticalDualApprovalEnabled: e.target.checked }))} />
                      Critical hard-hold dual approval
                    </label>
                    <div className="mt-2 text-[11px] font-semibold text-gray-700 mb-1">Secondary Approver Roles (Critical)</div>
                    <div className="flex flex-wrap gap-2">
                      {['SecurityAdmin', 'Admin', 'FinanceAdmin'].map((role) => (
                        <label key={`dual-${role}`} className="inline-flex items-center gap-1 text-[11px] text-gray-700">
                          <input
                            type="checkbox"
                            checked={Array.isArray(policyDraft?.criticalDualApprovalSecondaryRoles) && policyDraft.criticalDualApprovalSecondaryRoles.includes(role)}
                            onChange={e => setPolicyDraft((p: any) => {
                              const current = Array.isArray(p?.criticalDualApprovalSecondaryRoles) ? [...p.criticalDualApprovalSecondaryRoles] : ['SecurityAdmin', 'Admin'];
                              const next = e.target.checked ? Array.from(new Set([...current, role])) : current.filter((v: string) => v !== role);
                              return { ...(p || {}), criticalDualApprovalSecondaryRoles: next };
                            })}
                          />
                          {role}
                        </label>
                      ))}
                    </div>
                  </div>
                  <div className="rounded border border-gray-200 p-2">
                    <label className="inline-flex items-center gap-2 text-xs text-gray-700">
                      <input type="checkbox" checked={Boolean(policyDraft?.requireCriticalThresholdChangeReview)} onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), requireCriticalThresholdChangeReview: e.target.checked }))} />
                      Critical threshold changes require review
                    </label>
                    <input
                      className="mt-2 w-full rounded border border-gray-300 p-1.5 text-xs"
                      placeholder="Threshold change reason (required when critical thresholds change)"
                      value={policyDraft?.criticalThresholdChangeReason || ''}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), criticalThresholdChangeReason: e.target.value }))}
                    />
                    <div className="mt-1 text-[10px] text-gray-500">Lowering review/hold thresholds may require this reason.</div>
                  </div>
                </div>

                <div className="mt-2">
                  <label className="text-xs text-gray-600 block mb-1">Trust Account Policy Overrides (JSON object keyed by trustAccountId)</label>
                  <textarea
                    className="w-full rounded border border-gray-300 p-2 text-[11px] font-mono h-28"
                    value={String(policyDraft?.trustAccountOverridesJson || '{}')}
                    onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), trustAccountOverridesJson: e.target.value }))}
                    placeholder={'{\n  \"trust-account-id\": {\n    \"reviewThreshold\": 70,\n    \"hardHoldThreshold\": 98,\n    \"releaseRoles\": [\"FinanceAdmin\"],\n    \"criticalDualApprovalEnabled\": true\n  }\n}'}
                  />
                </div>
              </div>

              <div className="rounded border border-rose-100 bg-rose-50 p-2 mt-2">
                <div className="text-xs font-semibold text-rose-900 mb-2">Phase X4: Enforcement Maturity (Strict Preflight / Grace / Fail Mode)</div>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                  <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-rose-200 rounded px-2 py-2 bg-white">
                    <input
                      type="checkbox"
                      checked={Boolean(policyDraft?.preflightStrictModeEnabled)}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightStrictModeEnabled: e.target.checked }))}
                    />
                    Strict preflight enabled (tenant)
                  </label>
                  <label className="text-xs text-gray-600">Grace / Rollout Mode
                    <select
                      className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={String(policyDraft?.preflightStrictRolloutMode || 'warn')}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightStrictRolloutMode: e.target.value }))}
                    >
                      <option value="warn">warn (grace)</option>
                      <option value="soft_hold">soft_hold (grace)</option>
                      <option value="strict">strict</option>
                    </select>
                  </label>
                  <label className="text-xs text-gray-600">Recent Event Window (min)
                    <input
                      type="number"
                      min={1}
                      max={1440}
                      step={1}
                      className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={policyDraft?.preflightRecentEventWindowMinutes ?? ''}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightRecentEventWindowMinutes: numOr(e.target.value, 30) }))}
                    />
                  </label>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2 mt-2">
                  <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-rose-200 rounded px-2 py-2 bg-white">
                    <input
                      type="checkbox"
                      checked={Boolean(policyDraft?.preflightHighConfidenceOnly)}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightHighConfidenceOnly: e.target.checked }))}
                    />
                    Preflight only high-confidence combos
                  </label>
                  <label className="text-xs text-gray-600">Preflight Min Severity
                    <select
                      className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs"
                      value={String(policyDraft?.preflightMinSeverity || 'critical')}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightMinSeverity: e.target.value }))}
                    >
                      <option value="critical">critical</option>
                      <option value="high">high</option>
                      <option value="medium">medium</option>
                    </select>
                  </label>
                  <label className="inline-flex items-center gap-2 text-xs text-gray-700 border border-rose-200 rounded px-2 py-2 bg-white">
                    <input
                      type="checkbox"
                      checked={Boolean(policyDraft?.preflightDuplicateSuppressionEnabled)}
                      onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), preflightDuplicateSuppressionEnabled: e.target.checked }))}
                    />
                    Hold duplicate suppression enabled
                  </label>
                </div>
                <div className="mt-2">
                  <label className="text-xs text-gray-600 block mb-1">Operation Fail Modes (JSON; fail_open / fail_closed)</label>
                  <textarea
                    className="w-full rounded border border-gray-300 p-2 text-[11px] font-mono h-24"
                    value={String(policyDraft?.operationFailModesJson || '{}')}
                    onChange={e => setPolicyDraft((p: any) => ({ ...(p || {}), operationFailModesJson: e.target.value }))}
                    placeholder={'{\n  \"manual_ledger_post\": \"fail_open\",\n  \"ledger_reversal\": \"fail_open\",\n  \"payment_allocation_apply\": \"fail_closed\",\n  \"payment_allocation_reverse\": \"fail_open\"\n}'}
                  />
                  <div className="mt-1 text-[10px] text-rose-800">
                    Strict preflight blocks only in <span className="font-semibold">strict</span> rollout mode. Grace modes observe/log without same-operation blocking.
                  </div>
                </div>
              </div>

              <div className="mt-2 flex items-center justify-between gap-2">
                <div className="text-[11px] text-gray-500">
                  Suggested: W/R/S/H = {tuning?.suggestedThresholds ? `${tuning.suggestedThresholds.warn}/${tuning.suggestedThresholds.review}/${tuning.suggestedThresholds.softHold}/${tuning.suggestedThresholds.hardHold}` : 'n/a'} ({tuning?.dataQuality?.thresholdSuggestion || 'heuristic'})
                </div>
                <button
                  onClick={() => void savePolicyTuning()}
                  disabled={!policyDraft || busyKey === 'policy-save'}
                  className="rounded border border-emerald-200 px-2 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                >
                  {busyKey === 'policy-save' ? 'Saving...' : 'Save Policy Tuning'}
                </button>
              </div>

              <div className="rounded border border-indigo-100 bg-indigo-50 p-2 mt-2">
                <div className="flex items-center justify-between gap-2 mb-2">
                  <div className="text-xs font-semibold text-indigo-900">Phase X3: Threshold Impact Simulation</div>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => void runThresholdImpactSimulation(30)}
                      disabled={!policyDraft || busyKey === 'impact:30'}
                      className="rounded border border-indigo-200 px-2 py-1 text-xs font-semibold text-indigo-700 hover:bg-white disabled:opacity-50"
                    >
                      {busyKey === 'impact:30' ? 'Running...' : 'Run 30d'}
                    </button>
                    <button
                      type="button"
                      onClick={() => void runThresholdImpactSimulation(60)}
                      disabled={!policyDraft || busyKey === 'impact:60'}
                      className="rounded border border-indigo-200 px-2 py-1 text-xs font-semibold text-indigo-700 hover:bg-white disabled:opacity-50"
                    >
                      {busyKey === 'impact:60' ? 'Running...' : 'Run 60d'}
                    </button>
                  </div>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2 text-[11px]">
                  {[impact30, impact60].map((impact, idx) => (
                    <div key={idx} className="rounded border border-indigo-100 bg-white p-2">
                      <div className="font-semibold text-indigo-900">{impact ? `${impact.windowDays}d Impact` : (idx === 0 ? '30d Impact' : '60d Impact')}</div>
                      {!impact ? (
                        <div className="text-gray-500 mt-1">Not run yet.</div>
                      ) : (
                        <div className="space-y-1 mt-1 text-gray-700">
                          <div>Total events: {impact.totalEvents ?? 0}</div>
                          <div>Changed: {impact?.impact?.changedCount ?? 0} ({impact?.impact?.changedPct ?? 0}%)</div>
                          <div>Escalated: {impact?.impact?.escalatedCount ?? 0} | Relaxed: {impact?.impact?.relaxedCount ?? 0}</div>
                          <div className="text-gray-500 break-all">Before: {JSON.stringify(impact?.simulatedBefore || {})}</div>
                          <div className="text-gray-500 break-all">After: {JSON.stringify(impact?.simulatedAfter || {})}</div>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>

              <div className="rounded border border-slate-200 bg-white p-2 mt-2">
                <div className="text-xs font-semibold text-slate-800 mb-2">Phase X3: Policy Version Compare (Before/After)</div>
                <div className="grid grid-cols-1 md:grid-cols-4 gap-2">
                  <label className="text-xs text-gray-600">Window (days)
                    <select className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs" value={String(compareDays)} onChange={e => setCompareDays(numOr(e.target.value, 60))}>
                      <option value="30">30</option>
                      <option value="60">60</option>
                      <option value="90">90</option>
                    </select>
                  </label>
                  <label className="text-xs text-gray-600">From Version
                    <select className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs" value={String(compareFromVersion)} onChange={e => setCompareFromVersion(e.target.value ? Number(e.target.value) : '')}>
                      <option value="">auto</option>
                      {policyVersions.map((p: any) => (
                        <option key={`cmp-from-${p.id}`} value={String(p.versionNumber)}>{`v${p.versionNumber}${p.isActive ? ' (active)' : ''}`}</option>
                      ))}
                    </select>
                  </label>
                  <label className="text-xs text-gray-600">To Version
                    <select className="mt-1 w-full rounded border border-gray-300 p-1.5 text-xs" value={String(compareToVersion)} onChange={e => setCompareToVersion(e.target.value ? Number(e.target.value) : '')}>
                      <option value="">auto</option>
                      {policyVersions.map((p: any) => (
                        <option key={`cmp-to-${p.id}`} value={String(p.versionNumber)}>{`v${p.versionNumber}${p.isActive ? ' (active)' : ''}`}</option>
                      ))}
                    </select>
                  </label>
                  <button
                    type="button"
                    onClick={() => void runPolicyVersionCompare()}
                    disabled={busyKey === 'policy-compare' || policyVersions.length === 0}
                    className="self-end rounded border border-slate-300 px-2 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                  >
                    {busyKey === 'policy-compare' ? 'Comparing...' : 'Compare Versions'}
                  </button>
                </div>
                <div className="mt-2 text-[11px] text-gray-700">
                  {policyImpactCompare ? (
                    <div className="space-y-1">
                      <div>v{policyImpactCompare?.fromPolicy?.versionNumber ?? '?'} {'->'} v{policyImpactCompare?.toPolicy?.versionNumber ?? '?'} | changed={policyImpactCompare?.impact?.changedCount ?? 0} | escalated={policyImpactCompare?.impact?.escalatedCount ?? 0} | relaxed={policyImpactCompare?.impact?.relaxedCount ?? 0}</div>
                      <div className="text-gray-500 break-all">Before: {JSON.stringify(policyImpactCompare?.beforeDecisionCounts || {})}</div>
                      <div className="text-gray-500 break-all">After: {JSON.stringify(policyImpactCompare?.afterDecisionCounts || {})}</div>
                    </div>
                  ) : (
                    <div className="text-gray-500">No comparison run yet.</div>
                  )}
                </div>
              </div>
            </div>

            <div className="grid grid-cols-1 gap-2">
              <div className="rounded border border-gray-200 bg-white p-2">
                <div className="text-xs font-semibold text-slate-800 mb-1">Tenant Baselines</div>
                <div className="space-y-1">
                  {(baselines?.tenantBaselines || []).length === 0 ? (
                    <div className="text-[11px] text-gray-500">No tenant baseline data yet.</div>
                  ) : (baselines?.tenantBaselines || []).map((b: any) => (
                    <div key={`${b.scopeType}:${b.scopeId}`} className="text-[11px] text-gray-700 flex flex-wrap gap-x-3 gap-y-1">
                      <span className="font-medium">{b.scopeType}</span>
                      <span>n={b.sampleCount}</span>
                      <span>avg=${fmtNum(b.averageAbsoluteAmount)}</span>
                      <span>off-hours {fmtNum((Number(b.offHoursRate || 0) * 100), 0)}%</span>
                      <span>reversal {fmtNum((Number(b.reversalRate || 0) * 100), 0)}%</span>
                    </div>
                  ))}
                </div>
              </div>
              <div className="rounded border border-gray-200 bg-white p-2">
                <div className="text-xs font-semibold text-slate-800 mb-1">Top Trust Account Baselines</div>
                <div className="space-y-1 max-h-28 overflow-auto">
                  {(baselines?.trustAccountBaselines || []).length === 0 ? (
                    <div className="text-[11px] text-gray-500">No trust-account baselines yet.</div>
                  ) : (baselines?.trustAccountBaselines || []).map((b: any) => (
                    <div key={`${b.scopeType}:${b.scopeId}`} className="text-[11px] text-gray-700 break-all">
                      <span className="font-medium">{b.scopeId}</span> · n={b.sampleCount} · avg=${fmtNum(b.averageAbsoluteAmount)} · off-hours {fmtNum((Number(b.offHoursRate || 0) * 100), 0)}% · reversal {fmtNum((Number(b.reversalRate || 0) * 100), 0)}%
                    </div>
                  ))}
                </div>
              </div>
              <div className="rounded border border-gray-200 bg-white p-2">
                <div className="text-xs font-semibold text-slate-800 mb-1">Behavioral Rule-level Tuning Suggestions (X3)</div>
                <div className="space-y-1 max-h-36 overflow-auto">
                  {Array.isArray(tuning?.behavioralSignalStats?.ruleLevelSuggestions) && tuning.behavioralSignalStats.ruleLevelSuggestions.length > 0 ? (
                    tuning.behavioralSignalStats.ruleLevelSuggestions.slice(0, 10).map((row: any) => (
                      <div key={String(row?.ruleCode || 'rule')} className="text-[11px] text-gray-700">
                        <span className="font-medium">{row?.ruleCode || 'rule'}</span> | obs={row?.observations ?? 0} | FP%={row?.falsePositiveRatePct == null ? 'n/a' : `${fmtNum(row.falsePositiveRatePct, 1)}%`} | Precision={row?.precisionProxyPct == null ? 'n/a' : `${fmtNum(row.precisionProxyPct, 1)}%`} | burden={row?.burdenScore == null ? 'n/a' : fmtNum(row.burdenScore, 1)} | {row?.suggestion || 'monitor'}
                      </div>
                    ))
                  ) : (
                    <div className="text-[11px] text-gray-500">No behavioral rule-level suggestions yet.</div>
                  )}
                </div>
              </div>
              <div className="rounded border border-gray-200 bg-white p-2">
                <div className="text-xs font-semibold text-slate-800 mb-1">Rule-level False Positive Counters</div>
                <div className="space-y-1 max-h-28 overflow-auto">
                  {metrics?.ruleCounters && Object.keys(metrics.ruleCounters).length > 0 ? Object.entries(metrics.ruleCounters).slice(0, 10).map(([code, row]: [string, any]) => (
                    <div key={code} className="text-[11px] text-gray-700">
                      <span className="font-medium">{code}</span> | total={row.total} | FP={row.falsePositive} | TP={row.truePositive} | AE={row.acceptableException} | FP%={row.falsePositiveRatePct == null ? 'n/a' : `${fmtNum(row.falsePositiveRatePct, 1)}%`} | Precision={row.precisionProxyPct == null ? 'n/a' : `${fmtNum(row.precisionProxyPct, 1)}%`} | Burden={row.burdenScore == null ? 'n/a' : fmtNum(row.burdenScore, 1)}
                    </div>
                  )) : (
                    <div className="text-[11px] text-gray-500">No labeled rule counter data yet.</div>
                  )}
                </div>
              </div>
            </div>
          </div>

          <div className="rounded border border-slate-200 bg-white p-3 min-h-[720px]">
            <div className="flex items-center justify-between mb-2">
              <h4 className="text-sm font-semibold text-slate-900">Explanation / Drill-down</h4>
              {selectedEventDetail?.riskEvent ? (
                <button
                  onClick={() => void rescoreEvent(selectedEventDetail.riskEvent.id)}
                  disabled={busyKey === `rescore:${selectedEventDetail.riskEvent.id}`}
                  className="rounded border border-purple-200 px-2 py-1 text-xs font-semibold text-purple-700 hover:bg-purple-50 disabled:opacity-50"
                >
                  {busyKey === `rescore:${selectedEventDetail.riskEvent.id}` ? 'Rescoring...' : 'Re-score Event'}
                </button>
              ) : null}
            </div>
            {!selectedEventDetail?.riskEvent ? (
              <p className="text-xs text-gray-500">Select an event to inspect reasons, evidence and related records.</p>
            ) : (
              <div className="space-y-3">
                <div className="rounded border border-gray-200 bg-slate-50 p-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`inline-flex rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(selectedEventDetail.riskEvent.severity)}`}>{selectedEventDetail.riskEvent.severity}</span>
                    <span className={`inline-flex rounded border px-1.5 py-0.5 text-[10px] font-semibold ${badge(selectedEventDetail.riskEvent.decision)}`}>{selectedEventDetail.riskEvent.decision}</span>
                    <span className="text-xs text-gray-600">Score {fmtNum(selectedEventDetail.riskEvent.riskScore)}</span>
                  </div>
                  <div className="mt-1 text-[11px] text-gray-600 break-all">{selectedEventDetail.riskEvent.id}</div>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {newEventIds.includes(String(selectedEventDetail.riskEvent.id)) ? (
                      <span className="rounded border border-cyan-200 bg-cyan-100 px-2 py-1 text-[11px] font-semibold text-cyan-800">NEW since refresh</span>
                    ) : null}
                    {selectedEventDetail.riskEvent.matterId ? (
                      <button onClick={() => applyDrilldown('matter', selectedEventDetail.riskEvent.matterId)} className="rounded border border-gray-300 px-2 py-1 text-[11px] text-slate-700 hover:bg-white">
                        matter:{selectedEventDetail.riskEvent.matterId}
                      </button>
                    ) : null}
                    {selectedEventDetail.riskEvent.clientId ? (
                      <button onClick={() => applyDrilldown('client', selectedEventDetail.riskEvent.clientId)} className="rounded border border-gray-300 px-2 py-1 text-[11px] text-slate-700 hover:bg-white">
                        client:{selectedEventDetail.riskEvent.clientId}
                      </button>
                    ) : null}
                    {trustAccountId ? (
                      <span className="rounded border border-gray-300 px-2 py-1 text-[11px] text-slate-700 bg-white">trustAccount:{trustAccountId}</span>
                    ) : null}
                  </div>
                </div>

                <div>
                  <h5 className="text-xs font-semibold text-gray-700 mb-1">Triage Actions (Pilot Hardening)</h5>
                  <div className="rounded border border-gray-200 bg-white p-2 space-y-2">
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                      <input className="rounded border border-gray-300 p-1.5 text-xs" placeholder="Ack note (optional)" value={ackNote} onChange={e => setAckNote(e.target.value)} />
                      <button
                        onClick={() => void acknowledgeSelectedEvent()}
                        disabled={busyKey === `ack:${selectedEventDetail.riskEvent.id}`}
                        className="rounded border border-blue-200 px-2 py-1 text-xs font-semibold text-blue-700 hover:bg-blue-50 disabled:opacity-50"
                      >
                        {busyKey === `ack:${selectedEventDetail.riskEvent.id}` ? '...' : 'Acknowledge'}
                      </button>
                      <div className="text-[11px] text-gray-500 flex items-center">Audit logged</div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
                      <input className="rounded border border-gray-300 p-1.5 text-xs" placeholder="Assign to user id" value={assignUserId} onChange={e => setAssignUserId(e.target.value)} />
                      <input className="rounded border border-gray-300 p-1.5 text-xs" placeholder="Assignment note (optional)" value={assignNote} onChange={e => setAssignNote(e.target.value)} />
                      <button
                        onClick={() => void assignSelectedEvent()}
                        disabled={busyKey === `assign:${selectedEventDetail.riskEvent.id}`}
                        className="rounded border border-indigo-200 px-2 py-1 text-xs font-semibold text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
                      >
                        {busyKey === `assign:${selectedEventDetail.riskEvent.id}` ? '...' : 'Assign'}
                      </button>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-4 gap-2">
                      <select className="rounded border border-gray-300 p-1.5 text-xs" value={reviewDisposition} onChange={e => setReviewDisposition(e.target.value as any)}>
                        <option value="true_positive">true_positive</option>
                        <option value="false_positive">false_positive</option>
                        <option value="acceptable_exception">acceptable_exception</option>
                      </select>
                      <input className="rounded border border-gray-300 p-1.5 text-xs" placeholder="Disposition reason (required)" value={reviewDispositionReason} onChange={e => setReviewDispositionReason(e.target.value)} />
                      <input className="rounded border border-gray-300 p-1.5 text-xs" placeholder="Approver reason (required)" value={reviewDispositionApproverReason} onChange={e => setReviewDispositionApproverReason(e.target.value)} />
                      <button
                        onClick={() => void submitReviewDisposition()}
                        disabled={busyKey === `disposition:${selectedEventDetail.riskEvent.id}`}
                        className="rounded border border-emerald-200 px-2 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-50 disabled:opacity-50"
                      >
                        {busyKey === `disposition:${selectedEventDetail.riskEvent.id}` ? '...' : 'Set Disposition'}
                      </button>
                    </div>
                  </div>
                </div>

                <div>
                  <h5 className="text-xs font-semibold text-gray-700 mb-1">Reasons (explainable)</h5>
                  <div className="space-y-1">
                    {Array.isArray(parsedReasons) && parsedReasons.length > 0 ? parsedReasons.map((r: any, idx: number) => (
                      <div key={`${r?.code || 'reason'}:${idx}`} className="rounded border border-gray-200 bg-white p-2 text-xs">
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-medium text-slate-800">{r?.code || 'reason'}</span>
                          <span className="text-gray-500">w={r?.weight ?? '-'}</span>
                        </div>
                        <div className="text-gray-600 mt-1">{r?.message || JSON.stringify(r)}</div>
                      </div>
                    )) : <p className="text-xs text-gray-500">No parsed reasons.</p>}
                  </div>
                </div>

                <div className="grid grid-cols-1 gap-3">
                  {parsedFeatures?.behavioral ? (
                    <div className="rounded border border-indigo-200 bg-indigo-50 p-2">
                      <div className="text-xs font-semibold text-indigo-900">Behavioral Contribution (Phase 4)</div>
                      <div className="mt-1 grid grid-cols-2 gap-2 text-[11px] text-indigo-900">
                        <div>Mode: <span className="font-medium">{parsedFeatures.behavioral.shadowMode ? 'shadow' : 'active'}</span></div>
                        <div>Scope: <span className="font-medium">{parsedFeatures.behavioral.baselineScope || 'n/a'}</span></div>
                        <div>Candidate: <span className="font-medium">{fmtNum(parsedFeatures.behavioral.candidateContribution ?? 0)}</span></div>
                        <div>Applied: <span className="font-medium">{fmtNum(parsedFeatures.behavioral.appliedContribution ?? 0)}</span></div>
                        <div>Samples: <span className="font-medium">{parsedFeatures.behavioral.baselineSampleCount ?? 'n/a'}</span></div>
                        <div>Lookback: <span className="font-medium">{parsedFeatures.behavioral.lookbackDays ?? 'n/a'}d</span></div>
                      </div>
                      {Array.isArray(parsedFeatures?.behavioral?.components) && parsedFeatures.behavioral.components.length > 0 ? (
                        <div className="mt-2 space-y-1">
                          {parsedFeatures.behavioral.components.map((c: any, idx: number) => (
                            <div key={`${c?.code || 'component'}:${idx}`} className="text-[11px] text-indigo-800">
                              <span className="font-medium">{c?.code}</span> · cand={fmtNum(c?.candidateWeight ?? 0)} · applied={fmtNum(c?.appliedWeight ?? 0)}
                            </div>
                          ))}
                        </div>
                      ) : null}
                    </div>
                  ) : null}
                  <details className="rounded border border-gray-200 bg-white p-2">
                    <summary className="cursor-pointer text-xs font-semibold text-gray-700">Evidence</summary>
                    <pre className="mt-2 text-[11px] text-slate-700 whitespace-pre-wrap break-all">{JSON.stringify(parsedEvidence, null, 2)}</pre>
                  </details>
                  <details className="rounded border border-gray-200 bg-white p-2">
                    <summary className="cursor-pointer text-xs font-semibold text-gray-700">Features</summary>
                    <pre className="mt-2 text-[11px] text-slate-700 whitespace-pre-wrap break-all">{JSON.stringify(parsedFeatures, null, 2)}</pre>
                  </details>
                  <details className="rounded border border-gray-200 bg-white p-2">
                    <summary className="cursor-pointer text-xs font-semibold text-gray-700">Related Records (drill-down)</summary>
                    <pre className="mt-2 text-[11px] text-slate-700 whitespace-pre-wrap break-all">{JSON.stringify(selectedEventDetail.related || {}, null, 2)}</pre>
                  </details>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </section>
  );
};

export default TrustRiskRadarPanel;
