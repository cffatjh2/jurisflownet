'use client';

import { useState } from 'react';
import { CreditCard, DollarSign, X, CheckCircle, AlertCircle, Lock } from './Icons';

type PaymentMode = 'demo' | 'api';

interface PaymentCheckoutProps {
    isOpen: boolean;
    onClose: () => void;
    invoiceId?: string;
    invoiceNumber?: string;
    matterId?: string;
    clientId?: string;
    amount: number;
    currency?: string;
    clientName?: string;
    clientEmail?: string;
    matterName?: string;
    mode?: PaymentMode;
    authToken?: string;
    onSuccess?: (transactionId: string) => void;
}

export default function PaymentCheckout({
    isOpen,
    onClose,
    invoiceId,
    invoiceNumber,
    matterId,
    clientId,
    amount,
    currency = 'USD',
    clientName,
    clientEmail,
    matterName,
    mode = 'api',
    authToken,
    onSuccess
}: PaymentCheckoutProps) {
    const [step, setStep] = useState<'review' | 'processing' | 'success' | 'error'>('review');
    const [transactionId, setTransactionId] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const getAuthToken = () => {
        if (authToken) return authToken;
        if (typeof window === 'undefined') return null;
        return localStorage.getItem('auth_token');
    };

    const requestJson = async (endpoint: string, options: RequestInit = {}) => {
        const token = getAuthToken();
        const res = await fetch(`/api${endpoint}`, {
            headers: {
                'Content-Type': 'application/json',
                ...(token ? { Authorization: `Bearer ${token}` } : {})
            },
            ...options
        });
        if (!res.ok) {
            throw new Error(`API Error: ${res.statusText}`);
        }
        return res.json();
    };

    const formatCurrency = (value: number) => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: currency
        }).format(value);
    };

    const handlePayment = async () => {
        setStep('processing');
        setError(null);

        try {
            if (mode === 'demo') {
                await new Promise(resolve => setTimeout(resolve, 1600));
                const demoId = `demo_${Date.now()}`;
                setTransactionId(demoId);
                setStep('success');
                onSuccess?.(demoId);
                return;
            }

            const response = await requestJson('/payments/create-checkout', {
                method: 'POST',
                body: JSON.stringify({
                    invoiceId,
                    matterId,
                    clientId,
                    amount,
                    currency,
                    payerEmail: clientEmail,
                    payerName: clientName
                })
            });

            if (response?.checkoutUrl) {
                window.location.href = response.checkoutUrl;
                return;
            }

            throw new Error('Checkout URL is missing.');
        } catch (err) {
            console.error('Payment failed:', err);
            setError('Payment processing failed. Please try again.');
            setStep('error');
        }
    };

    const resetAndClose = () => {
        setStep('review');
        setTransactionId(null);
        setError(null);
        onClose();
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-xl shadow-2xl w-full max-w-md overflow-hidden">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b bg-gradient-to-r from-green-50 to-emerald-50">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-full bg-green-100 flex items-center justify-center">
                            <CreditCard className="w-5 h-5 text-green-600" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-slate-800">Payment Checkout</h2>
                            <p className="text-sm text-slate-500">Secure payment processing</p>
                        </div>
                    </div>
                    <button onClick={resetAndClose} className="p-2 hover:bg-white/50 rounded-full transition">
                        <X className="w-5 h-5" />
                    </button>
                </div>

                {/* Content */}
                <div className="p-6">
                    {step === 'review' && (
                        <div className="space-y-4">
                            {/* Amount Display */}
                            <div className="text-center py-6 bg-gradient-to-br from-slate-50 to-slate-100 rounded-xl">
                                <p className="text-sm text-slate-500 mb-1">Amount Due</p>
                                <p className="text-4xl font-bold text-slate-800">{formatCurrency(amount)}</p>
                            </div>

                            {/* Details */}
                            <div className="space-y-3 text-sm">
                                {matterName && (
                                    <div className="flex justify-between">
                                        <span className="text-slate-500">Matter</span>
                                        <span className="text-slate-700 font-medium">{matterName}</span>
                                    </div>
                                )}
                                {clientName && (
                                    <div className="flex justify-between">
                                        <span className="text-slate-500">Client</span>
                                        <span className="text-slate-700 font-medium">{clientName}</span>
                                    </div>
                                )}
                                {invoiceId && (
                                    <div className="flex justify-between">
                                        <span className="text-slate-500">Invoice</span>
                                        <span className="text-slate-700 font-medium">
                                            #{invoiceNumber || invoiceId.slice(0, 8)}
                                        </span>
                                    </div>
                                )}
                            </div>

                            {/* Security Badge */}
                            <div className="flex items-center justify-center gap-2 text-sm text-slate-500 py-3">
                                <Lock className="w-4 h-4" />
                                <span>{mode === 'api' ? 'Secured by Stripe' : 'Secure payment flow'}</span>
                            </div>

                            {/* Pay Button */}
                            <button
                                onClick={handlePayment}
                                className="w-full py-3 bg-gradient-to-r from-green-600 to-emerald-600 text-white rounded-lg font-medium hover:from-green-700 hover:to-emerald-700 transition flex items-center justify-center gap-2"
                            >
                                <DollarSign className="w-5 h-5" />
                                Pay {formatCurrency(amount)}
                            </button>

                            {/* Card Logos */}
                            <div className="flex items-center justify-center gap-3 pt-2">
                                <div className="px-3 py-1 bg-slate-100 rounded text-xs font-medium text-slate-600">VISA</div>
                                <div className="px-3 py-1 bg-slate-100 rounded text-xs font-medium text-slate-600">Mastercard</div>
                                <div className="px-3 py-1 bg-slate-100 rounded text-xs font-medium text-slate-600">AMEX</div>
                            </div>
                        </div>
                    )}

                    {step === 'processing' && (
                        <div className="text-center py-12">
                            <div className="w-16 h-16 border-4 border-green-200 border-t-green-600 rounded-full animate-spin mx-auto mb-4" />
                            <p className="text-lg font-medium text-slate-700">Processing Payment...</p>
                            <p className="text-sm text-slate-500 mt-1">Please wait while we process your payment</p>
                        </div>
                    )}

                    {step === 'success' && (
                        <div className="text-center py-8">
                            <div className="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-4">
                                <CheckCircle className="w-8 h-8 text-green-600" />
                            </div>
                            <p className="text-lg font-medium text-slate-800">Payment Successful!</p>
                            <p className="text-sm text-slate-500 mt-1">{formatCurrency(amount)} has been processed</p>
                            {transactionId && (
                                <p className="text-xs text-slate-400 mt-3">
                                    Transaction ID: {transactionId.slice(0, 8)}...
                                </p>
                            )}
                            <button
                                onClick={resetAndClose}
                                className="mt-6 px-6 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition"
                            >
                                Done
                            </button>
                        </div>
                    )}

                    {step === 'error' && (
                        <div className="text-center py-8">
                            <div className="w-16 h-16 bg-red-100 rounded-full flex items-center justify-center mx-auto mb-4">
                                <AlertCircle className="w-8 h-8 text-red-600" />
                            </div>
                            <p className="text-lg font-medium text-slate-800">Payment Failed</p>
                            <p className="text-sm text-red-600 mt-1">{error}</p>
                            <div className="flex gap-3 justify-center mt-6">
                                <button
                                    onClick={resetAndClose}
                                    className="px-4 py-2 border border-slate-200 text-slate-600 rounded-lg hover:bg-slate-50 transition"
                                >
                                    Cancel
                                </button>
                                <button
                                    onClick={() => setStep('review')}
                                    className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
                                >
                                    Try Again
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
