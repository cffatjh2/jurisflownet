import React, { useEffect, useMemo, useState } from 'react';
import { toast } from './Toast';
import { api } from '../services/api';
import {
  CanonicalIntegrationActionResult,
  IntegrationCanonicalContractDescriptor,
  IntegrationCapabilityMatrixRow,
  IntegrationConflictQueueItem,
  IntegrationInboxEventListItem,
  IntegrationMappingProfile,
  IntegrationOutboxEventListItem,
  IntegrationReviewQueueItem,
  IntegrationRunListItem,
  IntegrationSecretStoreStatus
} from '../types';

interface Props {
  refreshKey?: string;
}

type MappingDraft = {
  profileKey: string;
  name: string;
  entityType: string;
  direction: string;
  conflictPolicy: string;
  isDefault: boolean;
  fieldMappingsJson: string;
  enumMappingsJson: string;
  taxMappingsJson: string;
  accountMappingsJson: string;
};

type ActionDraft = {
  entityType: string;
  payloadJson: string;
  dryRun: boolean;
  requiresReview: boolean;
};

const fmt = (v?: string | null) => (v ? new Date(v).toLocaleString('en-US') : '-');
const pretty = (v?: string | null) => {
  if (!v) return '';
  try { return JSON.stringify(JSON.parse(v), null, 2); } catch { return v; }
};
const jsonOrNull = (v: string) => {
  const t = v.trim();
  if (!t) return null;
  return JSON.stringify(JSON.parse(t));
};

const defaultActionDraft = (): ActionDraft => ({ entityType: '', payloadJson: '', dryRun: false, requiresReview: true });
const defaultMappingDraft = (contract?: IntegrationCanonicalContractDescriptor | null): MappingDraft => ({
  profileKey: 'invoice-default',
  name: 'Invoice Default',
  entityType: 'invoice',
  direction: 'both',
  conflictPolicy: contract?.conflictPolicies?.[0] || 'manual_review',
  isDefault: true,
  fieldMappingsJson: JSON.stringify({ docNumber: 'number', totalAmt: 'total', balance: 'balance' }, null, 2),
  enumMappingsJson: JSON.stringify({ status: { Draft: 'Draft', Paid: 'Paid' } }, null, 2),
  taxMappingsJson: JSON.stringify({ defaultTaxCode: 'TAXABLE' }, null, 2),
  accountMappingsJson: JSON.stringify({ ar: '1100', income: '4000' }, null, 2)
});

const IntegrationOpsPanel: React.FC<Props> = ({ refreshKey }) => {
  const [loading, setLoading] = useState(false);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [contract, setContract] = useState<IntegrationCanonicalContractDescriptor | null>(null);
  const [rows, setRows] = useState<IntegrationCapabilityMatrixRow[]>([]);
  const [conflicts, setConflicts] = useState<IntegrationConflictQueueItem[]>([]);
  const [reviews, setReviews] = useState<IntegrationReviewQueueItem[]>([]);
  const [inbox, setInbox] = useState<IntegrationInboxEventListItem[]>([]);
  const [outbox, setOutbox] = useState<IntegrationOutboxEventListItem[]>([]);
  const [runs, setRuns] = useState<IntegrationRunListItem[]>([]);
  const [secretStatus, setSecretStatus] = useState<IntegrationSecretStoreStatus | null>(null);
  const [secretStatusError, setSecretStatusError] = useState<string | null>(null);
  const [selectedConnectionId, setSelectedConnectionId] = useState('');
  const [mappingProfiles, setMappingProfiles] = useState<IntegrationMappingProfile[]>([]);
  const [selectedProfileId, setSelectedProfileId] = useState('');
  const [mappingDraft, setMappingDraft] = useState<MappingDraft>(defaultMappingDraft(null));
  const [actionDraft, setActionDraft] = useState<ActionDraft>(defaultActionDraft());
  const [lastAction, setLastAction] = useState<CanonicalIntegrationActionResult | null>(null);
  const [conflictNotes, setConflictNotes] = useState<Record<string, string>>({});
  const [reviewNotes, setReviewNotes] = useState<Record<string, string>>({});

  const connectedRows = useMemo(() => rows.filter(r => !!r.connectionId), [rows]);
  const selectedRow = useMemo(
    () => connectedRows.find(r => r.connectionId === selectedConnectionId) || null,
    [connectedRows, selectedConnectionId]
  );

  const scopedConflicts = useMemo(() => {
    const list = selectedRow ? conflicts.filter(c => c.providerKey === selectedRow.providerKey) : conflicts;
    return (list.length ? list : conflicts).slice(0, 10);
  }, [conflicts, selectedRow]);

  const scopedReviews = useMemo(() => {
    const list = selectedRow ? reviews.filter(r => r.providerKey === selectedRow.providerKey) : reviews;
    return (list.length ? list : reviews).slice(0, 10);
  }, [reviews, selectedRow]);

  const scopedInbox = useMemo(() => {
    const list = selectedRow ? inbox.filter(e => e.providerKey === selectedRow.providerKey) : inbox;
    return (list.length ? list : inbox).slice(0, 8);
  }, [inbox, selectedRow]);

  const scopedOutbox = useMemo(() => {
    const list = selectedRow ? outbox.filter(e => e.providerKey === selectedRow.providerKey) : outbox;
    return (list.length ? list : outbox).slice(0, 8);
  }, [outbox, selectedRow]);

  const scopedRuns = useMemo(() => {
    if (selectedRow?.connectionId) {
      const byConnection = runs.filter(r => r.connectionId === selectedRow.connectionId);
      if (byConnection.length) return byConnection.slice(0, 12);
    }
    if (selectedRow?.providerKey) {
      const byProvider = runs.filter(r => r.providerKey === selectedRow.providerKey);
      if (byProvider.length) return byProvider.slice(0, 12);
    }
    return runs.slice(0, 12);
  }, [runs, selectedRow]);

  const webhookMonitor = useMemo(() => {
    const srcInbox = selectedRow ? inbox.filter(e => e.providerKey === selectedRow.providerKey) : inbox;
    const srcOutbox = selectedRow ? outbox.filter(e => e.providerKey === selectedRow.providerKey) : outbox;
    return {
      inboxTotal: srcInbox.length,
      inboxFailed: srcInbox.filter(e => (e.status || '').toLowerCase() === 'failed').length,
      signatureFailures: srcInbox.filter(e => !e.signatureValidated).length,
      replayed: srcInbox.filter(e => (e.replayCount || 0) > 0).length,
      outboxPending: srcOutbox.filter(e => ['pending', 'retrying'].includes((e.status || '').toLowerCase())).length,
      outboxDeadLetter: srcOutbox.filter(e => !!e.deadLettered).length
    };
  }, [inbox, outbox, selectedRow]);

  const reconciliationItems = useMemo(() => {
    const financeProviders = new Set(['quickbooks-online', 'xero', 'stripe', 'business-central', 'businesscentral', 'netsuite']);
    const financeConflicts = conflicts.filter(c => {
      const providerHit = financeProviders.has((c.providerKey || '').toLowerCase());
      const text = `${c.entityType || ''} ${c.conflictType || ''} ${c.summary || ''}`.toLowerCase();
      const financeHit = ['invoice', 'payment', 'refund', 'recon', 'account', 'tax', 'trust', 'iolta'].some(k => text.includes(k));
      return providerHit || financeHit;
    }).slice(0, 8).map(c => ({
      id: c.id,
      kind: 'conflict' as const,
      providerKey: c.providerKey,
      status: c.status,
      severity: c.severity,
      title: c.summary || `${c.entityType}/${c.conflictType}`,
      createdAt: c.createdAt
    }));

    const financeReviews = reviews.filter(r => {
      const providerHit = financeProviders.has((r.providerKey || '').toLowerCase());
      const text = `${r.itemType || ''} ${r.title || ''} ${r.summary || ''}`.toLowerCase();
      const financeHit = ['invoice', 'payment', 'refund', 'recon', 'account', 'tax', 'trust', 'iolta'].some(k => text.includes(k));
      return providerHit || financeHit;
    }).slice(0, 8).map(r => ({
      id: r.id,
      kind: 'review' as const,
      providerKey: r.providerKey,
      status: r.status,
      severity: r.priority,
      title: r.title || r.summary || r.itemType,
      createdAt: r.createdAt
    }));

    return [...financeConflicts, ...financeReviews]
      .sort((a, b) => (b.createdAt || '').localeCompare(a.createdAt || ''))
      .slice(0, 12);
  }, [conflicts, reviews]);

  const runMetrics = useMemo(() => {
    const source = selectedRow?.providerKey ? runs.filter(r => r.providerKey === selectedRow.providerKey) : runs;
    return {
      total: source.length,
      running: source.filter(r => ['running', 'processing'].includes((r.status || '').toLowerCase())).length,
      failed: source.filter(r => ['failed', 'error'].includes((r.status || '').toLowerCase())).length,
      deadLetter: source.filter(r => !!r.isDeadLetter).length,
      lastCompletedAt: source
        .map(r => r.completedAt || r.startedAt || r.createdAt)
        .filter(Boolean)
        .sort((a, b) => String(b).localeCompare(String(a)))[0] || null
    };
  }, [runs, selectedRow]);

  const nextRetryAt = useMemo(() => {
    const source = selectedRow?.providerKey ? outbox.filter(e => e.providerKey === selectedRow.providerKey) : outbox;
    const pending = source
      .filter(e => ['pending', 'retrying'].includes((e.status || '').toLowerCase()) && !!e.nextAttemptAt)
      .sort((a, b) => String(a.nextAttemptAt).localeCompare(String(b.nextAttemptAt)));
    return pending[0]?.nextAttemptAt || null;
  }, [outbox, selectedRow]);

  const recentWebhookFailures = useMemo(() => {
    const source = selectedRow?.providerKey ? inbox.filter(e => e.providerKey === selectedRow.providerKey) : inbox;
    return source
      .filter(e => !e.signatureValidated || (e.status || '').toLowerCase() === 'failed')
      .slice(0, 6);
  }, [inbox, selectedRow]);

  const loadAll = async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      const [c, m, cf, rq, ib, ob, rs] = await Promise.all([
        api.integrationsOps.getContract(),
        api.integrationsOps.getCapabilityMatrix(),
        api.integrationsOps.getConflicts({ limit: 100 }),
        api.integrationsOps.getReviewQueue({ limit: 100 }),
        api.integrationsOps.getInboxEvents({ limit: 100 }),
        api.integrationsOps.getOutboxEvents({ limit: 100 }),
        api.integrationsOps.getRuns({ limit: 100 })
      ]);
      setContract(c);
      setRows(Array.isArray(m?.rows) ? m.rows : []);
      setConflicts(Array.isArray(cf) ? cf : []);
      setReviews(Array.isArray(rq) ? rq : []);
      setInbox(Array.isArray(ib) ? ib : []);
      setOutbox(Array.isArray(ob) ? ob : []);
      setRuns(Array.isArray(rs) ? rs : []);

      try {
        const secret = await api.integrationsOps.getSecretStoreStatus();
        setSecretStatus(secret || null);
        setSecretStatusError(null);
      } catch (se) {
        console.warn(se);
        setSecretStatus(null);
        setSecretStatusError('Secret store status unavailable.');
      }
    } catch (e) {
      console.error(e);
      toast.error('Failed to load integration ops data.');
    } finally {
      if (!silent) setLoading(false);
    }
  };

  const loadMappings = async (connectionId: string, preserve = false) => {
    if (!connectionId) {
      setMappingProfiles([]);
      setSelectedProfileId('');
      return;
    }
    setBusyKey(`map-load:${connectionId}`);
    try {
      const data = await api.integrationsOps.getMappingProfiles(connectionId);
      const items = Array.isArray(data) ? data : [];
      setMappingProfiles(items);
      const next = preserve ? items.find(i => i.id === selectedProfileId) || items[0] : items[0];
      if (next) {
        setSelectedProfileId(next.id);
        setMappingDraft({
          profileKey: next.profileKey,
          name: next.name,
          entityType: next.entityType,
          direction: next.direction,
          conflictPolicy: next.conflictPolicy,
          isDefault: !!next.isDefault,
          fieldMappingsJson: pretty(next.fieldMappingsJson),
          enumMappingsJson: pretty(next.enumMappingsJson),
          taxMappingsJson: pretty(next.taxMappingsJson),
          accountMappingsJson: pretty(next.accountMappingsJson)
        });
      } else {
        setSelectedProfileId('');
        setMappingDraft(defaultMappingDraft(contract));
      }
    } catch (e) {
      console.error(e);
      toast.error('Failed to load mapping profiles.');
    } finally {
      setBusyKey(prev => (prev?.startsWith('map-load:') ? null : prev));
    }
  };

  useEffect(() => { loadAll(); }, []);
  useEffect(() => { if (refreshKey) loadAll(true); }, [refreshKey]);

  useEffect(() => {
    if (!connectedRows.length) { setSelectedConnectionId(''); return; }
    if (selectedConnectionId && connectedRows.some(r => r.connectionId === selectedConnectionId)) return;
    const preferred = connectedRows.find(r => r.providerKey === 'quickbooks-online') || connectedRows.find(r => r.providerKey === 'xero') || connectedRows[0];
    setSelectedConnectionId(preferred.connectionId || '');
  }, [connectedRows, selectedConnectionId]);

  useEffect(() => {
    if (selectedConnectionId) loadMappings(selectedConnectionId);
  }, [selectedConnectionId]);

  const refresh = async () => {
    setBusyKey('refresh');
    try {
      await loadAll(true);
      if (selectedConnectionId) await loadMappings(selectedConnectionId, true);
      toast.success('Integration ops refreshed.');
    } finally {
      setBusyKey(null);
    }
  };

  const saveMapping = async () => {
    if (!selectedConnectionId) return toast.error('Select a connection first.');
    if (!mappingDraft.profileKey.trim() || !mappingDraft.name.trim() || !mappingDraft.entityType.trim()) {
      return toast.error('profileKey, name and entityType are required.');
    }
    try {
      setBusyKey('map-save');
      const saved = await api.integrationsOps.upsertMappingProfile(selectedConnectionId, mappingDraft.profileKey.trim(), {
        name: mappingDraft.name.trim(),
        entityType: mappingDraft.entityType.trim(),
        direction: mappingDraft.direction,
        status: 'active',
        conflictPolicy: mappingDraft.conflictPolicy,
        isDefault: mappingDraft.isDefault,
        fieldMappingsJson: jsonOrNull(mappingDraft.fieldMappingsJson),
        enumMappingsJson: jsonOrNull(mappingDraft.enumMappingsJson),
        taxMappingsJson: jsonOrNull(mappingDraft.taxMappingsJson),
        accountMappingsJson: jsonOrNull(mappingDraft.accountMappingsJson),
        lastValidatedAt: new Date().toISOString()
      });
      if (!saved) throw new Error('Empty response');
      await loadMappings(selectedConnectionId, true);
      await loadAll(true);
      toast.success('Mapping profile saved.');
    } catch (e) {
      console.error(e);
      toast.error('Failed to save mapping profile (check JSON format).');
    } finally {
      setBusyKey(null);
    }
  };

  const runAction = async (action: string) => {
    if (!selectedRow?.connectionId) return;
    try {
      if (actionDraft.payloadJson.trim()) JSON.parse(actionDraft.payloadJson);
      setBusyKey(`act:${action}`);
      const result = await api.integrationsOps.runCanonicalAction(selectedRow.connectionId, action, {
        entityType: actionDraft.entityType.trim() || undefined,
        payloadJson: actionDraft.payloadJson.trim() || undefined,
        dryRun: actionDraft.dryRun,
        requiresReview: actionDraft.requiresReview
      });
      setLastAction(result);
      await loadAll(true);
      toast[result?.success ? 'success' : 'error'](`${selectedRow.provider} ${action}: ${result?.status || 'failed'}`);
    } catch (e) {
      console.error(e);
      toast.error('Canonical action request failed.');
    } finally {
      setBusyKey(null);
    }
  };

  const resolveConflict = async (item: IntegrationConflictQueueItem, mode: 'resolve' | 'ignore') => {
    try {
      setBusyKey(`conflict:${item.id}`);
      await api.integrationsOps.resolveConflict(item.id, {
        status: mode === 'resolve' ? 'resolved' : 'ignored',
        resolutionType: mode === 'resolve' ? 'manual_resolve' : 'ignored',
        notes: conflictNotes[item.id] || undefined
      });
      await loadAll(true);
    } catch (e) {
      console.error(e);
      toast.error('Failed to update conflict.');
    } finally {
      setBusyKey(null);
    }
  };

  const decideReview = async (item: IntegrationReviewQueueItem, decision: 'approve' | 'retry' | 'reject' | 'triage') => {
    try {
      setBusyKey(`review:${item.id}`);
      if (decision === 'retry') {
        if (item.sourceType === 'IntegrationInboxEvent' && item.sourceId) await api.integrationsOps.replayInboxEvent(item.sourceId);
        if (item.sourceType === 'IntegrationOutboxEvent' && item.sourceId) await api.integrationsOps.replayOutboxEvent(item.sourceId);
      }
      await api.integrationsOps.decideReviewItem(item.id, {
        decision,
        status: decision === 'reject' ? 'rejected' : decision === 'triage' ? 'in_review' : 'resolved',
        notes: reviewNotes[item.id] || undefined
      });
      await loadAll(true);
    } catch (e) {
      console.error(e);
      toast.error('Failed to update review item.');
    } finally {
      setBusyKey(null);
    }
  };

  const replayInbox = async (id: string) => {
    try { setBusyKey(`inbox:${id}`); await api.integrationsOps.replayInboxEvent(id); await loadAll(true); }
    catch (e) { console.error(e); toast.error('Failed to replay inbox event.'); }
    finally { setBusyKey(null); }
  };
  const replayOutbox = async (id: string) => {
    try { setBusyKey(`outbox:${id}`); await api.integrationsOps.replayOutboxEvent(id); await loadAll(true); }
    catch (e) { console.error(e); toast.error('Failed to replay outbox event.'); }
    finally { setBusyKey(null); }
  };
  const replayRun = async (id: string) => {
    try {
      setBusyKey(`run:${id}`);
      const res = await api.integrationsOps.replayRun(id);
      await loadAll(true);
      if (res?.success) toast.success(`Run replay started: ${res.replayRunId}`);
      else toast.error(res?.message || 'Sync replay failed.');
    } catch (e) {
      console.error(e);
      toast.error('Failed to replay sync run.');
    } finally {
      setBusyKey(null);
    }
  };

  const rotateSecretsNow = async () => {
    try {
      setBusyKey('secrets:rotate');
      const res = await api.integrationsOps.rotateSecretsNow();
      await loadAll(true);
      toast.success(`Secret rotation finished. Rotated ${res?.rotated ?? 0} entries.`);
    } catch (e) {
      console.error(e);
      toast.error('Failed to rotate secrets.');
    } finally {
      setBusyKey(null);
    }
  };

  const selectProfile = (id: string) => {
    setSelectedProfileId(id);
    const p = mappingProfiles.find(x => x.id === id);
    if (!p) return;
    setMappingDraft({
      profileKey: p.profileKey, name: p.name, entityType: p.entityType, direction: p.direction, conflictPolicy: p.conflictPolicy,
      isDefault: !!p.isDefault,
      fieldMappingsJson: pretty(p.fieldMappingsJson),
      enumMappingsJson: pretty(p.enumMappingsJson),
      taxMappingsJson: pretty(p.taxMappingsJson),
      accountMappingsJson: pretty(p.accountMappingsJson)
    });
  };

  const openConflictCount = conflicts.filter(c => ['open', 'in_review'].includes(c.status)).length;
  const openReviewCount = reviews.filter(r => ['pending', 'in_review'].includes(r.status)).length;

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-6 space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="font-semibold text-slate-800">Integration Runtime Ops</h3>
          <p className="text-xs text-gray-500 mt-1">Canonical action runner, mappings, conflict/review queues, inbox/outbox replay.</p>
          <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
            <span className="px-2 py-1 rounded-full bg-slate-100 text-slate-700">Providers: {rows.length}</span>
            <span className="px-2 py-1 rounded-full bg-amber-50 text-amber-700">Conflicts: {openConflictCount}</span>
            <span className="px-2 py-1 rounded-full bg-blue-50 text-blue-700">Review: {openReviewCount}</span>
          </div>
        </div>
        <button
          type="button"
          onClick={refresh}
          disabled={loading || busyKey === 'refresh'}
          className="px-3 py-2 text-xs font-semibold border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
        >
          {busyKey === 'refresh' ? 'Refreshing...' : 'Refresh Ops'}
        </button>
      </div>

      {loading ? (
        <div className="border border-dashed border-gray-300 rounded-lg p-6 text-sm text-gray-500">
          Loading integration runtime data...
        </div>
      ) : (
        <>
          <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
            <div className="xl:col-span-2 border border-gray-200 rounded-lg overflow-hidden">
              <div className="px-4 py-3 bg-slate-50 border-b border-gray-200">
                <h4 className="text-sm font-semibold text-slate-800">Capability Matrix</h4>
                <p className="text-xs text-gray-500 mt-1">Provider support, queue backlog, runtime gaps, and connection health.</p>
              </div>
              <div className="max-h-[360px] overflow-auto">
                <table className="w-full text-xs">
                  <thead className="sticky top-0 bg-white border-b border-gray-200">
                    <tr className="text-left text-gray-500">
                      <th className="px-4 py-2 font-semibold">Provider</th>
                      <th className="px-4 py-2 font-semibold">Actions</th>
                      <th className="px-4 py-2 font-semibold">Backlog</th>
                      <th className="px-4 py-2 font-semibold">Gaps</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map(r => {
                      const selected = !!r.connectionId && r.connectionId === selectedConnectionId;
                      return (
                        <tr key={`${r.providerKey}:${r.connectionId || 'none'}`} className={`border-b border-gray-100 ${selected ? 'bg-blue-50/50' : 'hover:bg-gray-50'}`}>
                          <td className="px-4 py-3 align-top">
                            <div className="flex items-start justify-between gap-2">
                              <div>
                                <p className="font-semibold text-slate-800">{r.provider}</p>
                                <p className="text-[11px] text-gray-500">{r.category} · {r.providerKey}</p>
                                <div className="mt-1 flex flex-wrap gap-1">
                                  <span className={`px-1.5 py-0.5 rounded ${r.connectionId ? 'bg-emerald-100 text-emerald-700' : 'bg-gray-100 text-gray-600'}`}>
                                    {r.connectionId ? (r.connectionStatus || 'connected') : 'not_connected'}
                                  </span>
                                  {r.webhookFirst && <span className="px-1.5 py-0.5 rounded bg-emerald-50 text-emerald-700">webhook-first</span>}
                                </div>
                              </div>
                              {r.connectionId && (
                                <button type="button" onClick={() => setSelectedConnectionId(r.connectionId || '')} className="px-2 py-1 text-[11px] border border-gray-300 rounded hover:bg-white">
                                  Select
                                </button>
                              )}
                            </div>
                          </td>
                          <td className="px-4 py-3 align-top">
                            <div className="flex flex-wrap gap-1">
                              {r.supportedActions.map(a => <span key={`${r.providerKey}:${a}`} className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-700">{a}</span>)}
                            </div>
                            <p className="text-[11px] text-gray-500 mt-2">{(r.capabilities || []).slice(0, 4).join(', ')}{(r.capabilities?.length || 0) > 4 ? '...' : ''}</p>
                          </td>
                          <td className="px-4 py-3 align-top text-[11px] text-gray-600">
                            <div>Mappings {r.mappingProfileCount}</div>
                            <div>Conflicts {r.openConflictCount}</div>
                            <div>Review {r.openReviewCount}</div>
                            <div>Inbox {r.pendingInboxEventCount}</div>
                            <div>Outbox {r.pendingOutboxEventCount}</div>
                          </td>
                          <td className="px-4 py-3 align-top">
                            {r.gaps?.length ? (
                              <div className="flex flex-wrap gap-1">
                                {r.gaps.slice(0, 4).map(g => <span key={`${r.providerKey}:${g}`} className="text-[11px] px-1.5 py-0.5 rounded bg-amber-50 text-amber-700">{g}</span>)}
                              </div>
                            ) : <span className="text-[11px] px-1.5 py-0.5 rounded bg-emerald-50 text-emerald-700">healthy</span>}
                          </td>
                        </tr>
                      );
                    })}
                    {rows.length === 0 && <tr><td colSpan={4} className="px-4 py-6 text-sm text-gray-500">No capability matrix data.</td></tr>}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="border border-gray-200 rounded-lg p-4 bg-slate-50/60">
              <h4 className="text-sm font-semibold text-slate-800">Contract</h4>
              {contract ? (
                <div className="mt-3 space-y-3 text-xs">
                  <div><p className="text-gray-500 mb-1">Actions</p><p className="text-slate-700">{contract.actions.join(', ')}</p></div>
                  <div><p className="text-gray-500 mb-1">Conflict Policies</p><p className="text-slate-700">{contract.conflictPolicies.join(', ')}</p></div>
                  <div><p className="text-gray-500 mb-1">Inbox Status</p><p className="text-slate-700">{contract.eventStatuses.inbox.join(', ')}</p></div>
                  <div><p className="text-gray-500 mb-1">Outbox Status</p><p className="text-slate-700">{contract.eventStatuses.outbox.join(', ')}</p></div>
                </div>
              ) : <p className="text-xs text-gray-500 mt-2">Contract metadata unavailable.</p>}
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="border border-gray-200 rounded-lg p-4 space-y-3">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <h4 className="text-sm font-semibold text-slate-800">Sync Status</h4>
                  <p className="text-xs text-gray-500 mt-1">Recent runs, replay controls, and next retry visibility.</p>
                </div>
                <div className="text-right text-[11px] text-gray-500">
                  <div>Last completed</div>
                  <div className="text-slate-700">{fmt(runMetrics.lastCompletedAt)}</div>
                </div>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-5 gap-2 text-xs">
                <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Runs</div><div className="font-semibold text-slate-800">{runMetrics.total}</div></div>
                <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Running</div><div className="font-semibold text-blue-700">{runMetrics.running}</div></div>
                <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Failed</div><div className="font-semibold text-red-700">{runMetrics.failed}</div></div>
                <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Dead letter</div><div className="font-semibold text-amber-700">{runMetrics.deadLetter}</div></div>
                <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Next retry</div><div className="font-semibold text-slate-800 truncate">{nextRetryAt ? new Date(nextRetryAt).toLocaleTimeString('en-US') : '-'}</div></div>
              </div>
              <p className="text-[11px] text-gray-500">Cursor/delta state is action-specific. Connection-level cursor exposure is not yet returned by the ops API.</p>
              <div className="max-h-[280px] overflow-auto border border-gray-200 rounded bg-white divide-y">
                {scopedRuns.length === 0 && <p className="p-3 text-sm text-gray-500">No integration runs.</p>}
                {scopedRuns.map(r => (
                  <div key={r.id} className="p-3 text-xs">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-semibold text-slate-700">{r.providerKey}</span>
                          <span className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-700">{r.trigger}</span>
                          <span className={`px-1.5 py-0.5 rounded ${
                            ['completed', 'success', 'processed'].includes((r.status || '').toLowerCase())
                              ? 'bg-emerald-50 text-emerald-700'
                              : ['failed', 'error'].includes((r.status || '').toLowerCase())
                                ? 'bg-red-50 text-red-700'
                                : 'bg-blue-50 text-blue-700'
                          }`}>{r.status}</span>
                          {r.isDeadLetter && <span className="px-1.5 py-0.5 rounded bg-amber-50 text-amber-700">dead-letter</span>}
                        </div>
                        <div className="mt-1 text-[11px] text-gray-500 break-all">
                          {r.id} · attempts {r.attemptCount}/{r.maxAttempts || '-'} · {r.idempotencyKey || 'no-idempotency-key'}
                        </div>
                        <div className="mt-1 text-[11px] text-gray-500">
                          created {fmt(r.createdAt)} · started {fmt(r.startedAt)} · completed {fmt(r.completedAt)}
                        </div>
                        {r.errorMessage && <p className="mt-1 text-[11px] text-red-700 break-words">{r.errorCode ? `${r.errorCode}: ` : ''}{r.errorMessage}</p>}
                      </div>
                      <button
                        type="button"
                        onClick={() => replayRun(r.id)}
                        disabled={busyKey === `run:${r.id}`}
                        className="px-2.5 py-1 text-xs border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50 whitespace-nowrap"
                      >
                        {busyKey === `run:${r.id}` ? 'Replaying...' : 'Replay'}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="space-y-4">
              <div className="border border-gray-200 rounded-lg p-4">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <h4 className="text-sm font-semibold text-slate-800">Webhook Monitor</h4>
                    <p className="text-xs text-gray-500 mt-1">Recent inbound/outbound event health, signature failures, and replay actions.</p>
                  </div>
                  <div className="text-[11px] text-gray-500">{selectedRow?.providerKey || 'all providers'}</div>
                </div>
                <div className="mt-3 grid grid-cols-2 md:grid-cols-3 gap-2 text-xs">
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Inbox</div><div className="font-semibold text-slate-800">{webhookMonitor.inboxTotal}</div></div>
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Inbox failed</div><div className="font-semibold text-red-700">{webhookMonitor.inboxFailed}</div></div>
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Signature fail</div><div className="font-semibold text-red-700">{webhookMonitor.signatureFailures}</div></div>
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Replayed</div><div className="font-semibold text-blue-700">{webhookMonitor.replayed}</div></div>
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Outbox pending</div><div className="font-semibold text-amber-700">{webhookMonitor.outboxPending}</div></div>
                  <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Outbox dead-letter</div><div className="font-semibold text-red-700">{webhookMonitor.outboxDeadLetter}</div></div>
                </div>
                <div className="mt-3 max-h-[220px] overflow-auto border border-gray-200 rounded bg-white divide-y">
                  {recentWebhookFailures.length === 0 && <p className="p-3 text-sm text-gray-500">No signature or processing failures.</p>}
                  {recentWebhookFailures.map(e => (
                    <div key={e.id} className="p-3 text-xs">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-slate-700">{e.providerKey}</span>
                            <span className={`px-1.5 py-0.5 rounded ${e.signatureValidated ? 'bg-gray-100 text-gray-700' : 'bg-red-50 text-red-700'}`}>
                              {e.signatureValidated ? 'signature ok' : 'signature failed'}
                            </span>
                            <span className={`px-1.5 py-0.5 rounded ${(e.status || '').toLowerCase() === 'failed' ? 'bg-red-50 text-red-700' : 'bg-slate-100 text-slate-700'}`}>{e.status}</span>
                          </div>
                          <div className="mt-1 text-[11px] text-gray-500 break-all">{e.externalEventId}</div>
                          <div className="mt-1 text-[11px] text-gray-500">Received {fmt(e.receivedAt)} · Replay #{e.replayCount}</div>
                          {e.errorMessage && <div className="mt-1 text-[11px] text-red-700 break-words">{e.errorMessage}</div>}
                        </div>
                        <button type="button" onClick={() => replayInbox(e.id)} disabled={busyKey === `inbox:${e.id}`} className="px-2 py-1 text-xs border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50">
                          {busyKey === `inbox:${e.id}` ? 'Replaying...' : 'Replay'}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="border border-gray-200 rounded-lg p-4">
                  <h4 className="text-sm font-semibold text-slate-800">Reconciliation</h4>
                  <p className="text-xs text-gray-500 mt-1">Finance mismatch queue (QBO/Xero/Stripe + accounting mapping issues).</p>
                  <div className="mt-3 max-h-[220px] overflow-auto border border-gray-200 rounded bg-white divide-y">
                    {reconciliationItems.length === 0 && <p className="p-3 text-sm text-gray-500">No finance mismatches in queue.</p>}
                    {reconciliationItems.map(item => (
                      <div key={`${item.kind}:${item.id}`} className="p-3 text-xs">
                        <div className="flex flex-wrap gap-2">
                          <span className="font-semibold text-slate-700">{item.providerKey}</span>
                          <span className={`px-1.5 py-0.5 rounded ${item.kind === 'conflict' ? 'bg-amber-50 text-amber-700' : 'bg-blue-50 text-blue-700'}`}>{item.kind}</span>
                          <span className="px-1.5 py-0.5 rounded bg-gray-100 text-gray-700">{item.status}</span>
                          <span className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-700">{item.severity}</span>
                        </div>
                        <div className="mt-1 text-slate-700">{item.title}</div>
                        <div className="mt-1 text-[11px] text-gray-500">{fmt(item.createdAt)}</div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="border border-gray-200 rounded-lg p-4">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <h4 className="text-sm font-semibold text-slate-800">Secret Store</h4>
                      <p className="text-xs text-gray-500 mt-1">KMS/Key Vault mode, keyring status, scope access, and manual rotation.</p>
                    </div>
                    <button
                      type="button"
                      onClick={rotateSecretsNow}
                      disabled={busyKey === 'secrets:rotate' || !secretStatus}
                      className="px-2.5 py-1 text-xs font-semibold border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50"
                    >
                      {busyKey === 'secrets:rotate' ? 'Rotating...' : 'Rotate'}
                    </button>
                  </div>
                  {secretStatusError ? (
                    <p className="mt-3 text-xs text-gray-500">{secretStatusError}</p>
                  ) : !secretStatus ? (
                    <p className="mt-3 text-xs text-gray-500">Secret store status unavailable.</p>
                  ) : (
                    <div className="mt-3 space-y-3 text-xs">
                      <div className="grid grid-cols-2 gap-2">
                        <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Provider</div><div className="font-semibold text-slate-800">{secretStatus.providerMode}</div></div>
                        <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Crypto</div><div className="font-semibold text-slate-800">{secretStatus.encryptionProviderId || '-'}</div></div>
                        <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Active key</div><div className="font-semibold text-slate-800 truncate">{secretStatus.activeKeyId || '-'}</div></div>
                        <div className="rounded border border-gray-200 p-2 bg-white"><div className="text-gray-500">Entries</div><div className="font-semibold text-slate-800">{secretStatus.entries?.total ?? 0}</div></div>
                      </div>
                      <div className="text-[11px] text-gray-500">
                        Keyring: {secretStatus.keyRingSource || '-'} · configured keys {secretStatus.configuredKeyCount} · rotation {secretStatus.rotationEnabled ? `on (${secretStatus.rotationIntervalMinutes}m)` : 'off'}
                      </div>
                      <div className="max-h-[120px] overflow-auto border border-gray-200 rounded bg-white divide-y">
                        {(secretStatus.scopeMatrix || []).map(scope => (
                          <div key={scope.scope} className="p-2 flex items-center justify-between gap-2 text-[11px]">
                            <span className="font-semibold text-slate-700">{scope.scope}</span>
                            <div className="flex flex-wrap gap-1">
                              {(['read', 'write', 'delete', 'rotate'] as const).map(op => (
                                <span key={`${scope.scope}:${op}`} className={`px-1.5 py-0.5 rounded ${scope[op] ? 'bg-emerald-50 text-emerald-700' : 'bg-gray-100 text-gray-500'}`}>
                                  {op}
                                </span>
                              ))}
                            </div>
                          </div>
                        ))}
                        {(secretStatus.scopeMatrix || []).length === 0 && <p className="p-2 text-[11px] text-gray-500">No scope matrix data.</p>}
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="border border-gray-200 rounded-lg p-4 space-y-3">
              <div>
                <h4 className="text-sm font-semibold text-slate-800">Canonical Action Runner</h4>
                <p className="text-xs text-gray-500 mt-1">Run validate/pull/push/backfill/reconcile on selected QBO/Xero connection.</p>
              </div>
              <select value={selectedConnectionId} onChange={e => setSelectedConnectionId(e.target.value)} className="w-full border border-gray-300 rounded-lg p-2 text-sm">
                <option value="">Select connection</option>
                {connectedRows.map(r => <option key={r.connectionId} value={r.connectionId || ''}>{r.provider} ({r.providerKey})</option>)}
              </select>
              {selectedRow && (
                <div className="rounded border border-blue-100 bg-blue-50 p-3 text-xs">
                  <div className="font-semibold text-blue-800">{selectedRow.provider} · {selectedRow.providerKey}</div>
                  <div className="text-blue-700 mt-1">Last run: {selectedRow.lastRunStatus || '-'} · {fmt(selectedRow.lastRunAt)}</div>
                </div>
              )}
              <input value={actionDraft.entityType} onChange={e => setActionDraft(p => ({ ...p, entityType: e.target.value }))} placeholder="entityType (invoice/payment/customer)" className="w-full border border-gray-300 rounded-lg p-2 text-xs" />
              <textarea value={actionDraft.payloadJson} onChange={e => setActionDraft(p => ({ ...p, payloadJson: e.target.value }))} rows={4} placeholder='payloadJson (optional) e.g. {"invoiceIds":["INV-1"]}' className="w-full border border-gray-300 rounded-lg p-2 text-xs font-mono" />
              <div className="flex flex-wrap gap-3 text-xs">
                <label className="flex items-center gap-2"><input type="checkbox" checked={actionDraft.dryRun} onChange={e => setActionDraft(p => ({ ...p, dryRun: e.target.checked }))} /> Dry run</label>
                <label className="flex items-center gap-2"><input type="checkbox" checked={actionDraft.requiresReview} onChange={e => setActionDraft(p => ({ ...p, requiresReview: e.target.checked }))} /> Review on fail</label>
              </div>
              <div className="flex flex-wrap gap-2">
                {(selectedRow?.supportedActions || []).map(a => (
                  <button key={a} type="button" onClick={() => runAction(a)} disabled={!selectedRow?.connectionId || busyKey === `act:${a}`} className="px-3 py-1.5 text-xs font-semibold border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50">
                    {busyKey === `act:${a}` ? `Running ${a}...` : a}
                  </button>
                ))}
              </div>
              {lastAction && (
                <div className={`rounded border p-3 text-xs ${lastAction.success ? 'border-emerald-200 bg-emerald-50' : 'border-red-200 bg-red-50'}`}>
                  <div className="font-semibold">{lastAction.action} · {lastAction.status}</div>
                  <div className="mt-1">read {lastAction.readCount} · write {lastAction.writeCount} · conflicts {lastAction.conflictCount} · reviews {lastAction.reviewCount}</div>
                  {(lastAction.errorMessage || lastAction.message) && <p className="mt-1 break-words">{lastAction.errorMessage || lastAction.message}</p>}
                </div>
              )}
            </div>

            <div className="border border-gray-200 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between gap-2">
                <div>
                  <h4 className="text-sm font-semibold text-slate-800">Mapping Profiles</h4>
                  <p className="text-xs text-gray-500 mt-1">Invoice/payment/customer field + account/tax mapping editor.</p>
                </div>
                <button type="button" onClick={() => { setSelectedProfileId(''); setMappingDraft(defaultMappingDraft(contract)); }} className="px-2 py-1 text-xs border border-gray-300 rounded hover:bg-gray-50">New</button>
              </div>
              <div className="flex flex-wrap gap-2">
                <button type="button" onClick={() => setMappingDraft(defaultMappingDraft(contract))} className="px-2 py-1 text-xs border rounded border-gray-300 hover:bg-gray-50">Invoice preset</button>
                <button type="button" onClick={() => setMappingDraft(p => ({ ...p, profileKey: 'payment-default', name: 'Payment Default', entityType: 'payment' }))} className="px-2 py-1 text-xs border rounded border-gray-300 hover:bg-gray-50">Payment preset</button>
                <button type="button" onClick={() => setMappingDraft(p => ({ ...p, profileKey: 'customer-default', name: 'Customer Default', entityType: 'customer' }))} className="px-2 py-1 text-xs border rounded border-gray-300 hover:bg-gray-50">Customer preset</button>
              </div>
              <select value={selectedProfileId} onChange={e => selectProfile(e.target.value)} disabled={!selectedConnectionId} className="w-full border border-gray-300 rounded-lg p-2 text-sm">
                <option value="">Create / overwrite by profileKey</option>
                {mappingProfiles.map(p => <option key={p.id} value={p.id}>{p.entityType} · {p.name} ({p.profileKey}) v{p.version}</option>)}
              </select>
              <div className="grid grid-cols-2 gap-2">
                <input value={mappingDraft.profileKey} onChange={e => setMappingDraft(p => ({ ...p, profileKey: e.target.value }))} placeholder="profileKey" className="border border-gray-300 rounded-lg p-2 text-xs" />
                <input value={mappingDraft.name} onChange={e => setMappingDraft(p => ({ ...p, name: e.target.value }))} placeholder="name" className="border border-gray-300 rounded-lg p-2 text-xs" />
                <input value={mappingDraft.entityType} onChange={e => setMappingDraft(p => ({ ...p, entityType: e.target.value }))} placeholder="entityType" className="border border-gray-300 rounded-lg p-2 text-xs" />
                <select value={mappingDraft.direction} onChange={e => setMappingDraft(p => ({ ...p, direction: e.target.value }))} className="border border-gray-300 rounded-lg p-2 text-xs"><option value="both">both</option><option value="inbound">inbound</option><option value="outbound">outbound</option></select>
              </div>
              <select value={mappingDraft.conflictPolicy} onChange={e => setMappingDraft(p => ({ ...p, conflictPolicy: e.target.value }))} className="w-full border border-gray-300 rounded-lg p-2 text-xs">
                {(contract?.conflictPolicies || ['manual_review']).map(p => <option key={p} value={p}>{p}</option>)}
              </select>
              <label className="flex items-center gap-2 text-xs text-gray-600"><input type="checkbox" checked={mappingDraft.isDefault} onChange={e => setMappingDraft(p => ({ ...p, isDefault: e.target.checked }))} /> Default profile</label>
              <textarea value={mappingDraft.fieldMappingsJson} onChange={e => setMappingDraft(p => ({ ...p, fieldMappingsJson: e.target.value }))} rows={3} className="w-full border border-gray-300 rounded-lg p-2 text-xs font-mono" placeholder="fieldMappingsJson" />
              <textarea value={mappingDraft.enumMappingsJson} onChange={e => setMappingDraft(p => ({ ...p, enumMappingsJson: e.target.value }))} rows={2} className="w-full border border-gray-300 rounded-lg p-2 text-xs font-mono" placeholder="enumMappingsJson" />
              <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                <textarea value={mappingDraft.taxMappingsJson} onChange={e => setMappingDraft(p => ({ ...p, taxMappingsJson: e.target.value }))} rows={2} className="w-full border border-gray-300 rounded-lg p-2 text-xs font-mono" placeholder="taxMappingsJson" />
                <textarea value={mappingDraft.accountMappingsJson} onChange={e => setMappingDraft(p => ({ ...p, accountMappingsJson: e.target.value }))} rows={2} className="w-full border border-gray-300 rounded-lg p-2 text-xs font-mono" placeholder="accountMappingsJson" />
              </div>
              <div className="flex justify-end">
                <button type="button" onClick={saveMapping} disabled={!selectedConnectionId || busyKey === 'map-save' || !!busyKey?.startsWith('map-load:')} className="px-3 py-2 text-xs font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50">
                  {busyKey === 'map-save' ? 'Saving...' : 'Save Mapping'}
                </button>
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="border border-gray-200 rounded-lg p-4">
              <div className="flex items-center justify-between"><h4 className="text-sm font-semibold text-slate-800">Conflict Queue</h4><span className="text-xs text-gray-500">{scopedConflicts.length} shown</span></div>
              <div className="mt-3 space-y-3 max-h-[360px] overflow-auto">
                {scopedConflicts.length === 0 && <p className="text-sm text-gray-500">No conflicts.</p>}
                {scopedConflicts.map(c => (
                  <div key={c.id} className="border border-gray-200 rounded p-3">
                    <div className="flex flex-wrap gap-2 text-[11px]">
                      <span className="font-semibold text-slate-700">{c.providerKey}</span>
                      <span className="px-1.5 py-0.5 rounded bg-gray-100 text-gray-700">{c.status}</span>
                      <span className="px-1.5 py-0.5 rounded bg-amber-50 text-amber-700">{c.severity}</span>
                      <span className="text-gray-500">{c.entityType}/{c.conflictType}</span>
                    </div>
                    <p className="mt-1 text-xs text-slate-700">{c.summary || 'No summary'}</p>
                    <p className="mt-1 text-[11px] text-gray-500">local={c.localEntityId || '-'} · external={c.externalEntityId || '-'} · {fmt(c.createdAt)}</p>
                    <textarea value={conflictNotes[c.id] || ''} onChange={e => setConflictNotes(p => ({ ...p, [c.id]: e.target.value }))} rows={2} className="mt-2 w-full border border-gray-300 rounded p-2 text-xs" placeholder="Resolution notes (optional)" />
                    <div className="mt-2 flex gap-2">
                      <button type="button" onClick={() => resolveConflict(c, 'resolve')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-emerald-200 text-emerald-700 rounded hover:bg-emerald-50 disabled:opacity-50">Resolve</button>
                      <button type="button" onClick={() => resolveConflict(c, 'ignore')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-gray-300 text-gray-700 rounded hover:bg-gray-50 disabled:opacity-50">Ignore</button>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="border border-gray-200 rounded-lg p-4">
              <div className="flex items-center justify-between"><h4 className="text-sm font-semibold text-slate-800">Review Queue</h4><span className="text-xs text-gray-500">{scopedReviews.length} shown</span></div>
              <div className="mt-3 space-y-3 max-h-[360px] overflow-auto">
                {scopedReviews.length === 0 && <p className="text-sm text-gray-500">No review items.</p>}
                {scopedReviews.map(r => (
                  <div key={r.id} className="border border-gray-200 rounded p-3">
                    <div className="flex flex-wrap gap-2 text-[11px]">
                      <span className="font-semibold text-slate-700">{r.providerKey}</span>
                      <span className="px-1.5 py-0.5 rounded bg-gray-100 text-gray-700">{r.status}</span>
                      <span className="px-1.5 py-0.5 rounded bg-blue-50 text-blue-700">{r.priority}</span>
                      <span className="text-gray-500">{r.itemType}</span>
                    </div>
                    <p className="mt-1 text-xs font-semibold text-slate-700">{r.title || 'Untitled'}</p>
                    <p className="mt-1 text-xs text-gray-600">{r.summary || 'No summary'}</p>
                    <p className="mt-1 text-[11px] text-gray-500">source={r.sourceType || '-'} · sourceId={r.sourceId || '-'}</p>
                    <textarea value={reviewNotes[r.id] || ''} onChange={e => setReviewNotes(p => ({ ...p, [r.id]: e.target.value }))} rows={2} className="mt-2 w-full border border-gray-300 rounded p-2 text-xs" placeholder="Decision notes (optional)" />
                    <div className="mt-2 flex flex-wrap gap-2">
                      <button type="button" onClick={() => decideReview(r, 'approve')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-emerald-200 text-emerald-700 rounded hover:bg-emerald-50 disabled:opacity-50">Approve</button>
                      <button type="button" onClick={() => decideReview(r, 'retry')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-blue-200 text-blue-700 rounded hover:bg-blue-50 disabled:opacity-50">Retry</button>
                      <button type="button" onClick={() => decideReview(r, 'triage')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-amber-200 text-amber-700 rounded hover:bg-amber-50 disabled:opacity-50">In Review</button>
                      <button type="button" onClick={() => decideReview(r, 'reject')} disabled={!!busyKey} className="px-2.5 py-1 text-xs border border-red-200 text-red-700 rounded hover:bg-red-50 disabled:opacity-50">Reject</button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="border border-gray-200 rounded-lg p-4">
              <h4 className="text-sm font-semibold text-slate-800">Inbox Events</h4>
              <p className="text-xs text-gray-500 mt-1">Webhook/inbound replay controls.</p>
              <div className="mt-3 space-y-3 max-h-[300px] overflow-auto">
                {scopedInbox.length === 0 && <p className="text-sm text-gray-500">No inbox events.</p>}
                {scopedInbox.map(e => (
                  <div key={e.id} className="border border-gray-200 rounded p-3">
                    <div className="flex flex-wrap gap-2 text-[11px]">
                      <span className="font-semibold text-slate-700">{e.providerKey}</span>
                      <span className="px-1.5 py-0.5 rounded bg-gray-100 text-gray-700">{e.status}</span>
                      {e.signatureValidated && <span className="px-1.5 py-0.5 rounded bg-emerald-50 text-emerald-700">signature ok</span>}
                      <span className="text-gray-500">replay #{e.replayCount}</span>
                    </div>
                    <p className="mt-1 text-[11px] text-gray-500 break-all">{e.externalEventId}</p>
                    {e.errorMessage && <p className="mt-1 text-xs text-red-700">{e.errorMessage}</p>}
                    <p className="mt-1 text-[11px] text-gray-500">received {fmt(e.receivedAt)} · processed {fmt(e.processedAt)}</p>
                    <button type="button" onClick={() => replayInbox(e.id)} disabled={busyKey === `inbox:${e.id}`} className="mt-2 px-2.5 py-1 text-xs border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50">{busyKey === `inbox:${e.id}` ? 'Replaying...' : 'Replay'}</button>
                  </div>
                ))}
              </div>
            </div>

            <div className="border border-gray-200 rounded-lg p-4">
              <h4 className="text-sm font-semibold text-slate-800">Outbox Events</h4>
              <p className="text-xs text-gray-500 mt-1">Dispatch trail and dead-letter requeue controls.</p>
              <div className="mt-3 space-y-3 max-h-[300px] overflow-auto">
                {scopedOutbox.length === 0 && <p className="text-sm text-gray-500">No outbox events.</p>}
                {scopedOutbox.map(e => (
                  <div key={e.id} className="border border-gray-200 rounded p-3">
                    <div className="flex flex-wrap gap-2 text-[11px]">
                      <span className="font-semibold text-slate-700">{e.providerKey}</span>
                      <span className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-700">{e.eventType}</span>
                      <span className={`px-1.5 py-0.5 rounded ${e.deadLettered ? 'bg-red-50 text-red-700' : 'bg-gray-100 text-gray-700'}`}>{e.status}</span>
                    </div>
                    <p className="mt-1 text-[11px] text-gray-500">entity={e.entityType || '-'}:{e.entityId || '-'} · attempts={e.attemptCount}</p>
                    {e.errorMessage && <p className="mt-1 text-xs text-red-700">{e.errorMessage}</p>}
                    <p className="mt-1 text-[11px] text-gray-500">next {fmt(e.nextAttemptAt)} · created {fmt(e.createdAt)}</p>
                    <button type="button" onClick={() => replayOutbox(e.id)} disabled={busyKey === `outbox:${e.id}`} className="mt-2 px-2.5 py-1 text-xs border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50">{busyKey === `outbox:${e.id}` ? 'Requeueing...' : 'Requeue'}</button>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  );
};

export default IntegrationOpsPanel;
