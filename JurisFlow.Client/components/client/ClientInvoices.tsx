import React, { useState, useEffect } from 'react';
import { Invoice, InvoiceStatus } from '../../types';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import PaymentCheckout from '../PaymentCheckout';
import { CreditCard, Download, CheckCircle } from '../Icons';
import { clientApi } from '../../services/clientApi';

const ClientInvoices: React.FC = () => {
  const { client } = useClientAuth();
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<'all' | 'paid' | 'unpaid' | 'overdue'>('all');
  const [checkoutInvoice, setCheckoutInvoice] = useState<Invoice | null>(null);
  const [showCheckout, setShowCheckout] = useState(false);

  const clientToken = typeof window !== 'undefined' ? localStorage.getItem('client_token') : null;

  const normalizeStatus = (status?: InvoiceStatus) => (status ? status.toLowerCase() : '');
  const formatStatusLabel = (status?: InvoiceStatus) => {
    const normalized = normalizeStatus(status);
    if (!normalized) return 'Unknown';
    return normalized
      .split(/[_\s]+/)
      .map(part => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  };
  const getBalance = (invoice: Invoice) => {
    if (typeof invoice.balance === 'number') return Math.max(0, invoice.balance);
    if (typeof invoice.amountPaid === 'number') {
      return Math.max(0, invoice.amount - invoice.amountPaid);
    }
    return invoice.amount;
  };

  useEffect(() => {
    const loadInvoices = async () => {
      try {
        const data = await clientApi.fetchJson('/invoices');
        setInvoices(Array.isArray(data) ? data : []);
      } catch (error) {
        console.error('Error loading invoices:', error);
      } finally {
        setLoading(false);
      }
    };
    
    loadInvoices();
  }, []);

  const filteredInvoices = invoices.filter(inv => {
    const status = normalizeStatus(inv.status);
    if (filter === 'paid') return status === 'paid';
    if (filter === 'unpaid') return status === 'sent';
    if (filter === 'overdue') return status === 'overdue';
    return true;
  });

  const totalAmount = filteredInvoices.reduce((sum, inv) => sum + inv.amount, 0);
  const unpaidAmount = invoices
    .filter(i => {
      const status = normalizeStatus(i.status);
      return status === 'overdue' || status === 'sent';
    })
    .reduce((sum, inv) => sum + inv.amount, 0);

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

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-slate-900">Invoices</h2>
        <p className="text-gray-600 mt-1">View and manage your invoices</p>
      </div>

      {/* Summary Cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Total Invoices</div>
          <div className="text-2xl font-bold text-slate-900">{invoices.length}</div>
        </div>
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Unpaid</div>
          <div className="text-2xl font-bold text-yellow-600">
            {invoices.filter(i => {
              const status = normalizeStatus(i.status);
              return status === 'sent' || status === 'overdue';
            }).length}
          </div>
        </div>
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Outstanding Balance</div>
          <div className="text-2xl font-bold text-red-600">${unpaidAmount.toLocaleString()}</div>
        </div>
      </div>

      {/* Filters */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-4 mb-6">
        <div className="flex gap-2">
          {(['all', 'unpaid', 'overdue', 'paid'] as const).map(f => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                filter === f
                  ? 'bg-blue-600 text-white'
                  : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
              }`}
            >
              {f.charAt(0).toUpperCase() + f.slice(1)}
            </button>
          ))}
        </div>
      </div>

      {/* Invoices List */}
      {filteredInvoices.length === 0 ? (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
          <p className="text-gray-400">No invoices found</p>
        </div>
      ) : (
        <div className="space-y-4">
          {filteredInvoices.map(invoice => (
            <div key={invoice.id} className="bg-white rounded-xl shadow-sm border border-gray-200 p-6">
              <div className="flex justify-between items-start">
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    <CreditCard className="w-5 h-5 text-gray-400" />
                    <h3 className="text-lg font-bold text-slate-900">{invoice.number}</h3>
                  </div>
                  <div className="text-sm text-gray-600 space-y-1">
                    <div>Amount: <span className="font-semibold text-slate-900">${invoice.amount.toLocaleString()}</span></div>
                    <div>Due Date: {new Date(invoice.dueDate).toLocaleDateString()}</div>
                    <div>Client: {invoice.client?.name || 'Client'}</div>
                  </div>
                </div>
                <div className="flex flex-col items-end gap-3">
                  <span className={`px-4 py-2 rounded-full text-sm font-bold ${
                    normalizeStatus(invoice.status) === 'paid' ? 'bg-green-100 text-green-700' :
                    normalizeStatus(invoice.status) === 'overdue' ? 'bg-red-100 text-red-700' :
                    'bg-yellow-100 text-yellow-700'
                  }`}>
                    {formatStatusLabel(invoice.status)}
                  </span>
                  <div className="flex gap-2">
                    {normalizeStatus(invoice.status) === 'paid' && (
                      <div className="flex items-center gap-1 text-green-600 text-sm">
                        <CheckCircle className="w-4 h-4" />
                        Paid
                      </div>
                    )}
                    {getBalance(invoice) > 0 && normalizeStatus(invoice.status) !== 'draft' && (
                      <button
                        onClick={() => handleCheckout(invoice)}
                        className="px-3 py-2 text-sm font-semibold text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors"
                      >
                        Pay Now
                      </button>
                    )}
                    <button className="p-2 text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded-lg">
                      <Download className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

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
    </div>
  );
};

export default ClientInvoices;

