import React, { useState, useEffect } from 'react';
import { Matter } from '../../types';
import { Clock, FileText, DollarSign } from '../Icons';
import { clientApi } from '../../services/clientApi';
import { useTranslation } from '../../contexts/LanguageContext';

type ClientTransparencySnapshot = {
  id: string;
  matterId: string;
  versionNumber: number;
  status: string;
  generatedAt: string;
  dataQuality?: string | null;
  confidenceScore?: number | null;
  summary?: string | null;
  whatChanged?: string | null;
};

type ClientTransparencyTimelineItem = {
  id: string;
  orderIndex: number;
  phaseKey: string;
  label: string;
  status: string;
  text?: string | null;
  startedAtUtc?: string | null;
  etaAtUtc?: string | null;
  completedAtUtc?: string | null;
};

type ClientTransparencyDelayReason = {
  id: string;
  code: string;
  severity: string;
  expectedDelayDays?: number | null;
  text?: string | null;
};

type ClientTransparencyNextStep = {
  id: string;
  ownerType: string;
  status: string;
  actionText: string;
  etaAtUtc?: string | null;
  blockedByText?: string | null;
};

type ClientTransparencyCostImpact = {
  currency: string;
  currentExpectedRangeMin?: number | null;
  currentExpectedRangeMax?: number | null;
  deltaRangeMin?: number | null;
  deltaRangeMax?: number | null;
  confidenceBand?: string | null;
  driverSummary?: string | null;
};

type ClientTransparencyEvidenceSourceRef = {
  source: string;
  entityId?: string | null;
  label?: string | null;
  isStale?: boolean;
  staleReason?: string | null;
  lastChangedAtUtc?: string | null;
};

type ClientTransparencyEvidenceSentence = {
  sentenceId: string;
  text: string;
  sourceRefs: ClientTransparencyEvidenceSourceRef[];
};

type ClientTransparencyEvidenceItemLink = {
  itemId: string;
  itemType: string;
  label?: string | null;
  text?: string | null;
  sourceRefs: ClientTransparencyEvidenceSourceRef[];
};

type ClientTransparencyEvidenceQuality = {
  coverage?: number;
  stale?: number;
  coveredSegments?: number;
  totalSegments?: number;
  totalSources?: number;
  staleSources?: number;
  reviewBurden?: number;
  dataQuality?: string | null;
};

type ClientTransparencyEvidenceBundle = {
  summarySentences?: ClientTransparencyEvidenceSentence[];
  whatChangedSentences?: ClientTransparencyEvidenceSentence[];
  delayReasonLinks?: ClientTransparencyEvidenceItemLink[];
  timelineLinks?: ClientTransparencyEvidenceItemLink[];
  nextStepLink?: ClientTransparencyEvidenceItemLink | null;
  costImpactLink?: ClientTransparencyEvidenceItemLink | null;
  staleSources?: ClientTransparencyEvidenceSourceRef[];
  allSources?: ClientTransparencyEvidenceSourceRef[];
  quality?: ClientTransparencyEvidenceQuality | null;
};

type ClientTransparencyResponse = {
  snapshot: ClientTransparencySnapshot | null;
  pendingReview?: boolean;
  pendingReviewReason?: string | null;
  riskFlags: string[];
  timeline: ClientTransparencyTimelineItem[];
  delayReasons: ClientTransparencyDelayReason[];
  nextStep: ClientTransparencyNextStep | null;
  costImpact: ClientTransparencyCostImpact | null;
  evidence?: ClientTransparencyEvidenceBundle | null;
};

const ClientMatters: React.FC = () => {
  const { language } = useTranslation();
  const [matters, setMatters] = useState<Matter[]>([]);
  const [selectedMatter, setSelectedMatter] = useState<Matter | null>(null);
  const [loading, setLoading] = useState(true);
  const [transparency, setTransparency] = useState<ClientTransparencyResponse | null>(null);
  const [transparencyLoading, setTransparencyLoading] = useState(false);
  const [transparencyError, setTransparencyError] = useState<string | null>(null);
  const [showTransparencyEvidence, setShowTransparencyEvidence] = useState(false);

  useEffect(() => {
    const loadMatters = async () => {
      try {
        const data = await clientApi.fetchJson('/matters');
        setMatters(Array.isArray(data) ? data : []);
      } catch (error) {
        console.error('Error loading matters:', error);
      } finally {
        setLoading(false);
      }
    };
    
    loadMatters();
  }, []);

  useEffect(() => {
    let disposed = false;
    let intervalId: number | null = null;

    const loadTransparency = async (silent = false) => {
      if (!selectedMatter?.id) {
        if (!disposed) {
          setTransparency(null);
          setTransparencyError(null);
          setTransparencyLoading(false);
        }
        return;
      }

      if (!silent && !disposed) {
        setTransparencyLoading(true);
      }
      if (!disposed) {
        setTransparencyError(null);
      }
      try {
        const data = await clientApi.fetchJson(`/matters/${selectedMatter.id}/transparency?lang=${encodeURIComponent(language)}`);
        if (!disposed) {
          setTransparency(data as ClientTransparencyResponse);
        }
      } catch (error) {
        console.error('Error loading client transparency snapshot:', error);
        if (!disposed) {
          setTransparencyError('Transparency summary is currently unavailable.');
          setTransparency(null);
        }
      } finally {
        if (!silent && !disposed) {
          setTransparencyLoading(false);
        }
      }
    };

    void loadTransparency();
    intervalId = window.setInterval(() => {
      void loadTransparency(true);
    }, 30000);

    return () => {
      disposed = true;
      if (intervalId != null) {
        window.clearInterval(intervalId);
      }
    };
  }, [selectedMatter?.id, language]);

  const formatEvidenceSource = (ref: ClientTransparencyEvidenceSourceRef) => {
    const base = [ref.source, ref.label || ref.entityId].filter(Boolean).join(': ');
    if (ref.isStale) {
      return `${base} (stale: ${(ref.staleReason || 'unknown').replace(/_/g, ' ')})`;
    }
    return base;
  };

  const renderEvidenceRefs = (refs?: ClientTransparencyEvidenceSourceRef[] | null) => {
    if (!refs || refs.length === 0) {
      return <div className="text-[11px] text-gray-400">No linked source evidence.</div>;
    }
    return (
      <div className="flex flex-wrap gap-1">
        {refs.slice(0, 6).map((ref, idx) => (
          <span key={`${ref.source}-${ref.entityId || idx}`} className={`px-2 py-1 rounded text-[10px] border ${ref.isStale ? 'bg-rose-50 border-rose-200 text-rose-700' : 'bg-slate-50 border-slate-200 text-slate-600'}`}>
            {formatEvidenceSource(ref)}
          </span>
        ))}
        {refs.length > 6 && (
          <span className="px-2 py-1 rounded text-[10px] border bg-white border-gray-200 text-gray-500">+{refs.length - 6} more</span>
        )}
      </div>
    );
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  if (selectedMatter) {
    const allTimeEntries = selectedMatter.timeEntries || [];
    const allExpenses = selectedMatter.expenses || [];
    const unbilledTime = allTimeEntries.filter(te => !te.billed);
    const unbilledExpenses = allExpenses.filter(e => !e.billed);
    const billedTime = allTimeEntries.filter(te => te.billed);
    const billedExpenses = allExpenses.filter(e => e.billed);
    const totalUnbilled = unbilledTime.reduce((sum, te) => sum + (te.duration * te.rate / 60), 0) +
                          unbilledExpenses.reduce((sum, e) => sum + e.amount, 0);
    const totalBilled = billedTime.reduce((sum, te) => sum + (te.duration * te.rate / 60), 0) +
                        billedExpenses.reduce((sum, e) => sum + e.amount, 0);

    return (
      <div className="p-8 h-full overflow-y-auto">
        <button 
          onClick={() => setSelectedMatter(null)}
          className="mb-4 text-blue-600 hover:text-blue-800 font-medium"
        >
          &lt;- Back to Cases
        </button>

        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mb-6">
          <div className="flex justify-between items-start mb-4">
            <div>
              <h2 className="text-2xl font-bold text-slate-900">{selectedMatter.name}</h2>
              <p className="text-gray-600 mt-1">Case #: {selectedMatter.caseNumber}</p>
            </div>
            <span className={`px-4 py-2 rounded-full text-sm font-bold ${
              selectedMatter.status === 'Open' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'
            }`}>
              {selectedMatter.status}
            </span>
          </div>

          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mt-6">
            <div>
              <div className="text-sm text-gray-600">Practice Area</div>
              <div className="font-semibold text-slate-900">{selectedMatter.practiceArea}</div>
            </div>
            <div>
              <div className="text-sm text-gray-600">Fee Structure</div>
              <div className="font-semibold text-slate-900">{selectedMatter.feeStructure}</div>
            </div>
            <div>
              <div className="text-sm text-gray-600">Open Date</div>
              <div className="font-semibold text-slate-900">{new Date(selectedMatter.openDate).toLocaleDateString()}</div>
            </div>
            <div>
              <div className="text-sm text-gray-600">Attorney</div>
              <div className="font-semibold text-slate-900">{selectedMatter.responsibleAttorney}</div>
            </div>
          </div>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mb-6">
          <div className="flex items-start justify-between gap-4 mb-4">
            <div>
              <h3 className="text-lg font-bold text-slate-900">Transparency Summary</h3>
              <p className="text-sm text-gray-600 mt-1">
                Plain-language status, next step, delay factors, and expected cost impact.
              </p>
            </div>
            {transparency?.snapshot && (
              <div className="text-right">
                <div className="text-xs text-gray-500">Version {transparency.snapshot.versionNumber}</div>
                <div className="text-xs text-gray-500">
                  {new Date(transparency.snapshot.generatedAt).toLocaleString()}
                </div>
              </div>
            )}
          </div>

          {transparencyLoading ? (
            <div className="text-sm text-gray-500">Loading transparency summary...</div>
          ) : transparencyError ? (
            <div className="text-sm text-red-600">{transparencyError}</div>
          ) : !transparency?.snapshot ? (
            <div className="text-sm text-gray-500">
              {transparency?.pendingReview
                ? `Transparency summary is being reviewed before publishing${transparency.pendingReviewReason ? ` (${String(transparency.pendingReviewReason).replaceAll('_', ' ')})` : ''}.`
                : 'No transparency summary available yet.'}
            </div>
          ) : (
            <div className="space-y-5">
              <div className="rounded-lg border border-blue-200 bg-blue-50 p-4">
                <div className="text-sm text-slate-800">{transparency.snapshot.summary}</div>
                {transparency.snapshot.whatChanged && (
                  <div className="text-xs text-blue-700 mt-2">{transparency.snapshot.whatChanged}</div>
                )}
              </div>

              <div className="flex flex-wrap gap-2 items-center">
                <span className={`px-2 py-1 rounded text-xs font-semibold ${
                  transparency.snapshot.dataQuality === 'high' ? 'bg-green-100 text-green-700' :
                  transparency.snapshot.dataQuality === 'medium' ? 'bg-amber-100 text-amber-700' :
                  'bg-gray-100 text-gray-700'
                }`}>
                  Data quality: {transparency.snapshot.dataQuality ?? 'unknown'}
                </span>
                <span className="px-2 py-1 rounded text-xs font-semibold bg-indigo-100 text-indigo-700">
                  Confidence: {typeof transparency.snapshot.confidenceScore === 'number'
                    ? `${Math.round(transparency.snapshot.confidenceScore * 100)}%`
                    : 'n/a'}
                </span>
                {(transparency.riskFlags || []).map(flag => (
                  <span key={flag} className="px-2 py-1 rounded text-xs font-semibold bg-rose-100 text-rose-700">
                    {flag.replace(/_/g, ' ')}
                  </span>
                ))}
                {transparency.evidence?.quality && (
                  <>
                    <span className="px-2 py-1 rounded text-xs font-semibold bg-slate-100 text-slate-700">
                      Evidence coverage: {Math.round(Number(transparency.evidence.quality.coverage || 0) * 100)}%
                    </span>
                    <span className={`px-2 py-1 rounded text-xs font-semibold ${Number(transparency.evidence.quality.stale || 0) > 0 ? 'bg-rose-100 text-rose-700' : 'bg-green-100 text-green-700'}`}>
                      Stale sources: {transparency.evidence.quality.staleSources ?? 0}
                    </span>
                  </>
                )}
              </div>

              {transparency.evidence && (
                <div className="rounded-lg border border-slate-200 p-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-sm font-semibold text-slate-900">How We Know (Evidence)</div>
                      <div className="text-xs text-gray-500">Source links behind this client-facing summary.</div>
                    </div>
                    <button
                      type="button"
                      onClick={() => setShowTransparencyEvidence(prev => !prev)}
                      className="px-3 py-1.5 rounded border border-slate-200 text-xs font-semibold bg-white hover:bg-slate-50"
                    >
                      {showTransparencyEvidence ? 'Hide' : 'Show'}
                    </button>
                  </div>
                  {showTransparencyEvidence && (
                    <div className="mt-3 space-y-3">
                      {(transparency.evidence.summarySentences || []).map(sentence => (
                        <div key={sentence.sentenceId} className="rounded border border-blue-100 bg-blue-50/40 p-2">
                          <div className="text-xs text-slate-800">{sentence.text}</div>
                          <div className="mt-2">{renderEvidenceRefs(sentence.sourceRefs)}</div>
                        </div>
                      ))}
                      {(transparency.evidence.whatChangedSentences || []).length > 0 && (
                        <div>
                          <div className="text-[11px] font-semibold text-slate-600 mb-1">What changed evidence</div>
                          {(transparency.evidence.whatChangedSentences || []).map(sentence => (
                            <div key={sentence.sentenceId} className="rounded border border-indigo-100 bg-indigo-50/30 p-2 mb-2">
                              <div className="text-xs text-slate-800">{sentence.text}</div>
                              <div className="mt-2">{renderEvidenceRefs(sentence.sourceRefs)}</div>
                            </div>
                          ))}
                        </div>
                      )}
                      {(transparency.evidence.staleSources || []).length > 0 && (
                        <div className="rounded border border-rose-200 bg-rose-50 p-2">
                          <div className="text-[11px] font-semibold text-rose-700 mb-1">Source freshness warnings</div>
                          {renderEvidenceRefs(transparency.evidence.staleSources)}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
                <div className="lg:col-span-2">
                  <div className="text-sm font-semibold text-slate-900 mb-2">Process Map</div>
                  <div className="space-y-3">
                    {transparency.timeline.map(item => (
                      <div key={item.id} className="rounded-lg border border-gray-200 p-3">
                        <div className="flex items-center justify-between gap-2">
                          <div className="font-medium text-slate-900 text-sm">{item.label}</div>
                          <span className={`px-2 py-1 rounded text-[11px] font-bold uppercase ${
                            item.status === 'completed' ? 'bg-green-100 text-green-700' :
                            item.status === 'in_progress' ? 'bg-blue-100 text-blue-700' :
                            item.status === 'blocked' ? 'bg-red-100 text-red-700' :
                            'bg-gray-100 text-gray-700'
                          }`}>
                            {item.status.replace('_', ' ')}
                          </span>
                        </div>
                        {item.text && <div className="text-xs text-gray-700 mt-2">{item.text}</div>}
                        <div className="text-xs text-gray-500 mt-2 flex flex-wrap gap-3">
                          {item.startedAtUtc && <span>Started: {new Date(item.startedAtUtc).toLocaleDateString()}</span>}
                          {item.etaAtUtc && <span>ETA: {new Date(item.etaAtUtc).toLocaleDateString()}</span>}
                          {item.completedAtUtc && <span>Completed: {new Date(item.completedAtUtc).toLocaleDateString()}</span>}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="space-y-4">
                  <div className="rounded-lg border border-gray-200 p-4">
                    <div className="text-sm font-semibold text-slate-900 mb-2">Next Step</div>
                    {transparency.nextStep ? (
                      <>
                        <div className="text-sm text-gray-800">{transparency.nextStep.actionText}</div>
                        <div className="text-xs text-gray-500 mt-2">
                          Owner: {transparency.nextStep.ownerType.replace('_', ' ')}
                          {transparency.nextStep.etaAtUtc && ` • ETA ${new Date(transparency.nextStep.etaAtUtc).toLocaleDateString()}`}
                        </div>
                        {transparency.nextStep.blockedByText && (
                          <div className="text-xs text-amber-700 mt-2">{transparency.nextStep.blockedByText}</div>
                        )}
                      </>
                    ) : (
                      <div className="text-sm text-gray-500">No next step is currently published.</div>
                    )}
                  </div>

                  <div className="rounded-lg border border-gray-200 p-4">
                    <div className="text-sm font-semibold text-slate-900 mb-2">Delay Factors</div>
                    {transparency.delayReasons.length > 0 ? (
                      <div className="space-y-2">
                        {transparency.delayReasons.map(delay => (
                          <div key={delay.id} className="rounded-md bg-amber-50 border border-amber-200 p-2">
                            <div className="text-xs font-semibold text-amber-800">
                              {delay.code.replace(/_/g, ' ')} ({delay.severity})
                            </div>
                            {delay.text && <div className="text-xs text-amber-900 mt-1">{delay.text}</div>}
                            {typeof delay.expectedDelayDays === 'number' && (
                              <div className="text-[11px] text-amber-700 mt-1">Expected impact: ~{delay.expectedDelayDays} day(s)</div>
                            )}
                            {showTransparencyEvidence && (
                              <div className="mt-2">
                                {renderEvidenceRefs((transparency.evidence?.delayReasonLinks || []).find(link => link.itemId === delay.id)?.sourceRefs)}
                              </div>
                            )}
                          </div>
                        ))}
                      </div>
                    ) : (
                      <div className="text-sm text-gray-500">No active delay factor is currently flagged.</div>
                    )}
                  </div>

                  <div className="rounded-lg border border-gray-200 p-4">
                    <div className="text-sm font-semibold text-slate-900 mb-2">Expected Cost Impact</div>
                    {transparency.costImpact ? (
                      <>
                        <div className="text-lg font-bold text-slate-900">
                          {transparency.costImpact.currency} {Number(transparency.costImpact.currentExpectedRangeMin ?? 0).toLocaleString()} - {Number(transparency.costImpact.currentExpectedRangeMax ?? 0).toLocaleString()}
                        </div>
                        <div className="text-xs text-gray-500 mt-1">
                          Confidence: {transparency.costImpact.confidenceBand ?? 'n/a'}
                        </div>
                        {(transparency.costImpact.deltaRangeMin != null || transparency.costImpact.deltaRangeMax != null) && (
                          <div className="text-xs mt-2 text-gray-700">
                            Delta vs current forecast: {transparency.costImpact.currency} {Number(transparency.costImpact.deltaRangeMin ?? 0).toLocaleString()} to {Number(transparency.costImpact.deltaRangeMax ?? 0).toLocaleString()}
                          </div>
                        )}
                        {transparency.costImpact.driverSummary && (
                          <div className="text-xs text-gray-600 mt-2">{transparency.costImpact.driverSummary}</div>
                        )}
                        {showTransparencyEvidence && (
                          <div className="mt-2">
                            {renderEvidenceRefs(transparency.evidence?.costImpactLink?.sourceRefs)}
                          </div>
                        )}
                      </>
                    ) : (
                      <div className="text-sm text-gray-500">No cost impact estimate is available yet.</div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Time Entries & Expenses */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
          <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6">
            <h3 className="text-lg font-bold text-slate-900 mb-4 flex items-center gap-2">
              <Clock className="w-5 h-5" /> Time Entries
            </h3>
            
            {/* Unbilled Time Entries */}
            {unbilledTime.length > 0 && (
              <div className="mb-4">
                <div className="text-xs font-bold text-amber-600 uppercase mb-2">Unbilled</div>
                <div className="space-y-2">
                  {unbilledTime.slice(0, 5).map(te => (
                    <div key={te.id} className="p-3 bg-amber-50 border border-amber-200 rounded-lg">
                      <div className="text-sm font-medium text-slate-900">{te.description}</div>
                      <div className="text-xs text-gray-600 mt-1">
                        {new Date(te.date).toLocaleDateString()} - {Math.floor(te.duration / 60)}h {te.duration % 60}m @ ${te.rate}/hr
                      </div>
                      <div className="text-xs font-semibold text-amber-700 mt-1">
                        ${((te.duration * te.rate) / 60).toFixed(2)}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            
            {/* Billed Time Entries */}
            {billedTime.length > 0 && (
              <div>
                <div className="text-xs font-bold text-green-600 uppercase mb-2">Billed</div>
                <div className="space-y-2">
                  {billedTime.slice(0, 5).map(te => (
                    <div key={te.id} className="p-3 bg-green-50 border border-green-200 rounded-lg">
                      <div className="text-sm font-medium text-slate-900">{te.description}</div>
                      <div className="text-xs text-gray-600 mt-1">
                        {new Date(te.date).toLocaleDateString()} - {Math.floor(te.duration / 60)}h {te.duration % 60}m @ ${te.rate}/hr
                      </div>
                      <div className="text-xs font-semibold text-green-700 mt-1">
                        ${((te.duration * te.rate) / 60).toFixed(2)} (Billed)
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            
            {allTimeEntries.length === 0 && (
              <p className="text-gray-400 text-sm">No time entries</p>
            )}
          </div>

          <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6">
            <h3 className="text-lg font-bold text-slate-900 mb-4 flex items-center gap-2">
              <DollarSign className="w-5 h-5" /> Expenses
            </h3>
            
            {/* Unbilled Expenses */}
            {unbilledExpenses.length > 0 && (
              <div className="mb-4">
                <div className="text-xs font-bold text-amber-600 uppercase mb-2">Unbilled</div>
                <div className="space-y-2">
                  {unbilledExpenses.slice(0, 5).map(exp => (
                    <div key={exp.id} className="p-3 bg-amber-50 border border-amber-200 rounded-lg">
                      <div className="text-sm font-medium text-slate-900">{exp.description}</div>
                      <div className="text-xs text-gray-600 mt-1">
                        {new Date(exp.date).toLocaleDateString()} - {exp.category}
                      </div>
                      <div className="text-xs font-semibold text-amber-700 mt-1">
                        ${exp.amount.toFixed(2)}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            
            {/* Billed Expenses */}
            {billedExpenses.length > 0 && (
              <div>
                <div className="text-xs font-bold text-green-600 uppercase mb-2">Billed</div>
                <div className="space-y-2">
                  {billedExpenses.slice(0, 5).map(exp => (
                    <div key={exp.id} className="p-3 bg-green-50 border border-green-200 rounded-lg">
                      <div className="text-sm font-medium text-slate-900">{exp.description}</div>
                      <div className="text-xs text-gray-600 mt-1">
                        {new Date(exp.date).toLocaleDateString()} - {exp.category}
                      </div>
                      <div className="text-xs font-semibold text-green-700 mt-1">
                        ${exp.amount.toFixed(2)} (Billed)
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            
            {allExpenses.length === 0 && (
              <p className="text-gray-400 text-sm">No expenses</p>
            )}
          </div>
        </div>
        
        {/* Calendar Events for this Matter */}
        {selectedMatter.events && selectedMatter.events.length > 0 && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mb-6">
            <h3 className="text-lg font-bold text-slate-900 mb-4">Upcoming Events</h3>
            <div className="space-y-2">
              {selectedMatter.events
                .filter((e: any) => new Date(e.date) >= new Date())
                .slice(0, 5)
                .map((event: any) => (
                  <div key={event.id} className="p-3 bg-blue-50 border border-blue-200 rounded-lg">
                    <div className="flex justify-between items-start">
                      <div>
                        <div className="text-sm font-medium text-slate-900">{event.title}</div>
                        <div className="text-xs text-gray-600 mt-1">
                          {new Date(event.date).toLocaleDateString()} {new Date(event.date).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                        </div>
                      </div>
                      <span className={`px-2 py-1 rounded text-[10px] uppercase font-bold
                        ${event.type === 'Court' ? 'bg-red-100 text-red-700' : 
                          event.type === 'Deadline' ? 'bg-amber-100 text-amber-700' : 
                          'bg-blue-100 text-blue-700'
                        }`}>
                        {event.type}
                      </span>
                    </div>
                  </div>
                ))}
            </div>
          </div>
        )}

        {/* Summary Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <div className="bg-amber-50 border border-amber-200 rounded-xl p-6">
            <div className="flex justify-between items-center">
              <div>
                <div className="text-sm text-amber-600 font-medium">Unbilled Work in Progress</div>
                <div className="text-2xl font-bold text-amber-900 mt-1">${totalUnbilled.toFixed(2)}</div>
              </div>
              <div className="text-sm text-amber-600">
                This amount will appear on your next invoice
              </div>
            </div>
          </div>
          
          <div className="bg-green-50 border border-green-200 rounded-xl p-6">
            <div className="flex justify-between items-center">
              <div>
                <div className="text-sm text-green-600 font-medium">Total Billed</div>
                <div className="text-2xl font-bold text-green-900 mt-1">${totalBilled.toFixed(2)}</div>
              </div>
              <div className="text-sm text-green-600">
                Already invoiced
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-slate-900">My Cases</h2>
        <p className="text-gray-600 mt-1">View details of your legal matters</p>
      </div>

      {matters.length === 0 ? (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
          <p className="text-gray-400">No cases found</p>
        </div>
      ) : (
        <div className="space-y-4">
          {matters.map(matter => (
            <div 
              key={matter.id} 
              onClick={() => setSelectedMatter(matter)}
              className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 hover:shadow-md transition-shadow cursor-pointer"
            >
              <div className="flex justify-between items-start">
                <div className="flex-1">
                  <h3 className="text-lg font-bold text-slate-900">{matter.name}</h3>
                  <p className="text-sm text-gray-600 mt-1">Case #: {matter.caseNumber}</p>
                  <div className="flex items-center gap-4 mt-3 text-sm text-gray-600">
                    <span>{matter.practiceArea}</span>
                    <span>-</span>
                    <span>Opened: {new Date(matter.openDate).toLocaleDateString()}</span>
                  </div>
                </div>
                <div className="flex flex-col items-end gap-2">
                  <span className={`px-3 py-1 rounded-full text-xs font-bold ${
                    matter.status === 'Open' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'
                  }`}>
                    {matter.status}
                  </span>
                  <span className="text-xs text-gray-500">Click to view details</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default ClientMatters;

