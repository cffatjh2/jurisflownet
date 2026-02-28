'use client';

import { useState, useEffect } from 'react';
import { X, FileText, Send, Clock, User, Calendar } from './Icons';
import { api } from '../services/api';

interface Document {
    id: string;
    name: string;
    fileName: string;
}

interface Client {
    id: string;
    name: string;
    email: string;
}

interface SignatureRequestModalProps {
    isOpen: boolean;
    onClose: () => void;
    matterId?: string;
    documentId?: string;
    onSuccess?: () => void;
}

export default function SignatureRequestModal({
    isOpen,
    onClose,
    matterId,
    documentId,
    onSuccess
}: SignatureRequestModalProps) {
    const [documents, setDocuments] = useState<Document[]>([]);
    const [clients, setClients] = useState<Client[]>([]);
    const [loading, setLoading] = useState(false);
    const [submitting, setSubmitting] = useState(false);

    const [formData, setFormData] = useState({
        documentId: documentId || '',
        signerEmail: '',
        signerName: '',
        clientId: '',
        expiresAt: '',
        verificationMethod: 'EmailLink'
    });
    const [disclosureProvided, setDisclosureProvided] = useState(false);

    useEffect(() => {
        if (isOpen) {
            loadData();
            setDisclosureProvided(false);
            if (documentId) {
                setFormData(prev => ({ ...prev, documentId }));
            }
        }
    }, [isOpen, documentId]);

    const loadData = async () => {
        setLoading(true);
        try {
            const [docsResponse, clientsResponse] = await Promise.all([
                matterId ? api.getDocuments({ matterId }) : Promise.resolve([]),
                api.getClients()
            ]);
            setDocuments(docsResponse);
            setClients(clientsResponse);
        } catch (error) {
            console.error('Failed to load data:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleClientSelect = (clientId: string) => {
        const client = clients.find(c => c.id === clientId);
        if (client) {
            setFormData(prev => ({
                ...prev,
                clientId,
                signerEmail: client.email,
                signerName: client.name
            }));
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!formData.documentId || !formData.signerEmail) return;

        setSubmitting(true);
        try {
            await api.signatures.request({
                documentId: formData.documentId,
                signerEmail: formData.signerEmail,
                signerName: formData.signerName || undefined,
                clientId: formData.clientId || undefined,
                expiresAt: formData.expiresAt || undefined,
                verificationMethod: formData.verificationMethod || undefined,
                requiresKba: formData.verificationMethod === 'Kba',
                disclosureProvided,
                disclosureVersion: 'v1'
            });
            onSuccess?.();
            onClose();
        } catch (error) {
            console.error('Failed to create signature request:', error);
        } finally {
            setSubmitting(false);
        }
    };

    // Default expiration: 30 days from now
    const getDefaultExpiration = () => {
        const date = new Date();
        date.setDate(date.getDate() + 30);
        return date.toISOString().split('T')[0];
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b bg-gradient-to-r from-blue-50 to-indigo-50">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-full bg-blue-100 flex items-center justify-center">
                            <FileText className="w-5 h-5 text-blue-600" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-slate-800">Request E-Signature</h2>
                            <p className="text-sm text-slate-500">Send document for digital signing</p>
                        </div>
                    </div>
                    <button onClick={onClose} className="p-2 hover:bg-white/50 rounded-full transition">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="p-6 space-y-4">
                    {/* Document Selection */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            <FileText className="w-4 h-4 inline mr-1" />
                            Document
                        </label>
                        {documentId ? (
                            <div className="px-4 py-2 bg-slate-100 rounded-lg text-slate-700">
                                {documents.find(d => d.id === documentId)?.name || 'Selected Document'}
                            </div>
                        ) : (
                            <select
                                value={formData.documentId}
                                onChange={(e) => setFormData(prev => ({ ...prev, documentId: e.target.value }))}
                                className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                                required
                            >
                                <option value="">Select a document...</option>
                                {documents.map(doc => (
                                    <option key={doc.id} value={doc.id}>{doc.name}</option>
                                ))}
                            </select>
                        )}
                    </div>

                    {/* Client Selection */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            <User className="w-4 h-4 inline mr-1" />
                            Select Client (optional)
                        </label>
                        <select
                            value={formData.clientId}
                            onChange={(e) => handleClientSelect(e.target.value)}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">Custom signer...</option>
                            {clients.map(client => (
                                <option key={client.id} value={client.id}>{client.name} ({client.email})</option>
                            ))}
                        </select>
                    </div>

                    {/* Signer Email */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            Signer Email *
                        </label>
                        <input
                            type="email"
                            value={formData.signerEmail}
                            onChange={(e) => setFormData(prev => ({ ...prev, signerEmail: e.target.value }))}
                            placeholder="signer@example.com"
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                            required
                        />
                    </div>

                    {/* Signer Name */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            Signer Name
                        </label>
                        <input
                            type="text"
                            value={formData.signerName}
                            onChange={(e) => setFormData(prev => ({ ...prev, signerName: e.target.value }))}
                            placeholder="John Doe"
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Expiration Date */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            <Calendar className="w-4 h-4 inline mr-1" />
                            Expires On
                        </label>
                        <input
                            type="date"
                            value={formData.expiresAt || getDefaultExpiration()}
                            onChange={(e) => setFormData(prev => ({ ...prev, expiresAt: e.target.value }))}
                            min={new Date().toISOString().split('T')[0]}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Verification Method */}
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            Verification Method
                        </label>
                        <select
                            value={formData.verificationMethod}
                            onChange={(e) => setFormData(prev => ({ ...prev, verificationMethod: e.target.value }))}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="EmailLink">Email Link (Default)</option>
                            <option value="SmsOtp">SMS One-Time Code</option>
                            <option value="Kba">Knowledge-Based (KBA)</option>
                            <option value="None">No Verification</option>
                        </select>
                        <p className="text-xs text-slate-500 mt-1">
                            Non-email methods require verification before signing is allowed.
                        </p>
                    </div>

                    {/* Disclosure Confirmation */}
                    <label className="flex items-start gap-2 text-sm text-slate-600">
                        <input
                            type="checkbox"
                            checked={disclosureProvided}
                            onChange={(e) => setDisclosureProvided(e.target.checked)}
                            className="mt-1"
                        />
                        <span>
                            I confirm the ESIGN/UETA disclosure has been provided to the signer.
                        </span>
                    </label>

                    {/* Info Box */}
                    <div className="p-3 bg-blue-50 rounded-lg text-sm text-blue-700">
                        <Clock className="w-4 h-4 inline mr-1" />
                        The signer will receive an email with a link to sign the document electronically.
                    </div>
                </form>

                {/* Footer */}
                <div className="flex items-center justify-end gap-3 p-4 border-t bg-slate-50">
                    <button
                        onClick={onClose}
                        className="px-4 py-2 text-slate-600 hover:bg-slate-100 rounded-lg transition"
                    >
                        Cancel
                    </button>
                    <button
                        onClick={handleSubmit}
                        disabled={submitting || !formData.documentId || !formData.signerEmail || !disclosureProvided}
                        className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 transition flex items-center gap-2"
                    >
                        {submitting ? (
                            <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                        ) : (
                            <Send className="w-4 h-4" />
                        )}
                        Send for Signature
                    </button>
                </div>
            </div>
        </div>
    );
}
