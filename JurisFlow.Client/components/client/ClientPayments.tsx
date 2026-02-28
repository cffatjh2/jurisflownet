import React, { useState, useEffect } from 'react';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import { Invoice, InvoiceStatus, PaymentPlan } from '../../types';
import PaymentCheckout from '../PaymentCheckout';
import PaymentHistory from '../PaymentHistory';
import { CreditCard, Check, AlertCircle, Clock, DollarSign, Wallet, Receipt, Calendar, RefreshCw, Play, Pause, Plus } from '../Icons';
import { clientApi } from '../../services/clientApi';

interface ClientPaymentsProps {
    clientId: string;
}

const ClientPayments: React.FC<ClientPaymentsProps> = ({ clientId }) => {
    const { client } = useClientAuth();
    const [invoices, setInvoices] = useState<Invoice[]>([]);
    const [paymentPlans, setPaymentPlans] = useState<PaymentPlan[]>([]);
    const [loading, setLoading] = useState(true);
    const [plansLoading, setPlansLoading] = useState(true);
    const [checkoutInvoice, setCheckoutInvoice] = useState<Invoice | null>(null);
    const [showCheckout, setShowCheckout] = useState(false);
    const [showPlanModal, setShowPlanModal] = useState(false);
    const [planError, setPlanError] = useState<string | null>(null);
    const [planSubmitting, setPlanSubmitting] = useState(false);
    const [planActionId, setPlanActionId] = useState<string | null>(null);

    const [planForm, setPlanForm] = useState({
        name: '',
        invoiceId: '',
        totalAmount: '',
        installmentAmount: '',
        frequency: 'Monthly',
        startDate: new Date().toISOString().split('T')[0],
        autoPayEnabled: false,
        autoPayMethod: 'Stripe',
        autoPayReference: ''
    });
    const clientToken = typeof window !== 'undefined' ? localStorage.getItem('client_token') : null;

    useEffect(() => {
        fetchData();
    }, []);

    const fetchData = async () => {
        setLoading(true);
        setPlansLoading(true);
        try {
            const [invoiceData, planData] = await Promise.all([
                clientApi.fetchJson('/invoices').catch(() => []),
                clientApi.fetchJson('/payment-plans').catch(() => [])
            ]);
            setInvoices(Array.isArray(invoiceData) ? invoiceData : []);
            setPaymentPlans(Array.isArray(planData) ? planData : []);
        } catch (error) {
            console.error('Error fetching payment data:', error);
        } finally {
            setLoading(false);
            setPlansLoading(false);
        }
    };

    const refreshPlans = async () => {
        setPlansLoading(true);
        try {
            const planData = await clientApi.fetchJson('/payment-plans');
            setPaymentPlans(Array.isArray(planData) ? planData : []);
        } catch (error) {
            console.error('Error refreshing payment plans:', error);
        } finally {
            setPlansLoading(false);
        }
    };

    const getBalance = (invoice: Invoice) => {
        if (typeof invoice.balance === 'number') return Math.max(0, invoice.balance);
        if (typeof invoice.amountPaid === 'number') {
            return Math.max(0, invoice.amount - invoice.amountPaid);
        }
        return invoice.amount;
    };

    useEffect(() => {
        if (!planForm.invoiceId) return;
        const invoice = invoices.find(inv => inv.id === planForm.invoiceId);
        if (!invoice) return;
        const balance = getBalance(invoice);
        setPlanForm(prev => ({
            ...prev,
            totalAmount: balance.toFixed(2)
        }));
    }, [planForm.invoiceId, invoices]);

    const handleCheckout = (invoice: Invoice) => {
        setCheckoutInvoice(invoice);
        setShowCheckout(true);
    };

    const handlePaymentSuccess = () => {
        if (!checkoutInvoice) return;
        setInvoices(prev => prev.map(inv => {
            if (inv.id !== checkoutInvoice.id) return inv;
            return {
                ...inv,
                status: InvoiceStatus.PAID,
                amountPaid: inv.amount,
                balance: 0
            };
        }));
    };

    const formatCurrency = (amount: number) => {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount);
    };

    const formatDate = (dateStr?: string) => {
        if (!dateStr) return 'N/A';
        return new Date(dateStr).toLocaleDateString('en-US');
    };

    const getStatusBadge = (status?: string) => {
        const normalized = status?.toLowerCase();
        switch (normalized) {
            case 'paid':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-green-100 text-green-800 flex items-center gap-1"><Check className="w-3 h-3" /> Paid</span>;
            case 'overdue':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-red-100 text-red-800 flex items-center gap-1"><AlertCircle className="w-3 h-3" /> Overdue</span>;
            case 'sent':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-800 flex items-center gap-1"><Clock className="w-3 h-3" /> Sent</span>;
            case 'draft':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-gray-100 text-gray-800">Draft</span>;
            default:
                return null;
        }
    };

    const getPlanStatusBadge = (status: string) => {
        switch (status) {
            case 'Active':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-emerald-100 text-emerald-700">Active</span>;
            case 'Paused':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-amber-100 text-amber-700">Paused</span>;
            case 'Completed':
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-slate-100 text-slate-600">Completed</span>;
            default:
                return <span className="px-2 py-1 text-xs font-medium rounded-full bg-gray-100 text-gray-600">{status}</span>;
        }
    };

    const resetPlanForm = () => {
        setPlanForm({
            name: '',
            invoiceId: '',
            totalAmount: '',
            installmentAmount: '',
            frequency: 'Monthly',
            startDate: new Date().toISOString().split('T')[0],
            autoPayEnabled: false,
            autoPayMethod: 'Stripe',
            autoPayReference: ''
        });
        setPlanError(null);
    };

    const handleCreatePlan = async () => {
        setPlanError(null);
        const installment = parseFloat(planForm.installmentAmount || '0');
        if (!Number.isFinite(installment) || installment <= 0) {
            setPlanError('Installment amount must be greater than 0.');
            return;
        }

        const invoice = planForm.invoiceId ? invoices.find(inv => inv.id === planForm.invoiceId) : null;
        const total = invoice ? getBalance(invoice) : parseFloat(planForm.totalAmount || '0');
        if (!Number.isFinite(total) || total <= 0) {
            setPlanError('Total amount must be greater than 0.');
            return;
        }
        if (installment > total + 0.01) {
            setPlanError('Installment amount cannot exceed the total balance.');
            return;
        }

        setPlanSubmitting(true);
        try {
            const payload = {
                name: planForm.name || undefined,
                invoiceId: planForm.invoiceId || undefined,
                totalAmount: total,
                installmentAmount: installment,
                frequency: planForm.frequency,
                startDate: planForm.startDate ? new Date(planForm.startDate).toISOString() : undefined,
                autoPayEnabled: planForm.autoPayEnabled,
                autoPayMethod: planForm.autoPayEnabled ? planForm.autoPayMethod : null,
                autoPayReference: planForm.autoPayEnabled ? planForm.autoPayReference || null : null
            };
            const created = await clientApi.fetchJson('/payment-plans', {
                method: 'POST',
                body: JSON.stringify(payload)
            });
            if (created) {
                setPaymentPlans(prev => [created, ...prev]);
                setShowPlanModal(false);
                resetPlanForm();
            }
        } catch (error: any) {
            setPlanError(error.message || 'Unable to create the payment plan.');
        } finally {
            setPlanSubmitting(false);
        }
    };

    const handleTogglePlanStatus = async (plan: PaymentPlan) => {
        const nextStatus = plan.status === 'Active' ? 'Paused' : 'Active';
        setPlanActionId(plan.id);
        try {
            const updated = await clientApi.fetchJson(`/payment-plans/${plan.id}`, {
                method: 'PUT',
                body: JSON.stringify({ status: nextStatus })
            });
            if (updated) {
                setPaymentPlans(prev => prev.map(p => (p.id === plan.id ? updated : p)));
            }
        } catch (error) {
            console.error('Failed to update payment plan status', error);
        } finally {
            setPlanActionId(null);
        }
    };

    const handleToggleAutoPay = async (plan: PaymentPlan) => {
        const nextEnabled = !plan.autoPayEnabled;
        const method = nextEnabled ? (plan.autoPayMethod || 'Stripe') : null;
        setPlanActionId(plan.id);
        try {
            const updated = await clientApi.fetchJson(`/payment-plans/${plan.id}`, {
                method: 'PUT',
                body: JSON.stringify({ autoPayEnabled: nextEnabled, autoPayMethod: method })
            });
            if (updated) {
                setPaymentPlans(prev => prev.map(p => (p.id === plan.id ? updated : p)));
            }
        } catch (error) {
            console.error('Failed to update AutoPay', error);
        } finally {
            setPlanActionId(null);
        }
    };

    const handleRunPlan = async (plan: PaymentPlan) => {
        setPlanActionId(plan.id);
        try {
            await clientApi.fetchJson(`/payment-plans/${plan.id}/run`, { method: 'POST' });
            await fetchData();
        } catch (error) {
            console.error('Failed to run payment plan', error);
        } finally {
            setPlanActionId(null);
        }
    };

    const unpaidInvoices = invoices.filter(inv => {
        const status = inv.status?.toLowerCase();
        if (status === 'draft') return false;
        return getBalance(inv) > 0;
    });

    const paidInvoices = invoices.filter(inv => {
        const status = inv.status?.toLowerCase();
        return status === 'paid' || getBalance(inv) === 0;
    });

    const activePlans = paymentPlans.filter(plan => plan.status === 'Active');
    const pausedPlans = paymentPlans.filter(plan => plan.status === 'Paused');
    const completedPlans = paymentPlans.filter(plan => plan.status === 'Completed');
    const selectedPlanInvoice = planForm.invoiceId ? invoices.find(inv => inv.id === planForm.invoiceId) : null;

    if (loading) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
            </div>
        );
    }

    return (
        <div className="p-6 h-full overflow-auto">
            <div className="max-w-5xl mx-auto">
                {/* Header */}
                <div className="mb-6">
                    <h2 className="text-2xl font-bold text-gray-900">Payments</h2>
                    <p className="text-gray-600 mt-1">Review invoices, choose a payment method, and complete your balance</p>
                </div>

                {invoices.length > 0 && unpaidInvoices.length === 0 && (
                    <div className="mb-6 bg-emerald-50 border border-emerald-100 text-emerald-700 px-4 py-3 rounded-lg text-sm">
                        All invoices are paid. Thank you for staying current with your balance.
                    </div>
                )}

                {/* Summary Cards */}
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-4">
                        <div className="flex items-center gap-3">
                            <div className="w-10 h-10 rounded-full bg-red-100 flex items-center justify-center">
                                <AlertCircle className="w-5 h-5 text-red-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">Outstanding</p>
                                <p className="text-xl font-bold text-gray-900">
                                    {formatCurrency(unpaidInvoices.reduce((sum, inv) => sum + getBalance(inv), 0))}
                                </p>
                            </div>
                        </div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-4">
                        <div className="flex items-center gap-3">
                            <div className="w-10 h-10 rounded-full bg-green-100 flex items-center justify-center">
                                <Check className="w-5 h-5 text-green-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">Paid</p>
                                <p className="text-xl font-bold text-gray-900">
                                    {formatCurrency(paidInvoices.reduce((sum, inv) => sum + (inv.amountPaid || inv.amount), 0))}
                                </p>
                            </div>
                        </div>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-4">
                        <div className="flex items-center gap-3">
                            <div className="w-10 h-10 rounded-full bg-blue-100 flex items-center justify-center">
                                <CreditCard className="w-5 h-5 text-blue-600" />
                            </div>
                            <div>
                                <p className="text-sm text-gray-500">Total Invoices</p>
                                <p className="text-xl font-bold text-gray-900">{invoices.length}</p>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Payment Plans */}
                <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-5 mb-8">
                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                        <div>
                            <h3 className="text-lg font-semibold text-gray-900">Payment Plans</h3>
                            <p className="text-sm text-gray-500 mt-1">Split balances into scheduled installments. AutoPay charges your saved billing method when a run is processed.</p>
                        </div>
                        <div className="flex items-center gap-2">
                            <button
                                onClick={refreshPlans}
                                className="flex items-center gap-2 px-3 py-2 border border-gray-200 text-sm rounded-lg hover:bg-gray-50"
                            >
                                <RefreshCw className="w-4 h-4" />
                                Refresh
                            </button>
                            <button
                                onClick={() => {
                                    resetPlanForm();
                                    setShowPlanModal(true);
                                }}
                                className="flex items-center gap-2 px-3 py-2 bg-slate-900 text-white text-sm rounded-lg hover:bg-slate-800"
                            >
                                <Plus className="w-4 h-4" />
                                Create Plan
                            </button>
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3 mt-4">
                        <div className="border border-gray-100 rounded-lg p-3">
                            <p className="text-xs text-gray-500">Active Plans</p>
                            <p className="text-lg font-bold text-gray-900">{activePlans.length}</p>
                        </div>
                        <div className="border border-gray-100 rounded-lg p-3">
                            <p className="text-xs text-gray-500">Paused Plans</p>
                            <p className="text-lg font-bold text-gray-900">{pausedPlans.length}</p>
                        </div>
                        <div className="border border-gray-100 rounded-lg p-3">
                            <p className="text-xs text-gray-500">Completed</p>
                            <p className="text-lg font-bold text-gray-900">{completedPlans.length}</p>
                        </div>
                    </div>

                    {plansLoading ? (
                        <div className="flex items-center justify-center py-10">
                            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
                        </div>
                    ) : paymentPlans.length === 0 ? (
                        <div className="mt-6 border border-dashed border-gray-200 rounded-lg p-6 text-center text-sm text-gray-500">
                            No payment plans yet. Create a plan to schedule installments for an invoice.
                        </div>
                    ) : (
                        <div className="mt-6 space-y-3">
                            {paymentPlans.map(plan => {
                                const planInvoice = plan.invoiceId ? invoices.find(inv => inv.id === plan.invoiceId) : null;
                                const isActionable = plan.status === 'Active' && plan.remainingAmount > 0;
                                return (
                                    <div key={plan.id} className="border border-gray-200 rounded-lg p-4">
                                        <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                                            <div className="space-y-2">
                                                <div className="flex items-center gap-2 flex-wrap">
                                                    <span className="font-semibold text-gray-900">{plan.name}</span>
                                                    {getPlanStatusBadge(plan.status)}
                                                    {plan.autoPayEnabled && (
                                                        <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-700">AutoPay</span>
                                                    )}
                                                </div>
                                                <div className="text-sm text-gray-500">
                                                    {planInvoice ? `Invoice #${planInvoice.number}` : 'General balance plan'}
                                                </div>
                                                <div className="flex flex-wrap gap-4 text-xs text-gray-500">
                                                    <div className="flex items-center gap-1">
                                                        <Calendar className="w-3.5 h-3.5" />
                                                        Next run: {formatDate(plan.nextRunDate)}
                                                    </div>
                                                    <div>Installment: {formatCurrency(plan.installmentAmount)}</div>
                                                    <div>Remaining: {formatCurrency(plan.remainingAmount)}</div>
                                                    <div>Frequency: {plan.frequency}</div>
                                                </div>
                                                <div className="flex items-center gap-3 text-xs text-gray-500">
                                                    <label className="inline-flex items-center gap-2">
                                                        <input
                                                            type="checkbox"
                                                            className="rounded border-gray-300"
                                                            checked={plan.autoPayEnabled}
                                                            onChange={() => handleToggleAutoPay(plan)}
                                                            disabled={plan.status === 'Completed' || planActionId === plan.id}
                                                        />
                                                        AutoPay
                                                    </label>
                                                    {plan.autoPayEnabled && plan.autoPayMethod && (
                                                        <span>Method: {plan.autoPayMethod}</span>
                                                    )}
                                                </div>
                                            </div>
                                            <div className="flex flex-wrap items-center gap-2">
                                                <button
                                                    onClick={() => handleRunPlan(plan)}
                                                    disabled={!isActionable || planActionId === plan.id}
                                                    className="flex items-center gap-2 px-3 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
                                                >
                                                    <Play className="w-4 h-4" />
                                                    Run Next Payment
                                                </button>
                                                {plan.status !== 'Completed' && (
                                                    <button
                                                        onClick={() => handleTogglePlanStatus(plan)}
                                                        disabled={planActionId === plan.id}
                                                        className="flex items-center gap-2 px-3 py-2 border border-gray-200 text-sm rounded-lg hover:bg-gray-50 disabled:opacity-50"
                                                    >
                                                        <Pause className="w-4 h-4" />
                                                        {plan.status === 'Active' ? 'Pause' : 'Resume'}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </div>

                {/* Payment Options */}
                <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-5 mb-8">
                    <div className="flex items-center justify-between">
                        <div>
                            <h3 className="text-lg font-semibold text-gray-900">Payment Options</h3>
                            <p className="text-sm text-gray-500 mt-1">Choose the method that works best for you. Online payments complete instantly.</p>
                        </div>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mt-4">
                        <div className="border border-gray-200 rounded-lg p-4">
                            <div className="flex items-center gap-2 text-sm font-semibold text-gray-900">
                                <CreditCard className="w-4 h-4 text-blue-600" />
                                Card Payment
                            </div>
                            <p className="text-sm text-gray-500 mt-2">Pay with credit or debit card for immediate confirmation.</p>
                        </div>
                        <div className="border border-gray-200 rounded-lg p-4">
                            <div className="flex items-center gap-2 text-sm font-semibold text-gray-900">
                                <Wallet className="w-4 h-4 text-emerald-600" />
                                Bank Transfer
                            </div>
                            <p className="text-sm text-gray-500 mt-2">Wire or ACH details provided by your firm.</p>
                        </div>
                        <div className="border border-gray-200 rounded-lg p-4">
                            <div className="flex items-center gap-2 text-sm font-semibold text-gray-900">
                                <Receipt className="w-4 h-4 text-amber-600" />
                                Check by Mail
                            </div>
                            <p className="text-sm text-gray-500 mt-2">Mailing instructions are available from your firm.</p>
                        </div>
                    </div>
                </div>

                {/* Unpaid Invoices */}
                {unpaidInvoices.length > 0 && (
                    <div className="mb-8">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4">Outstanding Invoices</h3>
                        <div className="space-y-3">
                            {unpaidInvoices.map((invoice) => (
                                <div key={invoice.id} className="bg-white rounded-xl shadow-sm border border-gray-200 p-5">
                                    <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                                        <div className="flex items-center gap-4">
                                            <div className="w-12 h-12 rounded-full bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white font-bold">
                                                <DollarSign className="w-6 h-6" />
                                            </div>
                                            <div>
                                                <div className="flex items-center gap-3">
                                                    <span className="font-semibold text-gray-900">Invoice #{invoice.number}</span>
                                                    {getStatusBadge(invoice.status)}
                                                </div>
                                                <p className="text-sm text-gray-500">Due date: {formatDate(invoice.dueDate)}</p>
                                                <p className="text-xs text-gray-400 mt-1">Balance due: {formatCurrency(getBalance(invoice))}</p>
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-4">
                                            <span className="text-xl font-bold text-gray-900">{formatCurrency(getBalance(invoice))}</span>
                                            <button
                                                onClick={() => handleCheckout(invoice)}
                                                disabled={getBalance(invoice) <= 0}
                                                className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50"
                                            >
                                                <CreditCard className="w-4 h-4" />
                                                Pay Now
                                            </button>
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Paid Invoices */}
                {paidInvoices.length > 0 && (
                    <div className="mb-8">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4">Paid Invoices</h3>
                        <div className="space-y-3">
                            {paidInvoices.map((invoice) => (
                                <div key={invoice.id} className="bg-white rounded-xl shadow-sm border border-gray-200 p-5 opacity-80">
                                    <div className="flex items-center justify-between">
                                        <div className="flex items-center gap-4">
                                            <div className="w-12 h-12 rounded-full bg-green-100 flex items-center justify-center">
                                                <Check className="w-6 h-6 text-green-600" />
                                            </div>
                                            <div>
                                                <div className="flex items-center gap-3">
                                                    <span className="font-semibold text-gray-900">Invoice #{invoice.number}</span>
                                                    {getStatusBadge(invoice.status)}
                                                </div>
                                                <p className="text-sm text-gray-500">Paid on: {formatDate(invoice.paidDate || invoice.updatedAt || invoice.dueDate)}</p>
                                            </div>
                                        </div>
                                        <span className="text-xl font-bold text-gray-900">{formatCurrency(invoice.amount)}</span>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Payment History */}
                {clientId && (
                    <div className="mb-8">
                        <PaymentHistory clientId={clientId} authToken={clientToken || undefined} />
                    </div>
                )}

                {/* Empty State */}
                {invoices.length === 0 && (
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
                        <CreditCard className="w-16 h-16 text-gray-300 mx-auto mb-4" />
                        <h3 className="text-lg font-medium text-gray-900 mb-2">No invoices yet</h3>
                        <p className="text-gray-500">Invoices from your attorney will appear here.</p>
                    </div>
                )}
            </div>

            <PaymentCheckout
                isOpen={showCheckout}
                onClose={() => setShowCheckout(false)}
                invoiceId={checkoutInvoice?.id}
                invoiceNumber={checkoutInvoice?.number}
                clientId={client?.id}
                amount={checkoutInvoice ? getBalance(checkoutInvoice) : 0}
                clientName={client?.name}
                clientEmail={client?.email}
                mode="api"
                authToken={clientToken || undefined}
                onSuccess={handlePaymentSuccess}
            />

            {showPlanModal && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden">
                        <div className="px-6 py-4 border-b border-gray-200">
                            <h3 className="text-lg font-semibold text-gray-900">Create Payment Plan</h3>
                            <p className="text-sm text-gray-500 mt-1">Set up installments for an invoice or your overall balance.</p>
                        </div>
                        <div className="p-6 space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-1">Plan Name</label>
                                <input
                                    type="text"
                                    value={planForm.name}
                                    onChange={e => setPlanForm(prev => ({ ...prev, name: e.target.value }))}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                    placeholder="e.g., Retainer Installments"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-1">Invoice (Optional)</label>
                                <select
                                    value={planForm.invoiceId}
                                    onChange={e => setPlanForm(prev => ({ ...prev, invoiceId: e.target.value }))}
                                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                >
                                    <option value="">Select an invoice</option>
                                    {unpaidInvoices.map(inv => (
                                        <option key={inv.id} value={inv.id}>
                                            #{inv.number} · {formatCurrency(getBalance(inv))}
                                        </option>
                                    ))}
                                </select>
                            </div>
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">Total Amount</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={selectedPlanInvoice ? getBalance(selectedPlanInvoice).toFixed(2) : planForm.totalAmount}
                                        onChange={e => setPlanForm(prev => ({ ...prev, totalAmount: e.target.value }))}
                                        className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                        placeholder="0.00"
                                        disabled={!!selectedPlanInvoice}
                                    />
                                    {selectedPlanInvoice && (
                                        <p className="text-xs text-gray-500 mt-1">Auto-filled from invoice balance.</p>
                                    )}
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">Installment Amount *</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={planForm.installmentAmount}
                                        onChange={e => setPlanForm(prev => ({ ...prev, installmentAmount: e.target.value }))}
                                        className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                        placeholder="0.00"
                                        required
                                    />
                                </div>
                            </div>
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">Frequency</label>
                                    <select
                                        value={planForm.frequency}
                                        onChange={e => setPlanForm(prev => ({ ...prev, frequency: e.target.value }))}
                                        className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                    >
                                        <option value="Weekly">Weekly</option>
                                        <option value="Biweekly">Biweekly</option>
                                        <option value="Monthly">Monthly</option>
                                        <option value="Quarterly">Quarterly</option>
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">Start Date</label>
                                    <input
                                        type="date"
                                        value={planForm.startDate}
                                        onChange={e => setPlanForm(prev => ({ ...prev, startDate: e.target.value }))}
                                        className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                    />
                                </div>
                            </div>
                            <div className="border border-gray-100 rounded-lg p-3">
                                <label className="flex items-center gap-2 text-sm text-gray-700">
                                    <input
                                        type="checkbox"
                                        className="rounded border-gray-300"
                                        checked={planForm.autoPayEnabled}
                                        onChange={e => setPlanForm(prev => ({
                                            ...prev,
                                            autoPayEnabled: e.target.checked,
                                            autoPayMethod: e.target.checked ? prev.autoPayMethod || 'Stripe' : prev.autoPayMethod
                                        }))}
                                    />
                                    Enable AutoPay
                                </label>
                                {planForm.autoPayEnabled && (
                                    <div className="mt-3 grid grid-cols-1 md:grid-cols-2 gap-3">
                                        <div>
                                            <label className="block text-xs font-medium text-gray-600 mb-1">Method</label>
                                            <select
                                                value={planForm.autoPayMethod}
                                                onChange={e => setPlanForm(prev => ({ ...prev, autoPayMethod: e.target.value }))}
                                                className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                            >
                                                <option value="Stripe">Stripe (Card / ACH on file)</option>
                                            </select>
                                        </div>
                                        <div>
                                            <label className="block text-xs font-medium text-gray-600 mb-1">Reference (Optional)</label>
                                            <input
                                                type="text"
                                                value={planForm.autoPayReference}
                                                onChange={e => setPlanForm(prev => ({ ...prev, autoPayReference: e.target.value }))}
                                                className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm"
                                                placeholder="e.g., Card ending 4242"
                                            />
                                        </div>
                                    </div>
                                )}
                            </div>
                            {planError && (
                                <div className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">
                                    {planError}
                                </div>
                            )}
                        </div>
                        <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
                            <button
                                onClick={() => {
                                    setShowPlanModal(false);
                                    resetPlanForm();
                                }}
                                className="px-4 py-2 text-sm text-gray-600 hover:text-gray-800"
                            >
                                Cancel
                            </button>
                            <button
                                onClick={handleCreatePlan}
                                disabled={planSubmitting}
                                className="px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
                            >
                                {planSubmitting ? 'Saving...' : 'Create Plan'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default ClientPayments;
