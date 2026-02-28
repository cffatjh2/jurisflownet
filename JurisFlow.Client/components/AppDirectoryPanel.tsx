import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  AppDirectoryListing,
  AppDirectoryOnboardingRequest,
  AppDirectoryOnboardingResponse,
  AppDirectorySubmission
} from '../types';
import { api } from '../services/api';
import { toast } from './Toast';
import { AlertTriangle, CheckCircle, RefreshCw, X } from './Icons';

interface AppDirectoryPanelProps {
  isAdmin: boolean;
}

interface OnboardingDraft {
  providerKey: string;
  name: string;
  category: string;
  connectionMode: 'oauth' | 'api_key' | 'hybrid';
  summary: string;
  supportsWebhook: boolean;
  webhookFirst: boolean;
  fallbackPollingMinutes: string;
  capabilitiesText: string;
  slaTier: string;
  slaResponseHours: string;
  slaResolutionHours: string;
  slaUptimePercent: string;
}

const createDraft = (): OnboardingDraft => ({
  providerKey: '',
  name: '',
  category: 'General',
  connectionMode: 'oauth',
  summary: '',
  supportsWebhook: false,
  webhookFirst: false,
  fallbackPollingMinutes: '360',
  capabilitiesText: '',
  slaTier: 'standard',
  slaResponseHours: '24',
  slaResolutionHours: '72',
  slaUptimePercent: '99.5'
});

const parseNumber = (value: string): number | undefined => {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
};

const parseCapabilities = (raw: string): string[] => {
  return Array.from(
    new Set(
      raw
        .split(/[,\n]/)
        .map(item => item.trim())
        .filter(Boolean)
    )
  );
};

const listingBadge = (status: string) => {
  const s = (status || '').toLowerCase();
  if (s === 'published') return 'bg-emerald-100 text-emerald-700';
  if (s === 'approved') return 'bg-sky-100 text-sky-700';
  if (s === 'in_review') return 'bg-amber-100 text-amber-700';
  if (s === 'changes_requested') return 'bg-orange-100 text-orange-700';
  if (s === 'rejected') return 'bg-red-100 text-red-700';
  if (s === 'suspended') return 'bg-rose-100 text-rose-700';
  return 'bg-gray-100 text-gray-600';
};

const AppDirectoryPanel: React.FC<AppDirectoryPanelProps> = ({ isAdmin }) => {
  const [view, setView] = useState<'directory' | 'review'>('directory');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [onboardingOpen, setOnboardingOpen] = useState(false);
  const [draft, setDraft] = useState<OnboardingDraft>(createDraft);
  const [listings, setListings] = useState<AppDirectoryListing[]>([]);
  const [reviewQueue, setReviewQueue] = useState<AppDirectoryListing[]>([]);
  const [expandedListingId, setExpandedListingId] = useState<string | null>(null);
  const [submissionsLoadingId, setSubmissionsLoadingId] = useState<string | null>(null);
  const [submissionsByListing, setSubmissionsByListing] = useState<Record<string, AppDirectorySubmission[]>>({});
  const [lastHarness, setLastHarness] = useState<AppDirectoryOnboardingResponse | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [listingResult, queueResult] = await Promise.allSettled([
        api.appDirectory.listListings(true),
        api.appDirectory.getReviewQueue()
      ]);

      setListings(listingResult.status === 'fulfilled' && Array.isArray(listingResult.value) ? listingResult.value : []);
      setReviewQueue(queueResult.status === 'fulfilled' && Array.isArray(queueResult.value) ? queueResult.value : []);
    } catch (error) {
      console.error('Failed to load app directory', error);
      toast.error('Failed to load app directory.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const summary = useMemo(() => ({
    total: listings.length,
    published: listings.filter(item => item.status.toLowerCase() === 'published').length,
    review: listings.filter(item => item.status.toLowerCase() === 'in_review').length
  }), [listings]);

  const closeOnboarding = () => {
    setOnboardingOpen(false);
    setDraft(createDraft());
  };

  const handleSubmit = async () => {
    if (!draft.providerKey.trim() || !draft.name.trim() || !draft.category.trim() || !draft.summary.trim()) {
      toast.error('Provider key, name, category and summary are required.');
      return;
    }

    const payload: AppDirectoryOnboardingRequest = {
      manifest: {
        providerKey: draft.providerKey.trim().toLowerCase(),
        name: draft.name.trim(),
        category: draft.category.trim(),
        connectionMode: draft.connectionMode,
        summary: draft.summary.trim(),
        supportsWebhook: draft.supportsWebhook,
        webhookFirst: draft.webhookFirst,
        fallbackPollingMinutes: parseNumber(draft.fallbackPollingMinutes),
        capabilities: parseCapabilities(draft.capabilitiesText),
        manifestVersion: '1.0'
      },
      sla: {
        tier: draft.slaTier.trim() || 'standard',
        responseHours: parseNumber(draft.slaResponseHours),
        resolutionHours: parseNumber(draft.slaResolutionHours),
        uptimePercent: parseNumber(draft.slaUptimePercent)
      }
    };

    try {
      setSaving(true);
      const result = await api.appDirectory.submitOnboarding(payload);
      if (result) {
        setLastHarness(result);
        closeOnboarding();
        await loadData();
        toast.success(`Listing submitted. ${result.harness.summary}`);
      }
    } catch (error: any) {
      console.error('Failed to submit listing', error);
      toast.error(error?.message || 'Failed to submit listing.');
    } finally {
      setSaving(false);
    }
  };

  const handleRetest = async (listing: AppDirectoryListing) => {
    try {
      setSaving(true);
      const result = await api.appDirectory.retestListing(listing.id);
      if (result) setLastHarness(result);
      await loadData();
      toast.success(`${listing.name} retested.`);
    } catch (error: any) {
      console.error('Failed to retest listing', error);
      toast.error(error?.message || 'Failed to retest listing.');
    } finally {
      setSaving(false);
    }
  };

  const handleToggleSubmissions = async (listingId: string) => {
    if (expandedListingId === listingId) {
      setExpandedListingId(null);
      return;
    }

    setExpandedListingId(listingId);
    if (submissionsByListing[listingId]) return;

    try {
      setSubmissionsLoadingId(listingId);
      const rows = await api.appDirectory.getSubmissions(listingId);
      setSubmissionsByListing(prev => ({ ...prev, [listingId]: Array.isArray(rows) ? rows : [] }));
    } catch (error: any) {
      console.error('Failed to load submissions', error);
      toast.error(error?.message || 'Failed to load run history.');
    } finally {
      setSubmissionsLoadingId(null);
    }
  };

  const handleReview = async (
    listing: AppDirectoryListing,
    decision: 'approve' | 'reject' | 'request_changes' | 'suspend',
    publish: boolean = false
  ) => {
    if (!isAdmin) return;
    const notes = window.prompt('Review notes (optional):') || '';
    try {
      setSaving(true);
      await api.appDirectory.reviewListing(listing.id, {
        decision,
        publish,
        notes: notes.trim() || undefined
      });
      await loadData();
      toast.success(`${listing.name} updated.`);
    } catch (error: any) {
      console.error('Failed to review listing', error);
      toast.error(error?.message || 'Failed to update listing.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between flex-wrap gap-3">
        <div>
          <h2 className="text-xl font-bold text-slate-800 mb-1">App Directory</h2>
          <p className="text-sm text-gray-500">Partner onboarding, test harness runs, and listing review queue.</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={loadData}
            disabled={loading || saving}
            className="px-3 py-2 text-xs font-semibold border border-gray-300 rounded-lg text-slate-700 hover:bg-white disabled:opacity-50 flex items-center gap-1"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
          <button
            onClick={() => setOnboardingOpen(true)}
            className="px-3 py-2 text-xs font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900"
          >
            Submit Listing
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        <div className="bg-white border border-gray-200 rounded-lg p-4">
          <p className="text-xs text-gray-500">Total Listings</p>
          <p className="text-xl font-bold text-slate-900 mt-1">{summary.total}</p>
        </div>
        <div className="bg-white border border-gray-200 rounded-lg p-4">
          <p className="text-xs text-gray-500">Published</p>
          <p className="text-xl font-bold text-emerald-700 mt-1">{summary.published}</p>
        </div>
        <div className="bg-white border border-gray-200 rounded-lg p-4">
          <p className="text-xs text-gray-500">In Review</p>
          <p className="text-xl font-bold text-amber-700 mt-1">{summary.review}</p>
        </div>
      </div>

      {lastHarness && (
        <div className={`rounded-lg border p-4 ${lastHarness.harness.passed ? 'border-emerald-200 bg-emerald-50' : 'border-red-200 bg-red-50'}`}>
          <div className="flex items-center gap-2">
            {lastHarness.harness.passed ? <CheckCircle className="w-5 h-5 text-emerald-700" /> : <AlertTriangle className="w-5 h-5 text-red-700" />}
            <p className={`text-sm font-semibold ${lastHarness.harness.passed ? 'text-emerald-800' : 'text-red-800'}`}>
              {lastHarness.listing.name}: {lastHarness.harness.summary}
            </p>
          </div>
        </div>
      )}

      <div className="bg-white border border-gray-200 rounded-xl p-4">
        <div className="flex items-center gap-2">
          <button onClick={() => setView('directory')} className={`px-3 py-1.5 text-xs font-semibold rounded ${view === 'directory' ? 'bg-slate-800 text-white' : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>Listings</button>
          <button onClick={() => setView('review')} className={`px-3 py-1.5 text-xs font-semibold rounded ${view === 'review' ? 'bg-slate-800 text-white' : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>Review Queue ({reviewQueue.length})</button>
        </div>

        {view === 'directory' && (
          <div className="mt-4 space-y-3">
            {loading ? (
              <p className="text-sm text-gray-500">Loading listings...</p>
            ) : listings.length === 0 ? (
              <p className="text-sm text-gray-500">No listings found.</p>
            ) : (
              listings.map(listing => (
                <div key={listing.id} className="border border-gray-200 rounded-lg p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-slate-800">{listing.name} <span className="text-xs text-gray-500">({listing.providerKey})</span></p>
                      <p className="text-xs text-gray-500 mt-1">{listing.category} · {listing.connectionMode} · {listing.lastTestStatus || 'not_run'}</p>
                    </div>
                    <span className={`text-[11px] font-semibold px-2 py-1 rounded-full ${listingBadge(listing.status)}`}>{listing.status}</span>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <button onClick={() => handleRetest(listing)} disabled={saving} className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50">Retest</button>
                    <button onClick={() => handleToggleSubmissions(listing.id)} disabled={submissionsLoadingId === listing.id} className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50">{expandedListingId === listing.id ? 'Hide Runs' : 'View Runs'}</button>
                  </div>
                  {expandedListingId === listing.id && (
                    <div className="mt-3 pt-3 border-t border-gray-200 text-xs text-gray-600 space-y-1">
                      {submissionsLoadingId === listing.id ? (
                        <p>Loading run history...</p>
                      ) : (submissionsByListing[listing.id] || []).length === 0 ? (
                        <p>No run history.</p>
                      ) : (
                        (submissionsByListing[listing.id] || []).map(row => (
                          <p key={row.id}>{row.status} · {row.testStatus} · {new Date(row.createdAt).toLocaleString('en-US')}</p>
                        ))
                      )}
                    </div>
                  )}
                </div>
              ))
            )}
          </div>
        )}

        {view === 'review' && (
          <div className="mt-4 space-y-3">
            {loading ? (
              <p className="text-sm text-gray-500">Loading review queue...</p>
            ) : reviewQueue.length === 0 ? (
              <p className="text-sm text-gray-500">Review queue is empty.</p>
            ) : (
              reviewQueue.map(listing => (
                <div key={listing.id} className="border border-gray-200 rounded-lg p-4">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-slate-800">{listing.name}</p>
                      <p className="text-xs text-gray-500 mt-1">{listing.providerKey} · {listing.lastTestSummary || 'No test summary'}</p>
                    </div>
                    <span className={`text-[11px] font-semibold px-2 py-1 rounded-full ${listingBadge(listing.status)}`}>{listing.status}</span>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <button onClick={() => handleRetest(listing)} disabled={saving} className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50">Retest</button>
                    {isAdmin && (
                      <>
                        <button onClick={() => handleReview(listing, 'request_changes')} disabled={saving} className="px-3 py-1 text-xs font-semibold text-orange-700 border border-orange-200 rounded hover:bg-orange-50 disabled:opacity-50">Request Changes</button>
                        <button onClick={() => handleReview(listing, 'reject')} disabled={saving} className="px-3 py-1 text-xs font-semibold text-red-700 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50">Reject</button>
                        <button onClick={() => handleReview(listing, 'approve')} disabled={saving} className="px-3 py-1 text-xs font-semibold text-sky-700 border border-sky-200 rounded hover:bg-sky-50 disabled:opacity-50">Approve</button>
                        <button onClick={() => handleReview(listing, 'approve', true)} disabled={saving} className="px-3 py-1 text-xs font-semibold text-emerald-700 border border-emerald-200 rounded hover:bg-emerald-50 disabled:opacity-50">Approve & Publish</button>
                      </>
                    )}
                  </div>
                </div>
              ))
            )}
          </div>
        )}
      </div>

      {onboardingOpen && (
        <div className="fixed inset-0 bg-black/40 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-xl w-full max-w-2xl p-6 max-h-[90vh] overflow-y-auto">
            <div className="flex items-start justify-between mb-4">
              <div>
                <h3 className="text-lg font-bold text-slate-900">Submit App Listing</h3>
                <p className="text-xs text-gray-500 mt-1">Manifest and SLA info for review queue.</p>
              </div>
              <button onClick={closeOnboarding} className="text-gray-400 hover:text-gray-600"><X className="w-4 h-4" /></button>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
              <input className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Provider key*" value={draft.providerKey} onChange={e => setDraft(prev => ({ ...prev, providerKey: e.target.value }))} />
              <input className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Name*" value={draft.name} onChange={e => setDraft(prev => ({ ...prev, name: e.target.value }))} />
              <input className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Category*" value={draft.category} onChange={e => setDraft(prev => ({ ...prev, category: e.target.value }))} />
              <select className="border border-gray-300 rounded-lg p-2.5 text-sm bg-white" value={draft.connectionMode} onChange={e => setDraft(prev => ({ ...prev, connectionMode: e.target.value as 'oauth' | 'api_key' | 'hybrid' }))}>
                <option value="oauth">oauth</option>
                <option value="api_key">api_key</option>
                <option value="hybrid">hybrid</option>
              </select>
              <input className="md:col-span-2 border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Summary*" value={draft.summary} onChange={e => setDraft(prev => ({ ...prev, summary: e.target.value }))} />
              <textarea className="md:col-span-2 border border-gray-300 rounded-lg p-2.5 text-sm min-h-[80px]" placeholder="Capabilities (comma or new line)" value={draft.capabilitiesText} onChange={e => setDraft(prev => ({ ...prev, capabilitiesText: e.target.value }))} />
              <input type="number" className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Fallback polling minutes" value={draft.fallbackPollingMinutes} onChange={e => setDraft(prev => ({ ...prev, fallbackPollingMinutes: e.target.value }))} />
              <div className="flex items-center gap-4 text-xs">
                <label className="inline-flex items-center gap-2"><input type="checkbox" checked={draft.supportsWebhook} onChange={e => setDraft(prev => ({ ...prev, supportsWebhook: e.target.checked }))} />Supports webhook</label>
                <label className="inline-flex items-center gap-2"><input type="checkbox" checked={draft.webhookFirst} onChange={e => setDraft(prev => ({ ...prev, webhookFirst: e.target.checked }))} />Webhook first</label>
              </div>
            </div>

            <div className="mt-4 grid grid-cols-1 md:grid-cols-4 gap-3">
              <input className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="SLA tier" value={draft.slaTier} onChange={e => setDraft(prev => ({ ...prev, slaTier: e.target.value }))} />
              <input type="number" className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Response h" value={draft.slaResponseHours} onChange={e => setDraft(prev => ({ ...prev, slaResponseHours: e.target.value }))} />
              <input type="number" className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Resolution h" value={draft.slaResolutionHours} onChange={e => setDraft(prev => ({ ...prev, slaResolutionHours: e.target.value }))} />
              <input type="number" step="0.01" className="border border-gray-300 rounded-lg p-2.5 text-sm" placeholder="Uptime %" value={draft.slaUptimePercent} onChange={e => setDraft(prev => ({ ...prev, slaUptimePercent: e.target.value }))} />
            </div>

            <div className="flex items-center justify-end gap-2 mt-5">
              <button onClick={closeOnboarding} className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800">Cancel</button>
              <button onClick={handleSubmit} disabled={saving} className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50">{saving ? 'Submitting...' : 'Submit For Review'}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default AppDirectoryPanel;
