import React, { useState, useRef, useEffect, useMemo } from 'react';
import { createLegalChatSession } from '../services/geminiService';
import { Matter, DocumentFile } from '../types';
import { BrainCircuit, FileText, Send, Paperclip, Search, Scale, File, X, Sparkles, Briefcase, CheckSquare, Edit } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { toast } from './Toast';
import { api } from '../services/api';

interface AIDrafterProps {
    matters: Matter[];
}

interface ChatMsg {
    id: string;
    role: 'user' | 'model';
    text: string;
    sources?: { title: string, uri: string }[];
    timestamp: Date;
}

interface EvidenceDraftClaimGraph {
    claim: any;
    evidenceLinks: any[];
    ruleCitations: any[];
}

interface EvidenceDraftGraphResponse {
    session: any;
    output: any;
    claims: EvidenceDraftClaimGraph[];
    verificationRuns: any[];
    summary: {
        claimCount: number;
        criticalClaimCount: number;
        evidenceLinkCount: number;
        ruleCitationCount: number;
        unsupportedCriticalClaims: number;
    };
}

const AIDrafter: React.FC<AIDrafterProps> = ({ matters }) => {
    const { t } = useTranslation();
    const { documents } = useData();

    // Chat State
    const [messages, setMessages] = useState<ChatMsg[]>([
        {
            id: 'welcome',
            role: 'model',
            text: "Hello, counselor. I am Juris, your AI Associate. I can help you draft documents, summarize depositions, or research case law. Select documents from the right to give me context.",
            timestamp: new Date()
        }
    ]);
    const [inputValue, setInputValue] = useState('');
    const [isTyping, setIsTyping] = useState(false);

    // Context State
    const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
    const [useSearch, setUseSearch] = useState(false);
    const [showEvidenceDrafting, setShowEvidenceDrafting] = useState(false);
    const [evidencePrompt, setEvidencePrompt] = useState('');
    const [evidenceMatterId, setEvidenceMatterId] = useState<string>('');
    const [isGeneratingEvidenceDraft, setIsGeneratingEvidenceDraft] = useState(false);
    const [isVerifyingEvidenceDraft, setIsVerifyingEvidenceDraft] = useState(false);
    const [evidenceDraftGraph, setEvidenceDraftGraph] = useState<EvidenceDraftGraphResponse | null>(null);
    const [selectedEvidenceClaimId, setSelectedEvidenceClaimId] = useState<string | null>(null);
    const [evidencePipelineMeta, setEvidencePipelineMeta] = useState<any>(null);
    const [reviewerNotes, setReviewerNotes] = useState('');
    const [approverReason, setApproverReason] = useState('');
    const [rewriteClaimText, setRewriteClaimText] = useState('');
    const [isReviewingClaim, setIsReviewingClaim] = useState(false);
    const [publishPolicy, setPublishPolicy] = useState<'warn_only' | 'block_on_unsupported_critical' | 'block_on_low_confidence'>('warn_only');
    const [lowConfidenceThreshold, setLowConfidenceThreshold] = useState<number>(0.55);
    const [isPublishingEvidenceDraft, setIsPublishingEvidenceDraft] = useState(false);
    const [lastPublishResult, setLastPublishResult] = useState<any>(null);
    const [isExportingEvidenceDraft, setIsExportingEvidenceDraft] = useState(false);
    const [isBatchReverifyingEvidenceDrafts, setIsBatchReverifyingEvidenceDrafts] = useState(false);
    const [lastBatchReverifyResult, setLastBatchReverifyResult] = useState<any>(null);
    const [evidenceDraftMetrics, setEvidenceDraftMetrics] = useState<any>(null);
    const [isLoadingEvidenceDraftMetrics, setIsLoadingEvidenceDraftMetrics] = useState(false);
    const [evidenceDraftMetricsDays, setEvidenceDraftMetricsDays] = useState<number>(30);

    const messagesEndRef = useRef<HTMLDivElement>(null);

    // Scroll to bottom on new message
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages]);

    const handleSendMessage = async () => {
        if (!inputValue.trim()) return;

        const userText = inputValue;
        setInputValue(''); // Clear input

        // 1. Add User Message
        const userMsg: ChatMsg = {
            id: Date.now().toString(),
            role: 'user',
            text: userText,
            timestamp: new Date()
        };
        setMessages(prev => [...prev, userMsg]);
        setIsTyping(true);

        // 2. Prepare Context (Mocking document content reading)
        const selectedDocs = documents.filter(d => selectedDocIds.includes(d.id));
        const contextString = selectedDocs.map(d => `[Document: ${d.name} (${d.type})]`).join(', ');

        // 3. Prepare History for API
        const apiHistory = messages.map(m => ({
            role: m.role,
            parts: [{ text: m.text }]
        }));

        // 4. Call API
        const response = await createLegalChatSession(apiHistory, userText, contextString, useSearch);

        // 5. Add Model Message
        const modelMsg: ChatMsg = {
            id: (Date.now() + 1).toString(),
            role: 'model',
            text: response.text,
            sources: response.sources,
            timestamp: new Date()
        };

        setMessages(prev => [...prev, modelMsg]);
        setIsTyping(false);
    };

    const toggleDocSelection = (id: string) => {
        setSelectedDocIds(prev =>
            prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
        );
    };

    const selectedEvidenceClaim = evidenceDraftGraph?.claims?.find(c => c.claim?.id === selectedEvidenceClaimId) ?? null;
    const latestVerificationPayload = useMemo(() => {
        const raw = evidenceDraftGraph?.verificationRuns?.[0]?.resultJson;
        if (!raw || typeof raw !== 'string') return null;
        try { return JSON.parse(raw); } catch { return null; }
    }, [evidenceDraftGraph]);
    const verificationClaimResults: any[] = Array.isArray(latestVerificationPayload?.claims) ? latestVerificationPayload.claims : [];
    const evidenceMismatchClaims = verificationClaimResults.filter((r: any) =>
        Array.isArray(r?.evidenceMismatches) && r.evidenceMismatches.length > 0);
    const advancedMismatchClaims = verificationClaimResults.filter((r: any) =>
        (Array.isArray(r?.crossCheckMismatches) && r.crossCheckMismatches.length > 0) ||
        (Array.isArray(r?.outdatedSourceMismatches) && r.outdatedSourceMismatches.length > 0));
    const unsupportedClaims = (evidenceDraftGraph?.claims || []).filter(c => c.claim?.status === 'unsupported');
    const selectedClaimVerificationResult = verificationClaimResults.find((r: any) => r?.claimId === selectedEvidenceClaimId) ?? null;

    const refreshEvidenceDraftMetrics = async (days: number = evidenceDraftMetricsDays) => {
        try {
            setIsLoadingEvidenceDraftMetrics(true);
            const metrics = await api.ai.evidenceDrafts.metrics({ days });
            setEvidenceDraftMetrics(metrics);
        } catch (err: any) {
            toast.error(err?.message || 'Failed to load evidence drafting metrics.');
        } finally {
            setIsLoadingEvidenceDraftMetrics(false);
        }
    };

    useEffect(() => {
        if (showEvidenceDrafting) {
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
        }
    }, [showEvidenceDrafting]); // eslint-disable-line react-hooks/exhaustive-deps

    const parseClaimRefIndex = (ref: string): number | null => {
        const match = ref.match(/\[CLM-(\d+)\]/i);
        if (!match) return null;
        const n = Number(match[1]);
        return Number.isFinite(n) ? n - 1 : null;
    };

    const handleEvidenceMarkerClick = (marker: string) => {
        const idx = parseClaimRefIndex(marker);
        if (idx === null || !evidenceDraftGraph?.claims?.[idx]?.claim?.id) return;
        setSelectedEvidenceClaimId(evidenceDraftGraph.claims[idx].claim.id);
    };

    const renderEvidenceDraftText = (text?: string) => {
        if (!text) return null;
        const parts = text.split(/(\[CLM-\d+\])/g);
        return parts.map((part, idx) => {
            if (/^\[CLM-\d+\]$/i.test(part)) {
                const claimIdx = parseClaimRefIndex(part);
                const claim = claimIdx !== null ? evidenceDraftGraph?.claims?.[claimIdx] : null;
                const isSelected = !!claim && claim.claim?.id === selectedEvidenceClaimId;
                return (
                    <button
                        key={`${part}-${idx}`}
                        type="button"
                        onClick={() => handleEvidenceMarkerClick(part)}
                        className={`mx-1 px-2 py-0.5 rounded-full text-[11px] font-bold border ${isSelected ? 'bg-indigo-600 text-white border-indigo-600' : 'bg-indigo-50 text-indigo-700 border-indigo-200 hover:bg-indigo-100'}`}
                        title="Open evidence for this claim"
                    >
                        {part.replace('[', '').replace(']', '')}
                    </button>
                );
            }
            return <React.Fragment key={`txt-${idx}`}>{part}</React.Fragment>;
        });
    };

    const handleGenerateEvidenceDraft = async () => {
        if (!evidencePrompt.trim()) {
            toast.warning('Provide a drafting prompt for evidence-linked generation.');
            return;
        }
        if (selectedDocIds.length === 0) {
            toast.warning('Select at least one document from the sidebar.');
            return;
        }

        try {
            setIsGeneratingEvidenceDraft(true);
            const response: any = await api.ai.evidenceDrafts.generate({
                prompt: evidencePrompt.trim(),
                matterId: evidenceMatterId || undefined,
                title: 'Evidence-Linked Draft (MVP)',
                purpose: 'drafting',
                selectedDocumentIds: selectedDocIds,
                autoVerify: true,
                topChunksPerDocument: 4,
                maxClaims: 6,
            });

            const graph: EvidenceDraftGraphResponse | null = response?.graph ?? null;
            setEvidenceDraftGraph(graph);
            setEvidencePipelineMeta(response?.pipeline ?? null);
            setSelectedEvidenceClaimId(graph?.claims?.[0]?.claim?.id ?? null);
            setShowEvidenceDrafting(true);
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
            toast.success('Evidence-linked draft generated.');
        } catch (err: any) {
            toast.error(err?.message || 'Failed to generate evidence-linked draft.');
        } finally {
            setIsGeneratingEvidenceDraft(false);
        }
    };

    const handleVerifyEvidenceDraft = async () => {
        const outputId = evidenceDraftGraph?.output?.id;
        if (!outputId) return;
        try {
            setIsVerifyingEvidenceDraft(true);
            const response: any = await api.ai.evidenceDrafts.verify(outputId, { createReviewQueueItems: true });
            const graph: EvidenceDraftGraphResponse | null = response?.graph ?? null;
            setEvidenceDraftGraph(graph);
            setEvidencePipelineMeta((prev: any) => ({
                ...(prev || {}),
                verificationSummary: response?.verificationSummary ?? prev?.verificationSummary ?? null,
            }));
            setSelectedEvidenceClaimId(prev => prev && graph?.claims?.some(c => c.claim?.id === prev) ? prev : (graph?.claims?.[0]?.claim?.id ?? null));
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
            toast.success('Evidence verifier completed.');
        } catch (err: any) {
            toast.error(err?.message || 'Failed to verify evidence-linked draft.');
        } finally {
            setIsVerifyingEvidenceDraft(false);
        }
    };

    const handleReviewClaim = async (action: 'approve' | 'reject' | 'rewrite') => {
        const draftId = evidenceDraftGraph?.output?.id;
        const claimId = selectedEvidenceClaim?.claim?.id;
        if (!draftId || !claimId) return;
        if (action === 'rewrite' && !rewriteClaimText.trim()) {
            toast.warning('Provide rewritten claim text.');
            return;
        }

        try {
            setIsReviewingClaim(true);
            const response: any = await api.ai.evidenceDrafts.reviewClaim(draftId, claimId, {
                action,
                reviewerNotes: reviewerNotes || undefined,
                approverReason: approverReason || undefined,
                rewrittenText: action === 'rewrite' ? rewriteClaimText.trim() : undefined,
                statusOverride: action === 'approve' ? 'supported' : undefined,
            });
            const graph: EvidenceDraftGraphResponse | null = response?.graph ?? null;
            setEvidenceDraftGraph(graph);
            setSelectedEvidenceClaimId(claimId);
            if (action === 'rewrite') {
                setRewriteClaimText('');
            }
            setReviewerNotes('');
            setApproverReason('');
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
            toast.success(`Claim ${action}d.`);
        } catch (err: any) {
            toast.error(err?.message || `Failed to ${action} claim.`);
        } finally {
            setIsReviewingClaim(false);
        }
    };

    const handlePublishEvidenceDraft = async () => {
        const draftId = evidenceDraftGraph?.output?.id;
        if (!draftId) return;
        try {
            setIsPublishingEvidenceDraft(true);
            const response: any = await api.ai.evidenceDrafts.publish(draftId, {
                policy: publishPolicy,
                lowConfidenceThreshold,
            });
            setLastPublishResult(response?.publish ?? response);
            if (response?.graph) {
                setEvidenceDraftGraph(response.graph);
            }
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
            toast.success('Evidence-linked draft published.');
        } catch (err: any) {
            setLastPublishResult(err ?? null);
            toast.error(err?.message || 'Publish blocked or failed.');
        } finally {
            setIsPublishingEvidenceDraft(false);
        }
    };

    const handleExportEvidenceDraft = async () => {
        const draftId = evidenceDraftGraph?.output?.id;
        if (!draftId) return;
        try {
            setIsExportingEvidenceDraft(true);
            const payload: any = await api.ai.evidenceDrafts.exportEvidence(draftId);
            const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `evidence-draft-${draftId}.json`;
            a.click();
            URL.revokeObjectURL(url);
            toast.success('Evidence export downloaded.');
        } catch (err: any) {
            toast.error(err?.message || 'Failed to export evidence.');
        } finally {
            setIsExportingEvidenceDraft(false);
        }
    };

    const handleBatchReverifyEvidenceDrafts = async () => {
        try {
            setIsBatchReverifyingEvidenceDrafts(true);
            const response: any = await api.ai.evidenceDrafts.batchReverify({
                days: evidenceDraftMetricsDays,
                limit: 25,
                createReviewQueueItems: true,
            });
            setLastBatchReverifyResult(response);
            if (evidenceDraftGraph?.output?.id) {
                try {
                    const refreshed = await api.ai.evidenceDrafts.get(evidenceDraftGraph.output.id);
                    setEvidenceDraftGraph(refreshed as any);
                } catch {
                    // non-blocking refresh
                }
            }
            void refreshEvidenceDraftMetrics(evidenceDraftMetricsDays);
            toast.success(`Batch re-verify finished (${response?.processedCount ?? 0} ok / ${response?.failedCount ?? 0} failed).`);
        } catch (err: any) {
            toast.error(err?.message || 'Failed to batch re-verify evidence drafts.');
        } finally {
            setIsBatchReverifyingEvidenceDrafts(false);
        }
    };

    const handleQuickAction = (action: 'summarize' | 'research' | 'draft' | 'analyze' | 'tasks' | 'template') => {
        let prompt = "";
        if (action === 'summarize') {
            if (selectedDocIds.length === 0) {
                toast.warning('Please select a document from the sidebar to summarize.');
                return;
            }
            prompt = "Please provide a detailed summary of the selected documents, highlighting key legal facts, dates, and inconsistencies.";
        } else if (action === 'research') {
            setUseSearch(true);
            prompt = "I need to research case law regarding... (please complete)";
        } else if (action === 'draft') {
            prompt = "Draft a formal demand letter based on the selected case context. The details are...";
        } else if (action === 'analyze') {
            // Case Analysis - predict outcome based on facts
            prompt = `Analyze this case and provide:
1. STRENGTHS: Key factors favoring our client
2. WEAKNESSES: Potential vulnerabilities
3. SIMILAR CASES: Reference relevant precedents
4. OUTCOME PREDICTION: Estimated probability of success (High/Medium/Low)
5. RECOMMENDED STRATEGY: Next steps to maximize chances

Case details: [Please describe the case facts, parties involved, and legal issues]`;
        } else if (action === 'tasks') {
            // Suggest tasks based on case type
            prompt = `Based on this matter, suggest a comprehensive task list:

1. IMMEDIATE ACTIONS: Tasks needed in the next 7 days
2. DISCOVERY PHASE: Document requests, depositions, interrogatories
3. MOTIONS: Potential motions to file
4. DEADLINES: Key dates and statute of limitations
5. CLIENT COMMUNICATIONS: Scheduled updates and meetings

Matter type: [Specify: Litigation/Corporate/Family Law/Criminal/IP/Estate]
Current status: [Specify current stage of the case]`;
        } else if (action === 'template') {
            // Template-based document generation
            prompt = `Generate a legal document using the following template:

DOCUMENT TYPE: [Motion to Dismiss / Demand Letter / Settlement Agreement / Contract / NDA]
CLIENT NAME: 
OPPOSING PARTY: 
CASE NUMBER: 
COURT/JURISDICTION: 
KEY FACTS: 
RELIEF REQUESTED: 

Please fill in the brackets and I will generate a complete, professionally formatted document.`;
        }
        setInputValue(prompt);
    };

    return (
        <div className="h-full flex bg-gray-50 overflow-hidden relative">

            {/* LEFT: MAIN CHAT AREA */}
            <div className="flex-1 flex flex-col min-w-0">

                {/* Header */}
                <div className="bg-white border-b border-gray-200 px-6 py-4 flex justify-between items-center shadow-sm z-10">
                    <div className="flex items-center gap-3">
                        <div className="bg-slate-900 p-2 rounded-lg">
                            <BrainCircuit className="w-5 h-5 text-white" />
                        </div>
                        <div>
                            <h2 className="font-bold text-slate-800 text-lg">AI Legal Associate</h2>
                            <p className="text-xs text-gray-500 flex items-center gap-2">
                                <span className="w-2 h-2 rounded-full bg-green-500"></span> Online • Gemini 2.5 Flash
                            </p>
                        </div>
                    </div>

                    {/* Quick Actions */}
                    <div className="flex gap-2 flex-wrap">
                        <button onClick={() => handleQuickAction('summarize')} className="px-3 py-1.5 bg-indigo-50 text-indigo-700 text-xs font-bold rounded-full hover:bg-indigo-100 transition-colors flex items-center gap-1">
                            <FileText className="w-3 h-3" /> Summarize
                        </button>
                        <button onClick={() => handleQuickAction('research')} className="px-3 py-1.5 bg-purple-50 text-purple-700 text-xs font-bold rounded-full hover:bg-purple-100 transition-colors flex items-center gap-1">
                            <Scale className="w-3 h-3" /> Research
                        </button>
                        <button onClick={() => handleQuickAction('draft')} className="px-3 py-1.5 bg-blue-50 text-blue-700 text-xs font-bold rounded-full hover:bg-blue-100 transition-colors flex items-center gap-1">
                            <Sparkles className="w-3 h-3" /> Draft
                        </button>
                        <button onClick={() => handleQuickAction('analyze')} className="px-3 py-1.5 bg-amber-50 text-amber-700 text-xs font-bold rounded-full hover:bg-amber-100 transition-colors flex items-center gap-1">
                            <Briefcase className="w-3 h-3" /> Case Analysis
                        </button>
                        <button onClick={() => handleQuickAction('tasks')} className="px-3 py-1.5 bg-green-50 text-green-700 text-xs font-bold rounded-full hover:bg-green-100 transition-colors flex items-center gap-1">
                            <CheckSquare className="w-3 h-3" /> Suggest Tasks
                        </button>
                        <button onClick={() => handleQuickAction('template')} className="px-3 py-1.5 bg-rose-50 text-rose-700 text-xs font-bold rounded-full hover:bg-rose-100 transition-colors flex items-center gap-1">
                            <Edit className="w-3 h-3" /> Template
                        </button>
                        <button onClick={() => setShowEvidenceDrafting(v => !v)} className={`px-3 py-1.5 text-xs font-bold rounded-full transition-colors flex items-center gap-1 border ${showEvidenceDrafting ? 'bg-indigo-600 text-white border-indigo-600' : 'bg-white text-indigo-700 border-indigo-200 hover:bg-indigo-50'}`}>
                            <Sparkles className="w-3 h-3" /> Evidence Draft
                        </button>
                    </div>
                </div>

                {/* Chat Stream */}
                <div className="flex-1 overflow-y-auto p-6 space-y-6 bg-slate-50 scroll-smooth">
                    {showEvidenceDrafting && (
                        <div className="max-w-4xl mx-auto bg-white border border-indigo-200 rounded-2xl shadow-sm overflow-hidden">
                            <div className="px-5 py-4 border-b border-indigo-100 bg-gradient-to-r from-indigo-50 to-blue-50">
                                <div className="flex items-center justify-between gap-3 flex-wrap">
                                    <div>
                                        <h3 className="font-bold text-slate-800">Evidence-Linked Drafting (MVP)</h3>
                                        <p className="text-xs text-slate-500">Claim-to-evidence draft generation with verifier and visible unsupported critical claims.</p>
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <button
                                            onClick={() => refreshEvidenceDraftMetrics(evidenceDraftMetricsDays)}
                                            disabled={isLoadingEvidenceDraftMetrics}
                                            className={`px-3 py-1.5 rounded-lg text-xs font-bold border ${isLoadingEvidenceDraftMetrics ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50'}`}
                                        >
                                            {isLoadingEvidenceDraftMetrics ? 'Refreshing...' : 'Refresh Metrics'}
                                        </button>
                                        <button
                                            onClick={handleBatchReverifyEvidenceDrafts}
                                            disabled={isBatchReverifyingEvidenceDrafts}
                                            className={`px-3 py-1.5 rounded-lg text-xs font-bold border ${isBatchReverifyingEvidenceDrafts ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-amber-700 border-amber-200 hover:bg-amber-50'}`}
                                        >
                                            {isBatchReverifyingEvidenceDrafts ? 'Batch Verifying...' : `Batch Re-Verify (${evidenceDraftMetricsDays}d)`}
                                        </button>
                                        <button
                                            onClick={handleExportEvidenceDraft}
                                            disabled={!evidenceDraftGraph?.output?.id || isExportingEvidenceDraft}
                                            className={`px-3 py-1.5 rounded-lg text-xs font-bold border ${!evidenceDraftGraph?.output?.id || isExportingEvidenceDraft ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50'}`}
                                        >
                                            {isExportingEvidenceDraft ? 'Exporting...' : 'Export JSON'}
                                        </button>
                                        <button
                                            onClick={handleVerifyEvidenceDraft}
                                            disabled={!evidenceDraftGraph?.output?.id || isVerifyingEvidenceDraft}
                                            className={`px-3 py-1.5 rounded-lg text-xs font-bold border ${!evidenceDraftGraph?.output?.id || isVerifyingEvidenceDraft ? 'bg-gray-100 text-gray-400 border-gray-200' : 'bg-white text-indigo-700 border-indigo-200 hover:bg-indigo-50'}`}
                                        >
                                            {isVerifyingEvidenceDraft ? 'Verifying...' : 'Re-Verify'}
                                        </button>
                                        <button
                                            onClick={handleGenerateEvidenceDraft}
                                            disabled={isGeneratingEvidenceDraft}
                                            className={`px-3 py-1.5 rounded-lg text-xs font-bold ${isGeneratingEvidenceDraft ? 'bg-gray-200 text-gray-500' : 'bg-indigo-600 text-white hover:bg-indigo-700'}`}
                                        >
                                            {isGeneratingEvidenceDraft ? 'Generating...' : 'Generate Evidence Draft'}
                                        </button>
                                    </div>
                                </div>
                            </div>

                            <div className="p-5 space-y-4">
                                <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                                    <div className="md:col-span-2">
                                        <label className="text-xs font-bold text-gray-500 uppercase">Draft Prompt</label>
                                        <textarea
                                            value={evidencePrompt}
                                            onChange={(e) => setEvidencePrompt(e.target.value)}
                                            className="mt-1 w-full min-h-[90px] rounded-xl border border-gray-300 bg-white px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                            placeholder="Draft a motion summary / filing narrative / demand letter section with evidence-linked claims..."
                                        />
                                    </div>
                                    <div>
                                        <label className="text-xs font-bold text-gray-500 uppercase">Matter (optional)</label>
                                        <select
                                            value={evidenceMatterId}
                                            onChange={(e) => setEvidenceMatterId(e.target.value)}
                                            className="mt-1 w-full rounded-xl border border-gray-300 bg-white px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                                        >
                                            <option value="">No matter selected</option>
                                            {matters.map(m => (
                                                <option key={m.id} value={m.id}>{m.name || m.id}</option>
                                            ))}
                                        </select>
                                        <div className="mt-3 p-3 rounded-xl border border-gray-200 bg-gray-50 text-xs text-gray-600">
                                            <div className="font-bold text-gray-700 mb-1">Selected Context</div>
                                            <div>{selectedDocIds.length} document(s) selected</div>
                                            {evidencePipelineMeta?.retrieval && (
                                                <div className="mt-1 text-gray-500">
                                                    Last run: {evidencePipelineMeta.retrieval.documentCount} docs / {evidencePipelineMeta.retrieval.chunkCount} chunks / {evidencePipelineMeta.retrieval.rulePackCount} rule packs
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                </div>

                                {evidenceDraftGraph?.summary?.unsupportedCriticalClaims > 0 && (
                                    <div className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
                                        <span className="font-bold">Unsupported critical claims detected:</span> {evidenceDraftGraph.summary.unsupportedCriticalClaims}. Review before use/export.
                                    </div>
                                )}

                                {(evidencePipelineMeta?.verificationSummary?.staleCitationCount ?? 0) > 0 && (
                                    <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                                        <span className="font-bold">Stale rule citations detected:</span> {evidencePipelineMeta.verificationSummary.staleCitationCount} citation(s). Re-verify after refreshing rule citations.
                                    </div>
                                )}

                                {evidencePipelineMeta?.ruleAwareness?.requiresHumanReview && (
                                    <div className="rounded-xl border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm text-indigo-800">
                                        <span className="font-bold">Jurisdiction coverage review required:</span> low-confidence or coverage gap detected for court/rule context.
                                        {evidencePipelineMeta?.ruleAwareness?.coverageReviewQueueItemId && (
                                            <span className="ml-1 text-indigo-600">Review Queue ID: {String(evidencePipelineMeta.ruleAwareness.coverageReviewQueueItemId).slice(0, 12)}...</span>
                                        )}
                                    </div>
                                )}

                                <div className="rounded-xl border border-slate-200 bg-slate-50 p-4">
                                    <div className="flex items-center justify-between gap-3 flex-wrap">
                                        <div>
                                            <div className="text-xs font-bold text-slate-500 uppercase">Advanced Verification & Quality Metrics</div>
                                            <div className="text-[11px] text-slate-500">Claim coverage, unsupported trend, reviewer burden, contradiction/outdated-source signals, citation stability.</div>
                                        </div>
                                        <div className="flex items-center gap-2">
                                            <select
                                                value={evidenceDraftMetricsDays}
                                                onChange={(e) => setEvidenceDraftMetricsDays(Number(e.target.value) || 30)}
                                                className="rounded-lg border border-slate-300 px-2 py-1 text-xs bg-white"
                                            >
                                                <option value={30}>30d</option>
                                                <option value={60}>60d</option>
                                                <option value={90}>90d</option>
                                            </select>
                                            <button
                                                type="button"
                                                onClick={() => refreshEvidenceDraftMetrics(evidenceDraftMetricsDays)}
                                                disabled={isLoadingEvidenceDraftMetrics}
                                                className={`px-2.5 py-1.5 rounded-lg text-xs font-bold ${isLoadingEvidenceDraftMetrics ? 'bg-gray-200 text-gray-500' : 'bg-slate-800 text-white hover:bg-slate-900'}`}
                                            >
                                                Reload
                                            </button>
                                        </div>
                                    </div>

                                    {evidenceDraftMetrics && (
                                        <div className="mt-4 space-y-4">
                                            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-[10px] font-bold text-gray-500 uppercase">Claim Coverage</div>
                                                    <div className="mt-1 text-lg font-bold text-slate-800">{evidenceDraftMetrics?.claimCoverage?.coveragePct ?? 0}%</div>
                                                    <div className="text-[11px] text-gray-500">{evidenceDraftMetrics?.claimCoverage?.supportedClaims ?? 0} sup / {evidenceDraftMetrics?.claimCoverage?.partiallySupportedClaims ?? 0} partial</div>
                                                </div>
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-[10px] font-bold text-gray-500 uppercase">Unsupported Critical</div>
                                                    <div className="mt-1 text-lg font-bold text-rose-700">{evidenceDraftMetrics?.claimCoverage?.unsupportedCriticalClaims ?? 0}</div>
                                                    <div className="text-[11px] text-gray-500">{evidenceDraftMetrics?.claimCoverage?.criticalClaims ?? 0} critical</div>
                                                </div>
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-[10px] font-bold text-gray-500 uppercase">Open Review Burden</div>
                                                    <div className="mt-1 text-lg font-bold text-amber-700">{evidenceDraftMetrics?.reviewerBurden?.openReviewQueueItems ?? 0}</div>
                                                    <div className="text-[11px] text-gray-500">Needs review: {evidenceDraftMetrics?.reviewerBurden?.needsReviewClaims ?? 0}</div>
                                                </div>
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-[10px] font-bold text-gray-500 uppercase">Avg Citation Stability</div>
                                                    <div className="mt-1 text-lg font-bold text-indigo-700">{evidenceDraftMetrics?.verifierQuality?.averageCitationStabilityScore ?? 'n/a'}</div>
                                                    <div className="text-[11px] text-gray-500">latest verifier/output</div>
                                                </div>
                                            </div>

                                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-xs font-bold text-gray-500 uppercase mb-2">Verifier Quality Signals</div>
                                                    <div className="grid grid-cols-2 gap-2 text-xs">
                                                        <div className="rounded border border-gray-100 bg-gray-50 p-2"><span className="font-bold">Contradictions:</span> {evidenceDraftMetrics?.verifierQuality?.contradictionCandidateCount ?? 0}</div>
                                                        <div className="rounded border border-gray-100 bg-gray-50 p-2"><span className="font-bold">Outdated links:</span> {evidenceDraftMetrics?.verifierQuality?.outdatedSourceLinkCount ?? 0}</div>
                                                        <div className="rounded border border-gray-100 bg-gray-50 p-2"><span className="font-bold">Stale citations:</span> {evidenceDraftMetrics?.verifierQuality?.staleCitationCount ?? 0}</div>
                                                        <div className="rounded border border-gray-100 bg-gray-50 p-2"><span className="font-bold">Suggestions:</span> {evidenceDraftMetrics?.verifierQuality?.evidenceSuggestionCount ?? 0}</div>
                                                    </div>
                                                </div>
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-xs font-bold text-gray-500 uppercase mb-2">Reviewer Burden SLA</div>
                                                    <div className="space-y-1 text-xs text-slate-700">
                                                        <div><span className="font-bold">Backlog age p50 (h):</span> {evidenceDraftMetrics?.reviewerBurden?.reviewQueueBacklogAgeHoursP50 ?? 'n/a'}</div>
                                                        <div><span className="font-bold">Backlog age p90 (h):</span> {evidenceDraftMetrics?.reviewerBurden?.reviewQueueBacklogAgeHoursP90 ?? 'n/a'}</div>
                                                        <div><span className="font-bold">Drafts in review:</span> {evidenceDraftMetrics?.reviewerBurden?.draftsInReview ?? 0}</div>
                                                    </div>
                                                </div>
                                            </div>

                                            <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-xs font-bold text-gray-500 uppercase mb-2">Unsupported Trend</div>
                                                    <div className="space-y-1 max-h-40 overflow-y-auto">
                                                        {(Array.isArray(evidenceDraftMetrics?.unsupportedTrend) ? evidenceDraftMetrics.unsupportedTrend.slice(-8) : []).map((row: any) => (
                                                            <div key={`ut-${row?.date}`} className="flex items-center justify-between rounded border border-gray-100 bg-gray-50 px-2 py-1 text-xs">
                                                                <span className="text-gray-600">{row?.date}</span>
                                                                <span className="font-semibold text-slate-800">u:{row?.unsupported ?? 0} • uc:{row?.unsupportedCritical ?? 0} • nr:{row?.needsReview ?? 0}</span>
                                                            </div>
                                                        ))}
                                                        {(!Array.isArray(evidenceDraftMetrics?.unsupportedTrend) || evidenceDraftMetrics.unsupportedTrend.length === 0) && (
                                                            <div className="text-xs text-gray-500">No trend data yet.</div>
                                                        )}
                                                    </div>
                                                </div>
                                                <div className="rounded-lg border border-white bg-white p-3">
                                                    <div className="text-xs font-bold text-gray-500 uppercase mb-2">Coverage Trend (Latest Verifier)</div>
                                                    <div className="space-y-1 max-h-40 overflow-y-auto">
                                                        {(Array.isArray(evidenceDraftMetrics?.verifierQuality?.claimCoverageTrend) ? evidenceDraftMetrics.verifierQuality.claimCoverageTrend.slice(-8) : []).map((row: any) => (
                                                            <div key={`ct-${row?.date}`} className="flex items-center justify-between rounded border border-gray-100 bg-gray-50 px-2 py-1 text-xs">
                                                                <span className="text-gray-600">{row?.date}</span>
                                                                <span className="font-semibold text-slate-800">{row?.coveragePct ?? 0}% • crit-unsup:{row?.unsupportedCritical ?? 0}</span>
                                                            </div>
                                                        ))}
                                                        {(!Array.isArray(evidenceDraftMetrics?.verifierQuality?.claimCoverageTrend) || evidenceDraftMetrics.verifierQuality.claimCoverageTrend.length === 0) && (
                                                            <div className="text-xs text-gray-500">No verifier trend data yet.</div>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>

                                            {lastBatchReverifyResult && (
                                                <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
                                                    <span className="font-bold">Last batch re-verify:</span> {lastBatchReverifyResult.processedCount ?? 0} processed, {lastBatchReverifyResult.failedCount ?? 0} failed.
                                                </div>
                                            )}
                                        </div>
                                    )}

                                    {isLoadingEvidenceDraftMetrics && !evidenceDraftMetrics && (
                                        <div className="mt-3 text-xs text-gray-500">Loading evidence drafting metrics...</div>
                                    )}
                                </div>

                                {evidenceDraftGraph && (
                                    <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
                                        <div className="lg:col-span-2 space-y-4">
                                            <div className="rounded-xl border border-gray-200 bg-white">
                                                <div className="px-4 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
                                                    <div className="text-xs font-bold text-gray-500 uppercase">Publish / Export Policy</div>
                                                    <button
                                                        onClick={handlePublishEvidenceDraft}
                                                        disabled={isPublishingEvidenceDraft}
                                                        className={`px-3 py-1.5 rounded-lg text-xs font-bold ${isPublishingEvidenceDraft ? 'bg-gray-200 text-gray-500' : 'bg-emerald-600 text-white hover:bg-emerald-700'}`}
                                                    >
                                                        {isPublishingEvidenceDraft ? 'Publishing...' : 'Publish Draft'}
                                                    </button>
                                                </div>
                                                <div className="p-4 grid grid-cols-1 md:grid-cols-3 gap-3">
                                                    <div>
                                                        <label className="text-xs font-bold text-gray-500 uppercase">Policy</label>
                                                        <select
                                                            value={publishPolicy}
                                                            onChange={(e) => setPublishPolicy(e.target.value as any)}
                                                            className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                                                        >
                                                            <option value="warn_only">warn_only</option>
                                                            <option value="block_on_unsupported_critical">block_on_unsupported_critical</option>
                                                            <option value="block_on_low_confidence">block_on_low_confidence</option>
                                                        </select>
                                                    </div>
                                                    <div>
                                                        <label className="text-xs font-bold text-gray-500 uppercase">Low Confidence Threshold</label>
                                                        <input
                                                            type="number"
                                                            min={0}
                                                            max={1}
                                                            step={0.01}
                                                            value={lowConfidenceThreshold}
                                                            onChange={(e) => setLowConfidenceThreshold(Number(e.target.value))}
                                                            className="mt-1 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                                                        />
                                                    </div>
                                                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-3 text-xs text-gray-600">
                                                        <div><span className="font-bold">Unsupported critical:</span> {evidenceDraftGraph.summary.unsupportedCriticalClaims}</div>
                                                        <div><span className="font-bold">Low-confidence review:</span> {(evidencePipelineMeta?.verificationSummary?.needsReviewCount ?? 0)}</div>
                                                        {lastPublishResult && (
                                                            <div className="mt-2 text-[11px] text-gray-500 line-clamp-4">
                                                                {JSON.stringify(lastPublishResult)}
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>

                                            <div className="rounded-xl border border-gray-200 bg-white">
                                                <div className="px-4 py-2 border-b border-gray-100 flex items-center justify-between">
                                                    <div className="text-xs font-bold text-gray-500 uppercase">Draft Output</div>
                                                    <div className="text-xs text-gray-500">
                                                        {evidenceDraftGraph.summary.claimCount} claims / {evidenceDraftGraph.summary.evidenceLinkCount} evidence links
                                                    </div>
                                                </div>
                                                <div className="p-4 text-sm leading-7 whitespace-pre-wrap text-slate-800">
                                                    {renderEvidenceDraftText(evidenceDraftGraph.output?.renderedText)}
                                                </div>
                                            </div>

                                            <div className="rounded-xl border border-gray-200 bg-white">
                                                <div className="px-4 py-2 border-b border-gray-100 text-xs font-bold text-gray-500 uppercase">Claims</div>
                                                <div className="max-h-[320px] overflow-y-auto p-2">
                                                    {evidenceDraftGraph.claims.map((cg, idx) => (
                                                        <button
                                                            key={cg.claim?.id || idx}
                                                            type="button"
                                                            onClick={() => setSelectedEvidenceClaimId(cg.claim?.id || null)}
                                                            className={`w-full text-left p-3 rounded-xl border mb-2 transition-colors ${selectedEvidenceClaimId === cg.claim?.id ? 'border-indigo-300 bg-indigo-50' : 'border-gray-200 bg-white hover:bg-gray-50'}`}
                                                        >
                                                            <div className="flex items-center justify-between gap-2">
                                                                <div className="text-xs font-bold text-indigo-700">CLM-{idx + 1}</div>
                                                                <span className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                                                                    cg.claim?.status === 'supported' ? 'bg-green-100 text-green-700' :
                                                                    cg.claim?.status === 'partially_supported' ? 'bg-amber-100 text-amber-700' :
                                                                    cg.claim?.status === 'unsupported' ? 'bg-rose-100 text-rose-700' :
                                                                    'bg-slate-100 text-slate-700'
                                                                }`}>
                                                                    {cg.claim?.status || 'needs_review'}
                                                                </span>
                                                            </div>
                                                            <div className="mt-1 text-sm text-slate-800 line-clamp-3">{cg.claim?.claimText}</div>
                                                            <div className="mt-2 text-[11px] text-gray-500">
                                                                {(cg.evidenceLinks?.length || 0)} evidence • {(cg.ruleCitations?.length || 0)} rules • conf {(Number(cg.claim?.confidence || 0)).toFixed(2)}
                                                            </div>
                                                        </button>
                                                    ))}
                                                </div>
                                            </div>

                                            <div className="rounded-xl border border-gray-200 bg-white">
                                                <div className="px-4 py-2 border-b border-gray-100 text-xs font-bold text-gray-500 uppercase">Reviewer Workflow</div>
                                                <div className="p-4 space-y-4">
                                                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                                        <div>
                                                            <div className="text-xs font-bold text-rose-700 uppercase mb-2">Unsupported Claims</div>
                                                            <div className="max-h-40 overflow-y-auto space-y-2">
                                                                {unsupportedClaims.map((cg: any, idx: number) => (
                                                                    <button
                                                                        key={`unsupported-${cg.claim?.id || idx}`}
                                                                        type="button"
                                                                        onClick={() => setSelectedEvidenceClaimId(cg.claim?.id || null)}
                                                                        className="w-full text-left rounded-lg border border-rose-200 bg-rose-50 p-2 text-xs hover:bg-rose-100"
                                                                    >
                                                                        <div className="font-bold text-rose-800">CLM-{(cg.claim?.orderIndex ?? idx) + 1}</div>
                                                                        <div className="text-rose-700 line-clamp-2">{cg.claim?.claimText}</div>
                                                                    </button>
                                                                ))}
                                                                {unsupportedClaims.length === 0 && <div className="text-sm text-gray-500">No unsupported claims.</div>}
                                                            </div>
                                                        </div>
                                                        <div>
                                                            <div className="text-xs font-bold text-amber-700 uppercase mb-2">Evidence Mismatch List</div>
                                                            <div className="max-h-40 overflow-y-auto space-y-2">
                                                                {[...evidenceMismatchClaims, ...advancedMismatchClaims.filter((r: any) => !(evidenceMismatchClaims as any[]).some((e: any) => e?.claimId === r?.claimId))].map((row: any, idx: number) => (
                                                                    <button
                                                                        key={`mismatch-${row.claimId || idx}`}
                                                                        type="button"
                                                                        onClick={() => setSelectedEvidenceClaimId(row.claimId || null)}
                                                                        className="w-full text-left rounded-lg border border-amber-200 bg-amber-50 p-2 text-xs hover:bg-amber-100"
                                                                    >
                                                                        <div className="font-bold text-amber-800">{row.claimId?.slice(0, 8)}...</div>
                                                                        <div className="text-amber-700">
                                                                            {(row.evidenceMismatches?.length || 0)} evidence • {(row.ruleMismatches?.length || 0)} rule • {(row.outdatedSourceMismatches?.length || 0)} outdated • {(row.crossCheckMismatches?.length || 0)} cross-check
                                                                        </div>
                                                                    </button>
                                                                ))}
                                                                {evidenceMismatchClaims.length === 0 && advancedMismatchClaims.length === 0 && <div className="text-sm text-gray-500">No verifier mismatches in latest run.</div>}
                                                            </div>
                                                        </div>
                                                    </div>

                                                    <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 space-y-3">
                                                        <div className="text-xs font-bold text-gray-700 uppercase">Selected Claim Review Action</div>
                                                        {!selectedEvidenceClaim ? (
                                                            <div className="text-sm text-gray-500">Select a claim to review.</div>
                                                        ) : (
                                                            <>
                                                                <div className="text-sm text-slate-800">{selectedEvidenceClaim.claim?.claimText}</div>
                                                                <textarea
                                                                    value={reviewerNotes}
                                                                    onChange={(e) => setReviewerNotes(e.target.value)}
                                                                    className="w-full min-h-[70px] rounded-lg border border-gray-300 px-3 py-2 text-sm"
                                                                    placeholder="Reviewer notes (required operationally for enterprise workflow)"
                                                                />
                                                                <input
                                                                    value={approverReason}
                                                                    onChange={(e) => setApproverReason(e.target.value)}
                                                                    className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
                                                                    placeholder="Approver reason / rationale (optional now, recommended)"
                                                                />
                                                                <textarea
                                                                    value={rewriteClaimText}
                                                                    onChange={(e) => setRewriteClaimText(e.target.value)}
                                                                    className="w-full min-h-[70px] rounded-lg border border-gray-300 px-3 py-2 text-sm"
                                                                    placeholder="Rewrite claim text (used only for Rewrite action)"
                                                                />
                                                                <div className="flex gap-2 flex-wrap">
                                                                    <button onClick={() => handleReviewClaim('approve')} disabled={isReviewingClaim} className={`px-3 py-1.5 rounded-lg text-xs font-bold ${isReviewingClaim ? 'bg-gray-200 text-gray-500' : 'bg-green-600 text-white hover:bg-green-700'}`}>Approve</button>
                                                                    <button onClick={() => handleReviewClaim('reject')} disabled={isReviewingClaim} className={`px-3 py-1.5 rounded-lg text-xs font-bold ${isReviewingClaim ? 'bg-gray-200 text-gray-500' : 'bg-rose-600 text-white hover:bg-rose-700'}`}>Reject</button>
                                                                    <button onClick={() => handleReviewClaim('rewrite')} disabled={isReviewingClaim} className={`px-3 py-1.5 rounded-lg text-xs font-bold ${isReviewingClaim ? 'bg-gray-200 text-gray-500' : 'bg-indigo-600 text-white hover:bg-indigo-700'}`}>Rewrite</button>
                                                                </div>
                                                            </>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>
                                        </div>

                                        <div className="space-y-4">
                                            <div className="rounded-xl border border-gray-200 bg-white">
                                                <div className="px-4 py-2 border-b border-gray-100 text-xs font-bold text-gray-500 uppercase">Evidence Drawer</div>
                                                <div className="p-4 space-y-4">
                                                    {!selectedEvidenceClaim ? (
                                                        <div className="text-sm text-gray-500">Select a claim marker or claim row to inspect evidence and rule citations.</div>
                                                    ) : (
                                                        <>
                                                            <div>
                                                                <div className="text-xs font-bold text-indigo-700 uppercase mb-1">
                                                                    {selectedEvidenceClaim.claim?.metadataJson ? 'Selected Claim' : 'Claim'}
                                                                </div>
                                                                <div className="text-sm text-slate-800">{selectedEvidenceClaim.claim?.claimText}</div>
                                                                <div className="mt-2 text-[11px] text-gray-500">{selectedEvidenceClaim.claim?.supportSummary}</div>
                                                                {typeof selectedClaimVerificationResult?.citationStabilityScore !== 'undefined' && selectedClaimVerificationResult?.citationStabilityScore !== null && (
                                                                    <div className="mt-2 inline-flex items-center rounded-full border border-indigo-200 bg-indigo-50 px-2 py-0.5 text-[10px] font-bold text-indigo-700">
                                                                        Citation stability: {selectedClaimVerificationResult.citationStabilityScore}
                                                                    </div>
                                                                )}
                                                            </div>

                                                            <div>
                                                                <div className="text-xs font-bold text-gray-500 uppercase mb-2">Evidence Links</div>
                                                                <div className="space-y-2">
                                                                    {(selectedEvidenceClaim.evidenceLinks || []).map((ev: any) => (
                                                                        <div key={ev.id} className="rounded-lg border border-gray-200 bg-gray-50 p-3">
                                                                            <div className="flex items-center justify-between gap-2">
                                                                                <div className="text-xs font-bold text-slate-700">{ev.paragraphId || 'Paragraph'} • {ev.supportStrength || 'unknown'}</div>
                                                                                <a
                                                                                    href={`#/documents?documentId=${encodeURIComponent(ev.documentId || '')}&paragraphId=${encodeURIComponent(ev.paragraphId || '')}`}
                                                                                    className="text-[10px] font-bold text-indigo-600 hover:underline"
                                                                                    title="Open source paragraph in Documents view"
                                                                                >
                                                                                    Open source paragraph
                                                                                </a>
                                                                            </div>
                                                                            <div className="mt-1 text-[11px] text-gray-500">{ev.documentId || 'doc'} {ev.documentVersionId ? `• v:${ev.documentVersionId.slice(0,8)}` : ''}</div>
                                                                            <div className="mt-2 text-xs text-slate-700 whitespace-pre-wrap">{ev.excerpt || 'No excerpt'}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(!selectedEvidenceClaim.evidenceLinks || selectedEvidenceClaim.evidenceLinks.length === 0) && (
                                                                        <div className="text-sm text-gray-500">No evidence links attached.</div>
                                                                    )}
                                                                </div>
                                                            </div>

                                                            <div>
                                                                <div className="text-xs font-bold text-gray-500 uppercase mb-2">Rule Citations</div>
                                                                <div className="space-y-2">
                                                                    {(selectedEvidenceClaim.ruleCitations || []).map((rc: any) => (
                                                                        <div key={rc.id} className="rounded-lg border border-indigo-100 bg-indigo-50 p-3">
                                                                            <div className="text-xs font-bold text-indigo-800">{rc.ruleCode || 'Rule Pack Reference'}</div>
                                                                            <div className="mt-1 text-[11px] text-indigo-700">{rc.citationText || rc.sourceCitation || 'No citation text'}</div>
                                                                            <div className="mt-1 text-[10px] text-indigo-500">
                                                                                Pack {rc.jurisdictionRulePackId ? rc.jurisdictionRulePackId.slice(0, 8) : 'n/a'} v{rc.rulePackVersion ?? 'n/a'}
                                                                            </div>
                                                                        </div>
                                                                    ))}
                                                                    {(!selectedEvidenceClaim.ruleCitations || selectedEvidenceClaim.ruleCitations.length === 0) && (
                                                                        <div className="text-sm text-gray-500">No rule citations attached.</div>
                                                                    )}
                                                                </div>
                                                            </div>

                                                            <div>
                                                                <div className="text-xs font-bold text-gray-500 uppercase mb-2">Verifier Mismatches</div>
                                                                <div className="space-y-2">
                                                                    {(selectedClaimVerificationResult?.evidenceMismatches || []).map((m: any, idx: number) => (
                                                                        <div key={`em-${idx}`} className="rounded-lg border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">
                                                                            <div className="font-bold">{m.type}</div>
                                                                            <div className="text-[11px] text-amber-700">{JSON.stringify(m)}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(selectedClaimVerificationResult?.ruleMismatches || []).map((m: any, idx: number) => (
                                                                        <div key={`rm-${idx}`} className="rounded-lg border border-orange-200 bg-orange-50 p-2 text-xs text-orange-800">
                                                                            <div className="font-bold">{m.type}</div>
                                                                            <div className="text-[11px] text-orange-700">{JSON.stringify(m)}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(selectedClaimVerificationResult?.outdatedSourceMismatches || []).map((m: any, idx: number) => (
                                                                        <div key={`om-${idx}`} className="rounded-lg border border-rose-200 bg-rose-50 p-2 text-xs text-rose-800">
                                                                            <div className="font-bold">{m.type}</div>
                                                                            <div className="text-[11px] text-rose-700">{JSON.stringify(m)}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(selectedClaimVerificationResult?.crossCheckMismatches || []).map((m: any, idx: number) => (
                                                                        <div key={`cm-${idx}`} className="rounded-lg border border-purple-200 bg-purple-50 p-2 text-xs text-purple-800">
                                                                            <div className="font-bold">{m.type}</div>
                                                                            <div className="text-[11px] text-purple-700">{JSON.stringify(m)}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(!(selectedClaimVerificationResult?.evidenceMismatches?.length) &&
                                                                        !(selectedClaimVerificationResult?.ruleMismatches?.length) &&
                                                                        !(selectedClaimVerificationResult?.outdatedSourceMismatches?.length) &&
                                                                        !(selectedClaimVerificationResult?.crossCheckMismatches?.length)) && (
                                                                        <div className="text-sm text-gray-500">No mismatch details for selected claim in latest verifier run.</div>
                                                                    )}
                                                                </div>
                                                            </div>

                                                            <div>
                                                                <div className="text-xs font-bold text-gray-500 uppercase mb-2">Suggested Better Evidence Links</div>
                                                                <div className="space-y-2">
                                                                    {(selectedClaimVerificationResult?.evidenceSuggestions || []).map((s: any, idx: number) => (
                                                                        <div key={`sug-${idx}`} className="rounded-lg border border-emerald-200 bg-emerald-50 p-3">
                                                                            <div className="flex items-center justify-between gap-2">
                                                                                <div className="text-xs font-bold text-emerald-800">{s.paragraphId || 'Paragraph'} • {s.supportStrength || 'suggested'}</div>
                                                                                <a
                                                                                    href={`#/documents?documentId=${encodeURIComponent(s.documentId || '')}&paragraphId=${encodeURIComponent(s.paragraphId || '')}`}
                                                                                    className="text-[10px] font-bold text-emerald-700 hover:underline"
                                                                                    title="Open suggested paragraph in Documents view"
                                                                                >
                                                                                    Open source paragraph
                                                                                </a>
                                                                            </div>
                                                                            <div className="mt-1 text-[11px] text-emerald-700">overlap {s.overlapTokenCount ?? 0} • ratio {s.overlapRatio ?? 0} • {s.reason || 'similarity'}</div>
                                                                            <div className="mt-2 text-xs text-emerald-900 whitespace-pre-wrap">{s.excerpt || 'No excerpt'}</div>
                                                                        </div>
                                                                    ))}
                                                                    {(!(selectedClaimVerificationResult?.evidenceSuggestions?.length)) && (
                                                                        <div className="text-sm text-gray-500">No evidence suggestions for this claim.</div>
                                                                    )}
                                                                </div>
                                                            </div>
                                                        </>
                                                    )}
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                )}
                            </div>
                        </div>
                    )}

                    {messages.map((msg) => (
                        <div key={msg.id} className={`flex gap-4 ${msg.role === 'user' ? 'flex-row-reverse' : 'flex-row'} max-w-4xl mx-auto`}>

                            {/* Avatar */}
                            <div className={`w-10 h-10 rounded-full flex items-center justify-center shrink-0 shadow-sm ${msg.role === 'user' ? 'bg-slate-200' : 'bg-gradient-to-br from-indigo-600 to-blue-600 text-white'}`}>
                                {msg.role === 'user' ? <span className="font-bold text-slate-600">Me</span> : <BrainCircuit className="w-6 h-6" />}
                            </div>

                            {/* Bubble */}
                            <div className={`flex flex-col max-w-[80%] ${msg.role === 'user' ? 'items-end' : 'items-start'}`}>
                                <div className={`px-6 py-4 rounded-2xl shadow-sm text-sm leading-relaxed whitespace-pre-wrap ${msg.role === 'user'
                                    ? 'bg-white text-slate-800 border border-gray-100 rounded-tr-none'
                                    : 'bg-white text-slate-800 border border-indigo-100 rounded-tl-none ring-1 ring-indigo-50'
                                    }`}>
                                    {msg.text}
                                </div>

                                {/* Sources / Grounding */}
                                {msg.sources && msg.sources.length > 0 && (
                                    <div className="mt-3 bg-white border border-gray-200 rounded-lg p-3 w-full">
                                        <p className="text-xs font-bold text-gray-500 uppercase mb-2 flex items-center gap-1">
                                            <Search className="w-3 h-3" /> Sources Found
                                        </p>
                                        <div className="space-y-1">
                                            {msg.sources.map((src, idx) => (
                                                <a key={idx} href={src.uri} target="_blank" rel="noopener noreferrer" className="block text-xs text-blue-600 hover:underline truncate">
                                                    {idx + 1}. {src.title}
                                                </a>
                                            ))}
                                        </div>
                                    </div>
                                )}
                                <span className="text-[10px] text-gray-400 mt-1 px-1">{msg.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                            </div>
                        </div>
                    ))}
                    {isTyping && (
                        <div className="flex gap-4 max-w-4xl mx-auto">
                            <div className="w-10 h-10 rounded-full bg-gradient-to-br from-indigo-600 to-blue-600 flex items-center justify-center shrink-0">
                                <BrainCircuit className="w-6 h-6 text-white" />
                            </div>
                            <div className="bg-white px-6 py-4 rounded-2xl rounded-tl-none border border-indigo-100 shadow-sm flex items-center gap-2">
                                <span className="w-2 h-2 bg-indigo-400 rounded-full animate-bounce"></span>
                                <span className="w-2 h-2 bg-indigo-400 rounded-full animate-bounce delay-75"></span>
                                <span className="w-2 h-2 bg-indigo-400 rounded-full animate-bounce delay-150"></span>
                            </div>
                        </div>
                    )}
                    <div ref={messagesEndRef} />
                </div>

                {/* Input Area */}
                <div className="bg-white border-t border-gray-200 p-4 pb-6">
                    <div className="max-w-4xl mx-auto relative">
                        <div className="absolute top-[-40px] left-0 flex gap-2">
                            {useSearch && (
                                <span className="bg-purple-100 text-purple-700 text-xs px-2 py-1 rounded-full font-bold flex items-center gap-1 border border-purple-200">
                                    <Search className="w-3 h-3" /> Web Search Active
                                    <button onClick={() => setUseSearch(false)}><X className="w-3 h-3 hover:text-purple-900" /></button>
                                </span>
                            )}
                            {selectedDocIds.length > 0 && (
                                <span className="bg-indigo-100 text-indigo-700 text-xs px-2 py-1 rounded-full font-bold flex items-center gap-1 border border-indigo-200">
                                    <File className="w-3 h-3" /> {selectedDocIds.length} Docs Attached
                                    <button onClick={() => setSelectedDocIds([])}><X className="w-3 h-3 hover:text-indigo-900" /></button>
                                </span>
                            )}
                        </div>

                        <div className="relative flex items-end gap-2 bg-gray-50 border border-gray-300 rounded-xl p-2 focus-within:ring-2 focus-within:ring-indigo-500 focus-within:bg-white focus-within:border-transparent transition-all">
                            <button
                                onClick={() => setUseSearch(!useSearch)}
                                className={`p-2 rounded-lg transition-colors ${useSearch ? 'bg-purple-100 text-purple-700' : 'text-gray-400 hover:bg-gray-200'}`}
                                title="Toggle Web Research"
                            >
                                <Search className="w-5 h-5" />
                            </button>
                            <textarea
                                className="flex-1 bg-transparent border-none focus:ring-0 resize-none max-h-32 min-h-[44px] py-2.5 text-sm text-slate-800 placeholder-gray-400"
                                placeholder="Ask Juris to draft, summarize, or research..."
                                value={inputValue}
                                onChange={(e) => setInputValue(e.target.value)}
                                onKeyDown={(e) => {
                                    if (e.key === 'Enter' && !e.shiftKey) {
                                        e.preventDefault();
                                        handleSendMessage();
                                    }
                                }}
                            />
                            <button
                                onClick={handleSendMessage}
                                disabled={!inputValue.trim() && !isTyping}
                                className={`p-2 rounded-lg mb-0.5 transition-all ${inputValue.trim() ? 'bg-indigo-600 text-white shadow-md hover:bg-indigo-700' : 'bg-gray-200 text-gray-400'}`}
                            >
                                <Send className="w-5 h-5" />
                            </button>
                        </div>
                        <p className="text-center text-[10px] text-gray-400 mt-2">AI can make mistakes. Please review generated legal documents.</p>
                    </div>
                </div>
            </div>

            {/* RIGHT: CONTEXT SIDEBAR */}
            <div className="w-72 bg-white border-l border-gray-200 flex flex-col shadow-xl z-20">
                <div className="p-4 border-b border-gray-100 bg-gray-50/50">
                    <h3 className="font-bold text-slate-800 text-sm uppercase tracking-wide">Context & Files</h3>
                </div>

                <div className="flex-1 overflow-y-auto p-2">
                    <div className="mb-4">
                        <p className="text-xs font-bold text-gray-400 px-2 mb-2 uppercase">Available Documents</p>
                        {documents.length === 0 && (
                            <div className="px-4 py-8 text-center text-gray-400 text-xs border-2 border-dashed border-gray-100 rounded-lg">
                                No documents found. Upload files in the 'Documents' tab to reference them here.
                            </div>
                        )}
                        {documents.map(doc => (
                            <div
                                key={doc.id}
                                onClick={() => toggleDocSelection(doc.id)}
                                className={`group flex items-center gap-3 p-2.5 rounded-lg mb-1 cursor-pointer transition-all border ${selectedDocIds.includes(doc.id)
                                    ? 'bg-indigo-50 border-indigo-200'
                                    : 'bg-white border-transparent hover:bg-gray-50 hover:border-gray-200'
                                    }`}
                            >
                                <div className={`w-4 h-4 rounded border flex items-center justify-center ${selectedDocIds.includes(doc.id) ? 'bg-indigo-600 border-indigo-600' : 'border-gray-300 bg-white'}`}>
                                    {selectedDocIds.includes(doc.id) && <svg className="w-3 h-3 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12" /></svg>}
                                </div>
                                <div className="flex-1 min-w-0">
                                    <p className={`text-sm truncate ${selectedDocIds.includes(doc.id) ? 'font-bold text-indigo-900' : 'text-slate-700'}`}>{doc.name}</p>
                                    <p className="text-[10px] text-gray-400">{doc.type.toUpperCase()} • {doc.size}</p>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

                {/* Quick Stats or Info */}
                <div className="p-4 bg-slate-50 border-t border-gray-200">
                    <div className="text-xs text-slate-500">
                        <p className="font-bold mb-1">Capabilities:</p>
                        <ul className="list-disc pl-4 space-y-1">
                            <li>Up to 1M tokens context</li>
                            <li>Deposition summarization</li>
                            <li>Case law search</li>
                        </ul>
                    </div>
                </div>
            </div>

        </div>
    );
};

export default AIDrafter;
