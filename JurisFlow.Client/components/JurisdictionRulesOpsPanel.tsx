import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../services/api';
import { toast } from './Toast';

type Props = {
  refreshKey?: string;
};

const fmt = (v?: string | null) => (v ? new Date(v).toLocaleString('en-US') : '-');
const short = (v?: string | null, n = 160) => (!v ? '' : v.length <= n ? v : `${v.slice(0, n)}...`);
const pretty = (v?: string | null) => {
  if (!v) return '';
  try {
    return JSON.stringify(JSON.parse(v), null, 2);
  } catch {
    return v;
  }
};

const JurisdictionRulesOpsPanel: React.FC<Props> = ({ refreshKey }) => {
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [jurisdictions, setJurisdictions] = useState<any[]>([]);
  const [coverage, setCoverage] = useState<any[]>([]);
  const [rulePacks, setRulePacks] = useState<any[]>([]);
  const [changes, setChanges] = useState<any[]>([]);
  const [validationRuns, setValidationRuns] = useState<any[]>([]);
  const [historicalKpis, setHistoricalKpis] = useState<any | null>(null);
  const [harnessResult, setHarnessResult] = useState<any | null>(null);
  const [resolveResult, setResolveResult] = useState<any | null>(null);

  const [filters, setFilters] = useState({
    jurisdictionCode: '',
    caseType: '',
    supportLevel: '',
    status: ''
  });

  const [resolveDraft, setResolveDraft] = useState({
    jurisdictionCode: '',
    courtSystem: '',
    courtDivision: '',
    venue: '',
    caseType: '',
    filingMethod: 'e_filing'
  });

  const [harnessDraft, setHarnessDraft] = useState({
    jurisdictionCode: '',
    courtSystem: '',
    caseType: '',
    filingMethod: 'e_filing',
    rulePackId: '',
    limit: 50
  });

  const [transitionNotes, setTransitionNotes] = useState<Record<string, string>>({});

  const loadAll = async () => {
    setLoading(true);
    try {
      const [j, c, p, ch, vr, kpi] = await Promise.all([
        api.jurisdictionRules.getJurisdictions({ activeOnly: true }),
        api.jurisdictionRules.getCoverage({
          jurisdictionCode: filters.jurisdictionCode || undefined,
          caseType: filters.caseType || undefined,
          supportLevel: filters.supportLevel || undefined,
          activeOnly: !filters.status || filters.status === 'active',
          limit: 200
        }),
        api.jurisdictionRules.getRulePacks({
          jurisdictionCode: filters.jurisdictionCode || undefined,
          caseType: filters.caseType || undefined,
          status: filters.status || undefined,
          limit: 200
        }),
        api.jurisdictionRules.getChanges({
          jurisdictionCode: filters.jurisdictionCode || undefined,
          status: filters.status || undefined,
          limit: 100
        }),
        api.jurisdictionRules.getValidationRuns({ limit: 50 }),
        api.integrationsOps.getKpiAnalytics({ days: 90, bucket: 'week' })
      ]);

      setJurisdictions(Array.isArray(j) ? j : []);
      setCoverage(Array.isArray(c) ? c : []);
      setRulePacks(Array.isArray(p) ? p : []);
      setChanges(Array.isArray(ch) ? ch : []);
      setValidationRuns(Array.isArray(vr) ? vr : []);
      setHistoricalKpis(kpi || null);
    } catch (error) {
      console.error(error);
      toast.error('Failed to load jurisdiction rules ops data.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  const coverageStats = useMemo(() => {
    const bySupport = coverage.reduce<Record<string, number>>((acc, row) => {
      const key = (row.supportLevel || 'unknown').toLowerCase();
      acc[key] = (acc[key] || 0) + 1;
      return acc;
    }, {});
    const lowConfidence = coverage.filter(r => (r.confidenceLevel || '').toLowerCase() === 'low' || Number(r.confidenceScore || 0) < 0.75).length;
    return {
      total: coverage.length,
      supported: (bySupport.full || 0) + (bySupport.partial || 0),
      planned: bySupport.planned || 0,
      none: bySupport.none || 0,
      lowConfidence
    };
  }, [coverage]);

  const validationStats = useMemo(() => {
    const latest = validationRuns[0];
    const total = validationRuns.reduce((s, r) => s + Number(r.totalCases || 0), 0);
    const failed = validationRuns.reduce((s, r) => s + Number(r.failedCases || 0), 0);
    return {
      runs: validationRuns.length,
      totalCases: total,
      failedCases: failed,
      passRate: total > 0 ? `${(((total - failed) / total) * 100).toFixed(1)}%` : '-',
      lastRunAt: latest?.createdAt || null
    };
  }, [validationRuns]);

  const historicalCourt = historicalKpis?.courtValidation?.summary;
  const historicalFiling = historicalKpis?.filingRejection?.summary;
  const historicalRuleLead = historicalKpis?.ruleUpdateLeadTime?.summary;

  const handleRunHarness = async () => {
    setBusy('harness');
    try {
      const result = await api.jurisdictionRules.runValidationHarness({
        jurisdictionCode: harnessDraft.jurisdictionCode || undefined,
        courtSystem: harnessDraft.courtSystem || undefined,
        caseType: harnessDraft.caseType || undefined,
        filingMethod: harnessDraft.filingMethod || undefined,
        rulePackId: harnessDraft.rulePackId || undefined,
        limit: Number(harnessDraft.limit) || 50
      });
      setHarnessResult(result);
      toast.success('Validation harness completed.');
      await loadAll();
    } catch (error) {
      console.error(error);
      toast.error('Validation harness failed.');
    } finally {
      setBusy(null);
    }
  };

  const handleResolveCoverage = async () => {
    if (!resolveDraft.jurisdictionCode.trim()) {
      toast.error('Jurisdiction code is required.');
      return;
    }
    setBusy('resolve');
    try {
      const result = await api.jurisdictionRules.resolveCoverage({
        jurisdictionCode: resolveDraft.jurisdictionCode.trim(),
        courtSystem: resolveDraft.courtSystem || undefined,
        courtDivision: resolveDraft.courtDivision || undefined,
        venue: resolveDraft.venue || undefined,
        caseType: resolveDraft.caseType || undefined,
        filingMethod: resolveDraft.filingMethod || undefined
      });
      setResolveResult(result);
      toast.success('Coverage resolved.');
    } catch (error) {
      console.error(error);
      toast.error('Coverage resolve failed.');
    } finally {
      setBusy(null);
    }
  };

  const transitionRulePack = async (id: string, action: 'submit' | 'publish') => {
    setBusy(`${action}:${id}`);
    try {
      if (action === 'submit') {
        await api.jurisdictionRules.submitRulePackForReview(id, transitionNotes[id]);
      } else {
        await api.jurisdictionRules.publishRulePack(id, transitionNotes[id]);
      }
      toast.success(`Rule pack ${action === 'submit' ? 'submitted' : 'published'}.`);
      await loadAll();
    } catch (error) {
      console.error(error);
      toast.error(`Rule pack ${action} failed.`);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="mt-6 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="text-base font-bold text-slate-900">Jurisdiction Rules Ops</h3>
          <p className="text-xs text-gray-500">Coverage matrix, rule packs, rule diffs, validation harness, and resolve tester.</p>
        </div>
        <button onClick={loadAll} disabled={loading} className="px-3 py-1.5 text-xs font-semibold rounded border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-50">
          {loading ? 'Refreshing...' : 'Refresh'}
        </button>
      </div>

      <div className="mt-4 grid gap-2 md:grid-cols-6">
        <Metric label="Jurisdictions" value={String(jurisdictions.length)} />
        <Metric label="Coverage Rows" value={String(coverageStats.total)} />
        <Metric label="Supported" value={String(coverageStats.supported)} />
        <Metric label="Low Confidence" value={String(coverageStats.lowConfidence)} />
        <Metric label="Harness Pass Rate" value={validationStats.passRate} />
        <Metric label="Last Harness Run" value={validationStats.lastRunAt ? fmt(validationStats.lastRunAt) : '-'} />
      </div>

      <div className="mt-3 grid gap-2 md:grid-cols-4">
        <Metric label="Hist Court FP %" value={historicalCourt ? `${Number(historicalCourt.humanReviewFalsePositiveRatePct || 0).toFixed(2)}%` : '-'} />
        <Metric label="Hist Court FN %" value={historicalCourt ? `${Number(historicalCourt.humanReviewFalseNegativeRatePct || 0).toFixed(2)}%` : '-'} />
        <Metric label="Filing Reject %" value={historicalFiling ? `${Number(historicalFiling.rejectionRatePct || 0).toFixed(2)}%` : '-'} />
        <Metric label="Rule Lead P50 (h)" value={historicalRuleLead ? Number(historicalRuleLead.p50Hours || 0).toFixed(2) : '-'} />
      </div>

      <div className="mt-4 grid gap-3 md:grid-cols-4">
        <input list="jurisdiction-code-list" className="rounded border border-gray-300 p-2 text-sm" placeholder="Jurisdiction (CA / US-CA)" value={filters.jurisdictionCode} onChange={e => setFilters(prev => ({ ...prev, jurisdictionCode: e.target.value.toUpperCase() }))} />
        <datalist id="jurisdiction-code-list">
          {jurisdictions.slice(0, 200).map(j => (
            <option key={j.id || j.code || j.jurisdictionCode} value={j.code || j.jurisdictionCode || ''} />
          ))}
        </datalist>
        <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Case Type" value={filters.caseType} onChange={e => setFilters(prev => ({ ...prev, caseType: e.target.value }))} />
        <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Support Level (full/partial/...)" value={filters.supportLevel} onChange={e => setFilters(prev => ({ ...prev, supportLevel: e.target.value }))} />
        <div className="flex gap-2">
          <input className="flex-1 rounded border border-gray-300 p-2 text-sm" placeholder="Status (active/published/...)" value={filters.status} onChange={e => setFilters(prev => ({ ...prev, status: e.target.value }))} />
          <button onClick={loadAll} className="px-3 py-2 text-xs font-semibold rounded border border-gray-300 bg-white hover:bg-gray-50">Apply</button>
        </div>
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-2">
        <div className="rounded border border-gray-200 bg-white">
          <div className="border-b border-gray-200 px-3 py-2 text-sm font-semibold">Coverage Matrix</div>
          <div className="max-h-80 overflow-auto">
            {coverage.length === 0 ? <p className="p-3 text-sm text-gray-500">No coverage rows.</p> : coverage.slice(0, 80).map(row => (
              <div key={row.id} className="border-b border-gray-100 p-3 text-sm">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="font-semibold text-slate-800">{row.jurisdictionCode} | {row.courtSystem || 'court'} | {row.caseType || 'case'}</div>
                  <div className="text-xs text-gray-600">v{row.version} | {(row.supportLevel || '').toUpperCase()} | conf {Number(row.confidenceScore || 0).toFixed(2)}</div>
                </div>
                <div className="mt-1 text-xs text-gray-600">
                  {row.filingMethod || 'e_filing'} | {row.venue || 'venue:*'} | {row.confidenceLevel || 'n/a'}
                  {row.rulePackId ? ` | rulePack:${row.rulePackId}` : ' | no rule pack'}
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="rounded border border-gray-200 bg-white">
          <div className="border-b border-gray-200 px-3 py-2 text-sm font-semibold">Rule Change Diff Feed</div>
          <div className="max-h-80 overflow-auto">
            {changes.length === 0 ? <p className="p-3 text-sm text-gray-500">No change records.</p> : changes.slice(0, 50).map(change => (
              <details key={change.id} className="border-b border-gray-100 p-3">
                <summary className="cursor-pointer text-sm font-medium text-slate-800">
                  {change.jurisdictionCode} | {change.changeType} | {change.severity} | {fmt(change.createdAt)}
                </summary>
                <div className="mt-2 text-xs text-gray-700">{change.summary}</div>
                <div className="mt-1 text-xs text-gray-500">Status: {change.status} | RulePack: {change.rulePackId || '-'}</div>
                {change.diffJson && (
                  <pre className="mt-2 max-h-40 overflow-auto rounded bg-slate-900 p-2 text-[11px] text-slate-100 whitespace-pre-wrap">{pretty(change.diffJson)}</pre>
                )}
              </details>
            ))}
          </div>
        </div>
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-2">
        <div className="rounded border border-gray-200 bg-white p-3">
          <div className="mb-2 text-sm font-semibold">Rule Packs (Ops)</div>
          <div className="max-h-80 space-y-2 overflow-auto">
            {rulePacks.length === 0 ? <p className="text-sm text-gray-500">No rule packs.</p> : rulePacks.slice(0, 40).map(pack => (
              <div key={pack.id} className="rounded border border-gray-200 p-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-sm font-semibold text-slate-800">{pack.name}</div>
                  <div className="text-xs text-gray-600">{pack.jurisdictionCode} | v{pack.version} | {pack.status}</div>
                </div>
                <div className="mt-1 text-xs text-gray-600">
                  {pack.caseType || 'case:*'} | {pack.filingMethod || 'e_filing'} | conf {Number(pack.confidenceScore || 0).toFixed(2)}
                </div>
                <div className="mt-2 flex flex-wrap gap-2">
                  <input
                    className="min-w-[220px] flex-1 rounded border border-gray-300 px-2 py-1 text-xs"
                    placeholder="Transition notes"
                    value={transitionNotes[pack.id] || ''}
                    onChange={e => setTransitionNotes(prev => ({ ...prev, [pack.id]: e.target.value }))}
                  />
                  <button onClick={() => transitionRulePack(pack.id, 'submit')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-amber-300 bg-amber-50 text-amber-800 disabled:opacity-50">Submit Review</button>
                  <button onClick={() => transitionRulePack(pack.id, 'publish')} disabled={!!busy} className="px-2 py-1 text-xs font-semibold rounded border border-green-300 bg-green-50 text-green-800 disabled:opacity-50">Publish</button>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="space-y-4">
          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 text-sm font-semibold">Coverage Resolve Tester</div>
            <div className="grid gap-2 md:grid-cols-2">
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Jurisdiction Code*" value={resolveDraft.jurisdictionCode} onChange={e => setResolveDraft(p => ({ ...p, jurisdictionCode: e.target.value.toUpperCase() }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Court System" value={resolveDraft.courtSystem} onChange={e => setResolveDraft(p => ({ ...p, courtSystem: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Court Division" value={resolveDraft.courtDivision} onChange={e => setResolveDraft(p => ({ ...p, courtDivision: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Venue" value={resolveDraft.venue} onChange={e => setResolveDraft(p => ({ ...p, venue: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Case Type" value={resolveDraft.caseType} onChange={e => setResolveDraft(p => ({ ...p, caseType: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Filing Method" value={resolveDraft.filingMethod} onChange={e => setResolveDraft(p => ({ ...p, filingMethod: e.target.value }))} />
            </div>
            <div className="mt-2 flex justify-end">
              <button onClick={handleResolveCoverage} disabled={busy === 'resolve'} className="px-3 py-1.5 text-xs font-semibold rounded border border-blue-300 bg-blue-50 text-blue-800 disabled:opacity-50">
                {busy === 'resolve' ? 'Resolving...' : 'Resolve'}
              </button>
            </div>
            {resolveResult && (
              <pre className="mt-2 max-h-48 overflow-auto rounded bg-slate-900 p-2 text-[11px] text-slate-100 whitespace-pre-wrap">{JSON.stringify(resolveResult, null, 2)}</pre>
            )}
          </div>

          <div className="rounded border border-gray-200 bg-white p-3">
            <div className="mb-2 text-sm font-semibold">Validation Harness</div>
            <div className="grid gap-2 md:grid-cols-2">
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Jurisdiction Code" value={harnessDraft.jurisdictionCode} onChange={e => setHarnessDraft(p => ({ ...p, jurisdictionCode: e.target.value.toUpperCase() }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Court System" value={harnessDraft.courtSystem} onChange={e => setHarnessDraft(p => ({ ...p, courtSystem: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Case Type" value={harnessDraft.caseType} onChange={e => setHarnessDraft(p => ({ ...p, caseType: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Rule Pack Id" value={harnessDraft.rulePackId} onChange={e => setHarnessDraft(p => ({ ...p, rulePackId: e.target.value }))} />
              <input className="rounded border border-gray-300 p-2 text-sm" placeholder="Filing Method" value={harnessDraft.filingMethod} onChange={e => setHarnessDraft(p => ({ ...p, filingMethod: e.target.value }))} />
              <input type="number" className="rounded border border-gray-300 p-2 text-sm" placeholder="Limit" value={harnessDraft.limit} onChange={e => setHarnessDraft(p => ({ ...p, limit: Number(e.target.value || 50) }))} />
            </div>
            <div className="mt-2 flex justify-end">
              <button onClick={handleRunHarness} disabled={busy === 'harness'} className="px-3 py-1.5 text-xs font-semibold rounded border border-indigo-300 bg-indigo-50 text-indigo-800 disabled:opacity-50">
                {busy === 'harness' ? 'Running...' : 'Run Harness'}
              </button>
            </div>
            {harnessResult && (
              <div className="mt-2 rounded border border-gray-200 p-2 text-xs text-gray-700">
                Run `{harnessResult.runId}`: total {harnessResult.totalCases}, passed {harnessResult.passedCases}, failed {harnessResult.failedCases}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded border border-gray-200 bg-white p-2">
    <div className="text-[11px] text-gray-500">{label}</div>
    <div className="text-sm font-semibold text-slate-800">{value}</div>
  </div>
);

export default JurisdictionRulesOpsPanel;

