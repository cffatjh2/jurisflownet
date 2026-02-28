'use client';

import { useState, useEffect } from 'react';
import { FileText, Clock, CheckCircle, XCircle, AlertTriangle, Send, Eye, RefreshCw } from './Icons';
import { api } from '../services/api';

interface SignatureRequest {
    id: string;
    documentId: string;
    signerEmail: string;
    signerName?: string;
    status: string;
    sentAt?: string;
    viewedAt?: string;
    signedAt?: string;
    expiresAt?: string;
    declineReason?: string;
    createdAt: string;
    verificationMethod?: string;
    verificationStatus?: string;
    reminderCount?: number;
}

interface SignatureStatusProps {
    documentId?: string;
    matterId?: string;
    showActions?: boolean;
    compact?: boolean;
}

export default function SignatureStatus({ documentId, matterId, showActions = true, compact = false }: SignatureStatusProps) {
    const [requests, setRequests] = useState<SignatureRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState<string | null>(null);

    useEffect(() => {
        loadRequests();
    }, [documentId, matterId]);

    const loadRequests = async () => {
        setLoading(true);
        try {
            let data;
            if (documentId) {
                data = await api.signatures.getByDocument(documentId);
            } else if (matterId) {
                data = await api.signatures.getByMatter(matterId);
            } else {
                data = [];
            }
            setRequests(data);
        } catch (error) {
            console.error('Failed to load signature requests:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleRemind = async (id: string) => {
        setActionLoading(id);
        try {
            await api.signatures.remind(id);
            await loadRequests();
        } catch (error) {
            console.error('Failed to send reminder:', error);
        } finally {
            setActionLoading(null);
        }
    };

    const handleVoid = async (id: string) => {
        if (!confirm('Are you sure you want to void this signature request?')) return;

        setActionLoading(id);
        try {
            await api.signatures.void(id);
            await loadRequests();
        } catch (error) {
            console.error('Failed to void request:', error);
        } finally {
            setActionLoading(null);
        }
    };

    const handleVerify = async (request: SignatureRequest) => {
        const method = request.verificationMethod;
        if (!method) return;
        if (!confirm('Mark identity verification as completed for this signer?')) return;

        setActionLoading(request.id);
        try {
            await api.signatures.verify(request.id, { method, passed: true, notes: 'Verified by staff' });
            await loadRequests();
        } catch (error) {
            console.error('Failed to verify signer:', error);
        } finally {
            setActionLoading(null);
        }
    };

    const getVerificationLabel = (method?: string) => {
        if (!method) return 'Email Link';
        const normalized = method.toLowerCase();
        if (normalized === 'kba') return 'Knowledge-Based (KBA)';
        if (normalized === 'smsotp' || normalized === 'sms') return 'SMS One-Time Code';
        if (normalized === 'none') return 'None';
        return 'Email Link';
    };

    const isVerificationRequired = (method?: string) => {
        const normalized = (method || '').toLowerCase();
        return normalized !== '' && normalized !== 'none' && normalized !== 'emaillink' && normalized !== 'email';
    };

    const isVerificationPassed = (status?: string) => {
        const normalized = (status || '').toLowerCase();
        return normalized === 'passed' || normalized === 'notrequired';
    };

    const getStatusConfig = (status: string) => {
        switch (status) {
            case 'Signed':
                return {
                    icon: CheckCircle,
                    color: 'text-green-600',
                    bg: 'bg-green-100',
                    label: 'Signed'
                };
            case 'Declined':
                return {
                    icon: XCircle,
                    color: 'text-red-600',
                    bg: 'bg-red-100',
                    label: 'Declined'
                };
            case 'Viewed':
                return {
                    icon: Eye,
                    color: 'text-blue-600',
                    bg: 'bg-blue-100',
                    label: 'Viewed'
                };
            case 'Sent':
                return {
                    icon: Send,
                    color: 'text-amber-600',
                    bg: 'bg-amber-100',
                    label: 'Sent'
                };
            case 'Expired':
                return {
                    icon: AlertTriangle,
                    color: 'text-slate-500',
                    bg: 'bg-slate-100',
                    label: 'Expired'
                };
            case 'Voided':
                return {
                    icon: XCircle,
                    color: 'text-slate-500',
                    bg: 'bg-slate-100',
                    label: 'Voided'
                };
            default:
                return {
                    icon: Clock,
                    color: 'text-slate-500',
                    bg: 'bg-slate-100',
                    label: status
                };
        }
    };

    const formatDate = (dateString?: string) => {
        if (!dateString) return '-';
        return new Date(dateString).toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit'
        });
    };

    const isExpiringSoon = (expiresAt?: string) => {
        if (!expiresAt) return false;
        const daysUntil = Math.ceil((new Date(expiresAt).getTime() - Date.now()) / (1000 * 60 * 60 * 24));
        return daysUntil <= 3 && daysUntil > 0;
    };

    if (loading) {
        return (
            <div className="flex items-center justify-center py-8">
                <div className="w-6 h-6 border-2 border-blue-200 border-t-blue-600 rounded-full animate-spin" />
            </div>
        );
    }

    if (requests.length === 0) {
        return compact ? null : (
            <div className="text-center py-8 text-slate-500">
                <FileText className="w-10 h-10 mx-auto mb-2 opacity-30" />
                <p className="text-sm">No signature requests</p>
            </div>
        );
    }

    if (compact) {
        // Compact mode - just show badges
        return (
            <div className="flex flex-wrap gap-1">
                {requests.map(req => {
                    const config = getStatusConfig(req.status);
                    const Icon = config.icon;
                    return (
                        <span
                            key={req.id}
                            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs ${config.bg} ${config.color}`}
                            title={`${req.signerEmail}: ${config.label}`}
                        >
                            <Icon className="w-3 h-3" />
                            {config.label}
                        </span>
                    );
                })}
            </div>
        );
    }

    return (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
            {/* Header */}
            <div className="px-4 py-3 border-b bg-gradient-to-r from-indigo-50 to-purple-50 flex items-center justify-between">
                <div className="flex items-center gap-2">
                    <FileText className="w-5 h-5 text-indigo-600" />
                    <h3 className="font-medium text-slate-800">Signature Requests</h3>
                </div>
                <button
                    onClick={loadRequests}
                    className="p-1.5 hover:bg-white/50 rounded transition"
                >
                    <RefreshCw className="w-4 h-4" />
                </button>
            </div>

            {/* Requests List */}
            <div className="divide-y">
                {requests.map(req => {
                    const config = getStatusConfig(req.status);
                    const Icon = config.icon;
                    const canRemind = ['Sent', 'Viewed'].includes(req.status);
                    const canVoid = ['Pending', 'Sent', 'Viewed'].includes(req.status);
                    const verificationRequired = isVerificationRequired(req.verificationMethod);
                    const verificationPassed = isVerificationPassed(req.verificationStatus);

                    return (
                        <div key={req.id} className="p-4">
                            <div className="flex items-start justify-between">
                                <div className="flex items-start gap-3">
                                    <div className={`w-10 h-10 rounded-full ${config.bg} flex items-center justify-center flex-shrink-0`}>
                                        <Icon className={`w-5 h-5 ${config.color}`} />
                                    </div>
                                    <div>
                                        <p className="font-medium text-slate-800">
                                            {req.signerName || req.signerEmail}
                                        </p>
                                        <p className="text-sm text-slate-500">{req.signerEmail}</p>
                                        <div className="flex items-center gap-3 mt-1 text-xs text-slate-400">
                                            <span>Sent: {formatDate(req.sentAt)}</span>
                                            {req.viewedAt && <span>Viewed: {formatDate(req.viewedAt)}</span>}
                                            {req.signedAt && <span>Signed: {formatDate(req.signedAt)}</span>}
                                        </div>
                                        <div className="mt-1 text-xs text-slate-500">
                                            Verification: {getVerificationLabel(req.verificationMethod)} ({req.verificationStatus || 'Pending'})
                                        </div>
                                        {req.declineReason && (
                                            <p className="mt-2 text-sm text-red-600">
                                                Reason: {req.declineReason}
                                            </p>
                                        )}
                                        {isExpiringSoon(req.expiresAt) && (
                                            <p className="mt-1 text-xs text-amber-600 flex items-center gap-1">
                                                <AlertTriangle className="w-3 h-3" />
                                                Expires {formatDate(req.expiresAt)}
                                            </p>
                                        )}
                                    </div>
                                </div>

                                {showActions && (
                                    <div className="flex items-center gap-2">
                                        <span className={`px-2 py-1 rounded-full text-xs font-medium ${config.bg} ${config.color}`}>
                                            {config.label}
                                        </span>
                                        {canRemind && (
                                            <button
                                                onClick={() => handleRemind(req.id)}
                                                disabled={actionLoading === req.id}
                                                className="px-2 py-1 text-xs border border-slate-200 rounded hover:bg-slate-50 disabled:opacity-50"
                                            >
                                                {actionLoading === req.id ? '...' : 'Remind'}
                                            </button>
                                        )}
                                        {verificationRequired && !verificationPassed && (
                                            <button
                                                onClick={() => handleVerify(req)}
                                                disabled={actionLoading === req.id}
                                                className="px-2 py-1 text-xs border border-indigo-200 text-indigo-700 rounded hover:bg-indigo-50 disabled:opacity-50"
                                            >
                                                Verify
                                            </button>
                                        )}
                                        {canVoid && (
                                            <button
                                                onClick={() => handleVoid(req.id)}
                                                disabled={actionLoading === req.id}
                                                className="px-2 py-1 text-xs text-red-600 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50"
                                            >
                                                Void
                                            </button>
                                        )}
                                    </div>
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
