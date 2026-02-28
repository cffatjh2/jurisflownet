import React, { useEffect, useMemo, useState } from 'react';
import { api } from '../services/api';
import { toast } from './Toast';
import {
  EfilingDocketAutomationResponse,
  EfilingPrecheckResponse,
  EfilingSubmissionTimelineResponse,
  EfilingWorkspaceResponse,
  IntegrationItem,
  Matter
} from '../types';

type Props = {
  integrations?: IntegrationItem[];
  refreshKey?: string;
};

type MatterOption = Pick<Matter, 'id' | 'name' | 'caseNumber' | 'courtType' | 'status'>;

const fmtDate = (value?: string | null) => (value ? new Date(value).toLocaleString('en-US') : '-');

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded border border-gray-200 bg-white p-2">
    <div className="text-[11px] text-gray-500">{label}</div>
    <div className="text-sm font-semibold text-slate-800">{value}</div>
  </div>
);

const EfilingWorkflowPanel: React.FC<Props> = ({ integrations = [], refreshKey }) => {
  const [loadingMatters, setLoadingMatters] = useState(false);
  const [matters, setMatters] = useState<MatterOption[]>([]);
  const [selectedMatterId, setSelectedMatterId] = useState('');
  const [selectedProviderKey, setSelectedProviderKey] = useState('');

  const [workspaceLoading, setWorkspaceLoading] = useState(false);
  const [workspace, setWorkspace] = useState<EfilingWorkspaceResponse | null>(null);
  const [selectedDocumentIds, setSelectedDocumentIds] = useState<string[]>([]);
  const [packetName, setPacketName] = useState('');
  const [filingType, setFilingType] = useState('');
  const [metadataCaseNumber, setMetadataCaseNumber] = useState('');
  const [metadataPartyRole, setMetadataPartyRole] = useState('Plaintiff');
  const [precheckLoading, setPrecheckLoading] = useState(false);
  const [precheckResult, setPrecheckResult] = useState<EfilingPrecheckResponse | null>(null);

  const [submissionsLoading, setSubmissionsLoading] = useState(false);
  const [submissions, setSubmissions] = useState<any[]>([]);
  const [filingQueueFilter, setFilingQueueFilter] = useState('all');
  const [selectedSubmissionId, setSelectedSubmissionId] = useState('');
  const [timelineLoading, setTimelineLoading] = useState(false);
  const [timeline, setTimeline] = useState<EfilingSubmissionTimelineResponse | null>(null);
  const [transitionTarget, setTransitionTarget] = useState('submitted');
  const [transitionReason, setTransitionReason] = useState('');
  const [transitionBusy, setTransitionBusy] = useState(false);
  const [repairNotes, setRepairNotes] = useState('');
  const [repairBusy, setRepairBusy] = useState(false);

  const [docketsLoading, setDocketsLoading] = useState(false);
  const [dockets, setDockets] = useState<any[]>([]);
  const [docketAutomationBusy, setDocketAutomationBusy] = useState(false);
  const [docketAutomationResult, setDocketAutomationResult] = useState<EfilingDocketAutomationResponse | null>(null);

  const efilingIntegrations = useMemo(
    () =>
      integrations.filter(i =>
        ['courtlistener-dockets', 'courtlistener-recap', 'one-legal-efile', 'fileandservexpress-efile'].includes((i.providerKey || '').toLowerCase())
      ),
    [integrations]
  );

  const connectedEfilingProviders = useMemo(
    () => efilingIntegrations.filter(i => i.status === 'connected'),
    [efilingIntegrations]
  );

  const selectedMatter = useMemo(
    () => matters.find(m => m.id === selectedMatterId) || null,
    [matters, selectedMatterId]
  );

  const loadMatters = async () => {
    setLoadingMatters(true);
    try {
      const response = await api.getMatters({ status: 'Open' });
      const rows = Array.isArray(response)
        ? response
        : Array.isArray((response as any)?.items)
          ? (response as any).items
          : [];
      const normalized: MatterOption[] = rows.map((m: any) => ({
        id: m.id,
        name: m.name,
        caseNumber: m.caseNumber,
        courtType: m.courtType,
        status: m.status
      }));
      setMatters(normalized);
      if (!selectedMatterId && normalized.length > 0) {
        setSelectedMatterId(normalized[0].id);
      }
    } catch (error) {
      console.error(error);
      toast.error('Failed to load matters.');
    } finally {
      setLoadingMatters(false);
    }
  };

  const loadWorkspace = async () => {
    if (!selectedMatterId) {
      setWorkspace(null);
      return;
    }
    setWorkspaceLoading(true);
    try {
      const data = await api.efiling.getWorkspace(selectedMatterId, selectedProviderKey || undefined);
      setWorkspace(data);
      if (data?.suggestedPacket) {
        setPacketName(data.suggestedPacket.packetName || '');
        setFilingType(data.suggestedPacket.suggestedFilingType || '');
        const suggestedIds = Array.isArray(data.suggestedPacket.suggestedDocumentIds) ? data.suggestedPacket.suggestedDocumentIds : [];
        setSelectedDocumentIds(suggestedIds);
      }
      if (data?.matter?.caseNumber) {
        setMetadataCaseNumber(prev => prev || data.matter.caseNumber);
      }
      if (!selectedSubmissionId && data?.submissions?.length) {
        setSelectedSubmissionId(data.submissions[0].id);
      }
    } catch (error) {
      console.error(error);
      toast.error('Failed to load e-filing workspace.');
    } finally {
      setWorkspaceLoading(false);
    }
  };

  const loadSubmissions = async () => {
    if (!selectedMatterId) {
      setSubmissions([]);
      return;
    }
    setSubmissionsLoading(true);
    try {
      const rows = await api.efiling.listSubmissions({ matterId: selectedMatterId, providerKey: selectedProviderKey || undefined, limit: 100 });
      const list = Array.isArray(rows) ? rows : [];
      setSubmissions(list);
      if (!selectedSubmissionId || !list.some((s: any) => s.id === selectedSubmissionId)) {
        setSelectedSubmissionId(list[0]?.id || '');
      }
    } catch (error) {
      console.error(error);
      toast.error('Failed to load filing submissions.');
    } finally {
      setSubmissionsLoading(false);
    }
  };

  const loadTimeline = async (submissionId: string) => {
    if (!submissionId) {
      setTimeline(null);
      return;
    }
    setTimelineLoading(true);
    try {
      const data = await api.efiling.getSubmissionTimeline(submissionId);
      setTimeline(data);
    } catch (error) {
      console.error(error);
      toast.error('Failed to load submission timeline.');
    } finally {
      setTimelineLoading(false);
    }
  };

  const loadDockets = async () => {
    if (!selectedMatterId) {
      setDockets([]);
      return;
    }
    setDocketsLoading(true);
    try {
      const rows = await api.efiling.listDockets({ matterId: selectedMatterId, limit: 100 });
      setDockets(Array.isArray(rows) ? rows : []);
    } catch (error) {
      console.error(error);
      toast.error('Failed to load dockets.');
    } finally {
      setDocketsLoading(false);
    }
  };

  const reloadAll = async () => {
    await Promise.all([loadWorkspace(), loadSubmissions(), loadDockets()]);
  };

  useEffect(() => { void loadMatters(); }, []);
  useEffect(() => { if (refreshKey && selectedMatterId) { void reloadAll(); } }, [refreshKey]);

  useEffect(() => {
    if (!selectedProviderKey && connectedEfilingProviders.length > 0) {
      const preferred = connectedEfilingProviders.find(i => i.providerKey === 'one-legal-efile')
        || connectedEfilingProviders.find(i => i.providerKey === 'fileandservexpress-efile')
        || connectedEfilingProviders.find(i => i.providerKey === 'courtlistener-recap')
        || connectedEfilingProviders[0];
      if (preferred?.providerKey) setSelectedProviderKey(preferred.providerKey);
    }
  }, [connectedEfilingProviders, selectedProviderKey]);

  useEffect(() => {
    if (selectedMatterId) {
      void reloadAll();
    }
  }, [selectedMatterId, selectedProviderKey]);

  useEffect(() => {
    if (selectedSubmissionId) {
      void loadTimeline(selectedSubmissionId);
    } else {
      setTimeline(null);
    }
  }, [selectedSubmissionId]);

  const toggleDocument = (id: string) => {
    setSelectedDocumentIds(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
  };

  const applySuggestedPacket = () => {
    if (!workspace?.suggestedPacket) return;
    setPacketName(workspace.suggestedPacket.packetName || '');
    setFilingType(workspace.suggestedPacket.suggestedFilingType || '');
    setSelectedDocumentIds(Array.isArray(workspace.suggestedPacket.suggestedDocumentIds) ? workspace.suggestedPacket.suggestedDocumentIds : []);
  };

  const runPrecheck = async () => {
    if (!selectedMatterId) return toast.error('Select a matter first.');
    if (selectedDocumentIds.length === 0) return toast.error('Select at least one document.');
    setPrecheckLoading(true);
    try {
      const result = await api.efiling.precheckPacket({
        matterId: selectedMatterId,
        providerKey: selectedProviderKey || undefined,
        packetName: packetName || undefined,
        filingType: filingType || undefined,
        courtType: selectedMatter?.courtType || undefined,
        triggerDateUtc: new Date().toISOString(),
        documentIds: selectedDocumentIds,
        metadata: {
          caseNumber: metadataCaseNumber,
          partyRole: metadataPartyRole,
          filingType: filingType || 'general_filing'
        }
      });
      setPrecheckResult(result);
      if (result?.canSubmit) toast.success('Precheck passed.');
      else toast.error('Precheck returned issues.');
    } catch (error) {
      console.error(error);
      toast.error('Precheck failed.');
    } finally {
      setPrecheckLoading(false);
    }
  };

  const transitionSubmission = async () => {
    if (!selectedSubmissionId) return toast.error('Select a submission first.');
    setTransitionBusy(true);
    try {
      const res = await api.efiling.transitionSubmission(selectedSubmissionId, {
        targetStatus: transitionTarget,
        rejectionReason: transitionTarget === 'rejected' ? (transitionReason || undefined) : undefined
      });
      if (!res) throw new Error('Empty response');
      toast.success(`Submission moved to ${res.currentStatus}.`);
      await reloadAll();
      await loadTimeline(selectedSubmissionId);
    } catch (error) {
      console.error(error);
      toast.error('Failed to transition submission.');
    } finally {
      setTransitionBusy(false);
    }
  };

  const startRepair = async () => {
    if (!selectedSubmissionId) return toast.error('Select a submission first.');
    setRepairBusy(true);
    try {
      const res = await api.efiling.startRepair(selectedSubmissionId, { notes: repairNotes || undefined });
      if (!res) throw new Error('Empty response');
      toast.success('Rejection repair workflow started.');
      await reloadAll();
      await loadTimeline(selectedSubmissionId);
    } catch (error) {
      console.error(error);
      toast.error('Failed to start repair workflow.');
    } finally {
      setRepairBusy(false);
    }
  };

  const runDocketAutomation = async () => {
    if (!selectedMatterId) return toast.error('Select a matter first.');
    setDocketAutomationBusy(true);
    try {
      const res = await api.efiling.runDocketAutomation({
        matterId: selectedMatterId,
        providerKey: 'courtlistener-dockets',
        limit: 50
      });
      setDocketAutomationResult(res);
      toast.success('Docket automation completed.');
      await reloadAll();
    } catch (error) {
      console.error(error);
      toast.error('Failed to run docket automation.');
    } finally {
      setDocketAutomationBusy(false);
    }
  };

  const selectedSubmission = useMemo(
    () => submissions.find((s: any) => s.id === selectedSubmissionId) || null,
    [submissions, selectedSubmissionId]
  );
  const filingQueueCounts = useMemo(() => {
    const counts: Record<string, number> = { all: submissions.length };
    for (const s of submissions) {
      const key = String(s?.status || 'unknown').toLowerCase();
      counts[key] = (counts[key] || 0) + 1;
    }
    return counts;
  }, [submissions]);
  const filteredSubmissions = useMemo(() => {
    const list = [...submissions].sort((a: any, b: any) => {
      const aTs = a?.updatedAt || a?.submittedAt || a?.createdAt || '';
      const bTs = b?.updatedAt || b?.submittedAt || b?.createdAt || '';
      return String(bTs).localeCompare(String(aTs));
    });
    if (filingQueueFilter === 'all') return list;
    return list.filter((s: any) => String(s?.status || '').toLowerCase() === filingQueueFilter);
  }, [submissions, filingQueueFilter]);

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-6 space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h3 className="font-semibold text-slate-800">E-Filing Workspace & Docket Automation</h3>
          <p className="text-xs text-gray-500 mt-1">Packet precheck, submission timeline, rejection repair workflow, and docket-to-task/deadline automation.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <select value={selectedMatterId} onChange={(e) => setSelectedMatterId(e.target.value)} className="border border-gray-300 rounded px-3 py-2 text-sm min-w-[220px]" disabled={loadingMatters}>
            <option value="">Select matter...</option>
            {matters.map(m => <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>)}
          </select>
          <select value={selectedProviderKey} onChange={(e) => setSelectedProviderKey(e.target.value)} className="border border-gray-300 rounded px-3 py-2 text-sm min-w-[220px]">
            <option value="">Auto provider</option>
            {connectedEfilingProviders.map(i => <option key={i.id} value={i.providerKey || ''}>{i.provider}</option>)}
          </select>
          <button onClick={() => void reloadAll()} disabled={workspaceLoading || submissionsLoading || docketsLoading || !selectedMatterId} className="px-3 py-2 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50">Refresh</button>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        <div className="space-y-4">
          <div className="border border-gray-200 rounded-lg p-4 bg-gray-50/50">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-semibold text-slate-800">Filing Workspace (Packet Builder)</p>
                <p className="text-xs text-gray-500">Build a filing packet from matter documents and run court-aware precheck.</p>
              </div>
              <button type="button" onClick={applySuggestedPacket} disabled={!workspace?.suggestedPacket} className="px-2.5 py-1 text-[11px] font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50">Apply Suggestions</button>
            </div>

            {workspaceLoading ? (
              <p className="text-sm text-gray-500 mt-4">Loading workspace...</p>
            ) : !workspace ? (
              <p className="text-sm text-gray-500 mt-4">Select a matter to load filing workspace.</p>
            ) : (
              <div className="space-y-4 mt-4">
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div className="rounded border border-gray-200 bg-white p-3">
                    <p className="text-[11px] text-gray-500">Matter</p>
                    <p className="text-sm font-semibold text-slate-800">{workspace.matter.caseNumber} - {workspace.matter.name}</p>
                    <p className="text-xs text-gray-500">{workspace.matter.courtType || 'Court type not set'} · {workspace.matter.status}</p>
                  </div>
                  <div className="rounded border border-gray-200 bg-white p-3">
                    <p className="text-[11px] text-gray-500">Connections</p>
                    <div className="space-y-1 mt-1">
                      {workspace.connections.slice(0, 4).map(c => <div key={c.id} className="text-xs text-slate-700">{c.provider} · {c.status} · Sync {fmtDate(c.lastSyncAt)}</div>)}
                      {workspace.connections.length === 0 && <p className="text-xs text-gray-500">No e-filing/court connections found.</p>}
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Packet Name</label>
                    <input value={packetName} onChange={(e) => setPacketName(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm" placeholder="Filing packet name" />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Filing Type</label>
                    <input value={filingType} onChange={(e) => setFilingType(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm" placeholder="motion / complaint / notice / ..." />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Case Number (metadata)</label>
                    <input value={metadataCaseNumber} onChange={(e) => setMetadataCaseNumber(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm" />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-500 mb-1">Party Role</label>
                    <select value={metadataPartyRole} onChange={(e) => setMetadataPartyRole(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm">
                      <option>Plaintiff</option><option>Defendant</option><option>Petitioner</option><option>Respondent</option><option>Appellant</option><option>Appellee</option>
                    </select>
                  </div>
                </div>

                <div>
                  <div className="flex items-center justify-between mb-2">
                    <p className="text-xs font-semibold text-gray-500">Matter Documents ({workspace.documents.length})</p>
                    <p className="text-xs text-gray-500">{selectedDocumentIds.length} selected</p>
                  </div>
                  <div className="max-h-64 overflow-auto border border-gray-200 rounded bg-white divide-y">
                    {workspace.documents.map(doc => {
                      const checked = selectedDocumentIds.includes(doc.id);
                      return (
                        <label key={doc.id} className="flex items-start gap-3 p-3 text-sm hover:bg-gray-50 cursor-pointer">
                          <input type="checkbox" checked={checked} onChange={() => toggleDocument(doc.id)} className="mt-0.5" />
                          <div className="min-w-0 flex-1">
                            <div className="font-medium text-slate-800 truncate">{doc.name || doc.fileName}</div>
                            <div className="text-xs text-gray-500 truncate">{doc.fileName} · {(doc.fileSize / 1024).toFixed(1)} KB · {doc.mimeType || 'n/a'} · {doc.category || 'uncategorized'}</div>
                            {doc.tags && <div className="text-[11px] text-gray-400 truncate">Tags: {doc.tags}</div>}
                          </div>
                        </label>
                      );
                    })}
                    {workspace.documents.length === 0 && <p className="p-3 text-sm text-gray-500">No documents linked to this matter.</p>}
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button onClick={runPrecheck} disabled={precheckLoading || !selectedMatterId} className="px-3 py-2 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900 disabled:opacity-50">{precheckLoading ? 'Running Precheck...' : 'Run Precheck'}</button>
                  <button onClick={() => setPrecheckResult(null)} disabled={!precheckResult} className="px-3 py-2 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50">Clear Result</button>
                </div>

                {precheckResult && (
                  <div className="border border-gray-200 rounded-lg bg-white p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <p className="text-sm font-semibold text-slate-800">Precheck Result</p>
                      <span className={`text-xs font-semibold px-2 py-1 rounded-full ${precheckResult.canSubmit ? 'bg-emerald-100 text-emerald-700' : 'bg-amber-100 text-amber-700'}`}>{precheckResult.canSubmit ? 'Ready to Submit' : 'Needs Review'}</span>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-xs">
                      <div className="rounded border border-gray-200 p-3"><p className="font-semibold text-slate-700 mb-1">Errors ({precheckResult.errors?.length || 0})</p><ul className="space-y-1 text-red-700">{(precheckResult.errors || []).map(issue => <li key={`${issue.code}-${issue.message}`}>• {issue.message}</li>)}{(precheckResult.errors || []).length === 0 && <li className="text-gray-400">No blocking errors.</li>}</ul></div>
                      <div className="rounded border border-gray-200 p-3"><p className="font-semibold text-slate-700 mb-1">Warnings ({precheckResult.warnings?.length || 0})</p><ul className="space-y-1 text-amber-700">{(precheckResult.warnings || []).map(issue => <li key={`${issue.code}-${issue.message}`}>• {issue.message}</li>)}{(precheckResult.warnings || []).length === 0 && <li className="text-gray-400">No warnings.</li>}</ul></div>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                      <div className="rounded border border-gray-200 p-3"><p className="text-xs font-semibold text-slate-700 mb-2">Matched Court Rules</p><div className="space-y-2 max-h-40 overflow-auto">{(precheckResult.matchedRules || []).map(rule => <div key={rule.id} className="text-xs text-slate-700"><div className="font-semibold">{rule.name}</div><div className="text-gray-500">{rule.jurisdiction} · {rule.triggerEvent} · {rule.daysCount} {rule.dayType} days</div></div>)}{(precheckResult.matchedRules || []).length === 0 && <p className="text-xs text-gray-500">No rules matched.</p>}</div></div>
                      <div className="rounded border border-gray-200 p-3"><p className="text-xs font-semibold text-slate-700 mb-2">Suggested Deadlines</p><div className="space-y-2 max-h-40 overflow-auto">{(precheckResult.suggestedDeadlines || []).map(item => <div key={`${item.ruleId}-${item.dueDateUtc}`} className="text-xs text-slate-700"><div className="font-semibold">{item.ruleName}</div><div className="text-gray-500">Due: {fmtDate(item.dueDateUtc)} {item.triggerEvent ? `· ${item.triggerEvent}` : ''}</div></div>)}{(precheckResult.suggestedDeadlines || []).length === 0 && <p className="text-xs text-gray-500">No suggested deadlines.</p>}</div></div>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="border border-gray-200 rounded-lg p-4 bg-gray-50/50">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-semibold text-slate-800">Docket-to-Task/Deadline Automation</p>
                <p className="text-xs text-gray-500">Run CourtListener docket automation for the selected matter.</p>
              </div>
              <button onClick={runDocketAutomation} disabled={docketAutomationBusy || !selectedMatterId} className="px-3 py-2 text-xs font-semibold text-white bg-blue-700 rounded hover:bg-blue-800 disabled:opacity-50">{docketAutomationBusy ? 'Running...' : 'Run Automation'}</button>
            </div>
            <div className="mt-3 text-xs text-gray-500">{docketsLoading ? 'Loading dockets...' : `${dockets.length} docket entries loaded for this matter.`}</div>
            {docketAutomationResult && <div className="mt-3 grid grid-cols-2 md:grid-cols-4 gap-2 text-xs"><Metric label="Processed" value={String(docketAutomationResult.processed)} /><Metric label="Tasks" value={String(docketAutomationResult.tasksCreated)} /><Metric label="Deadlines" value={String(docketAutomationResult.deadlinesCreated)} /><Metric label="Reviews" value={String(docketAutomationResult.reviewsQueued)} /></div>}
            <div className="mt-3 max-h-40 overflow-auto border border-gray-200 rounded bg-white divide-y">
              {dockets.slice(0, 20).map((d: any) => <div key={d.id} className="p-2 text-xs"><div className="font-semibold text-slate-700">{d.docketNumber || d.externalDocketId}</div><div className="text-gray-500">{d.caseName || 'No case name'} · {d.court || 'Unknown court'}</div><div className="text-gray-400">Modified {fmtDate(d.modifiedAt || d.lastSeenAt)}</div></div>)}
              {dockets.length === 0 && <p className="p-3 text-xs text-gray-500">No docket entries for this matter yet.</p>}
            </div>
          </div>
        </div>

        <div className="space-y-4">
          <div className="border border-gray-200 rounded-lg p-4 bg-gray-50/50">
            <div className="flex items-center justify-between gap-2">
              <div>
                <p className="text-sm font-semibold text-slate-800">Filing Queue</p>
                <p className="text-xs text-gray-500">Draft/submitted/rejected/accepted queue with quick filtering and selection.</p>
              </div>
              {submissionsLoading && <span className="text-xs text-gray-500">Loading...</span>}
            </div>
            <div className="mt-3 grid grid-cols-2 md:grid-cols-5 gap-2">
              {[
                ['all', 'All'],
                ['draft', 'Draft'],
                ['submitted', 'Submitted'],
                ['accepted', 'Accepted'],
                ['rejected', 'Rejected']
              ].map(([key, label]) => {
                const active = filingQueueFilter === key;
                return (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setFilingQueueFilter(key)}
                    className={`rounded border px-2 py-2 text-left text-xs ${active ? 'border-blue-300 bg-blue-50' : 'border-gray-200 bg-white hover:bg-gray-50'}`}
                  >
                    <div className="text-gray-500">{label}</div>
                    <div className={`font-semibold ${active ? 'text-blue-800' : 'text-slate-800'}`}>{filingQueueCounts[key] || 0}</div>
                  </button>
                );
              })}
            </div>
            <div className="mt-3 max-h-[280px] overflow-auto border border-gray-200 rounded bg-white divide-y">
              {filteredSubmissions.length === 0 && (
                <p className="p-3 text-sm text-gray-500">No submissions in this queue.</p>
              )}
              {filteredSubmissions.map((s: any) => {
                const active = s.id === selectedSubmissionId;
                const status = String(s.status || 'unknown').toLowerCase();
                const statusClass =
                  status === 'accepted' ? 'bg-emerald-50 text-emerald-700' :
                  status === 'rejected' ? 'bg-red-50 text-red-700' :
                  status === 'submitted' || status === 'processing' ? 'bg-blue-50 text-blue-700' :
                  'bg-gray-100 text-gray-700';
                return (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => setSelectedSubmissionId(s.id)}
                    className={`w-full text-left p-3 hover:bg-gray-50 ${active ? 'bg-blue-50/50' : ''}`}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-semibold text-slate-800 truncate">{s.referenceNumber || s.externalSubmissionId || s.id}</span>
                          <span className={`px-1.5 py-0.5 rounded text-[11px] ${statusClass}`}>{s.status}</span>
                          <span className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-700 text-[11px]">{s.providerKey}</span>
                        </div>
                        <div className="mt-1 text-xs text-gray-500">
                          Submitted {fmtDate(s.submittedAt)} | Updated {fmtDate(s.updatedAt)}
                        </div>
                        {s.rejectionReason && <div className="mt-1 text-xs text-red-700 truncate">{s.rejectionReason}</div>}
                      </div>
                      {active && <span className="text-[11px] font-semibold text-blue-700">Selected</span>}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>
          <div className="border border-gray-200 rounded-lg p-4 bg-gray-50/50">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-semibold text-slate-800">Submission Tracking Timeline</p>
                <p className="text-xs text-gray-500">Review filing lifecycle, transition state, and trigger rejection repair workflow.</p>
              </div>
              {submissionsLoading && <span className="text-xs text-gray-500">Loading...</span>}
            </div>

            <div className="mt-3 space-y-3">
              <select value={selectedSubmissionId} onChange={(e) => setSelectedSubmissionId(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm" disabled={submissionsLoading}>
                <option value="">Select submission...</option>
                {submissions.map((s: any) => <option key={s.id} value={s.id}>{(s.referenceNumber || s.externalSubmissionId)} · {s.status} · {s.providerKey}</option>)}
              </select>

              {selectedSubmission && (
                <div className="rounded border border-gray-200 bg-white p-3 text-xs space-y-1">
                  <div><span className="font-semibold text-slate-700">Status:</span> {selectedSubmission.status}</div>
                  <div><span className="font-semibold text-slate-700">Reference:</span> {selectedSubmission.referenceNumber || selectedSubmission.externalSubmissionId}</div>
                  <div><span className="font-semibold text-slate-700">Submitted:</span> {fmtDate(selectedSubmission.submittedAt)}</div>
                  <div><span className="font-semibold text-slate-700">Accepted:</span> {fmtDate(selectedSubmission.acceptedAt)}</div>
                  <div><span className="font-semibold text-slate-700">Rejected:</span> {fmtDate(selectedSubmission.rejectedAt)}</div>
                  {selectedSubmission.rejectionReason && <div className="text-red-700"><span className="font-semibold">Reason:</span> {selectedSubmission.rejectionReason}</div>}
                </div>
              )}

              <div className="grid grid-cols-1 md:grid-cols-[1fr_auto] gap-2 items-start">
                <div className="space-y-2">
                  <div className="flex gap-2">
                    <select value={transitionTarget} onChange={(e) => setTransitionTarget(e.target.value)} className="flex-1 border border-gray-300 rounded p-2 text-sm" disabled={!selectedSubmissionId || transitionBusy}>
                      <option value="draft">draft</option><option value="submitted">submitted</option><option value="processing">processing</option><option value="accepted">accepted</option><option value="rejected">rejected</option><option value="corrected">corrected</option><option value="failed">failed</option>
                    </select>
                    <button onClick={transitionSubmission} disabled={!selectedSubmissionId || transitionBusy} className="px-3 py-2 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900 disabled:opacity-50">{transitionBusy ? 'Updating...' : 'Transition'}</button>
                  </div>
                  {(transitionTarget === 'rejected' || transitionTarget === 'failed') && <input value={transitionReason} onChange={(e) => setTransitionReason(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm" placeholder="Rejection reason / failure reason" />}
                </div>
                <div className="w-full md:w-64 space-y-2">
                  <textarea value={repairNotes} onChange={(e) => setRepairNotes(e.target.value)} className="w-full border border-gray-300 rounded p-2 text-sm min-h-[72px]" placeholder="Repair notes for corrected filing..." disabled={!selectedSubmissionId || repairBusy} />
                  <button onClick={startRepair} disabled={!selectedSubmissionId || repairBusy} className="w-full px-3 py-2 text-xs font-semibold text-amber-800 bg-amber-100 border border-amber-200 rounded hover:bg-amber-200 disabled:opacity-50">{repairBusy ? 'Starting...' : 'Start Rejection Repair'}</button>
                </div>
              </div>

              <div className="border border-gray-200 rounded bg-white">
                <div className="px-3 py-2 border-b border-gray-200 flex items-center justify-between"><p className="text-xs font-semibold text-slate-700">Timeline</p>{timelineLoading && <span className="text-xs text-gray-500">Loading...</span>}</div>
                <div className="max-h-[480px] overflow-auto divide-y">
                  {!selectedSubmissionId && <p className="p-3 text-sm text-gray-500">Select a submission to view timeline.</p>}
                  {selectedSubmissionId && !timelineLoading && (!timeline || !Array.isArray(timeline.timeline) || timeline.timeline.length === 0) && <p className="p-3 text-sm text-gray-500">No timeline events found.</p>}
                  {timeline?.timeline?.map((event, idx) => (
                    <div key={`${event.eventType}-${event.timestampUtc}-${idx}`} className="p-3 text-xs">
                      <div className="flex items-start justify-between gap-2">
                        <div>
                          <p className="font-semibold text-slate-800">{event.title || event.eventType}</p>
                          <p className="text-gray-500 mt-0.5">{event.source || 'system'} · {event.eventType}</p>
                        </div>
                        <div className="text-right text-gray-400 whitespace-nowrap">{fmtDate(event.timestampUtc)}</div>
                      </div>
                      {event.summary && <p className="text-gray-600 mt-1 whitespace-pre-wrap break-words">{String(event.summary)}</p>}
                      {event.status && <p className="text-[11px] mt-1 text-gray-500">Status: {String(event.status)}</p>}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default EfilingWorkflowPanel;


