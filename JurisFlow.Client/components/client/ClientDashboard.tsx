import React, { useState, useEffect } from 'react';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import { Matter, Invoice } from '../../types';
import { clientApi } from '../../services/clientApi';

type ClientTab =
  | 'dashboard'
  | 'matters'
  | 'invoices'
  | 'payments'
  | 'documents'
  | 'messages'
  | 'calendar'
  | 'appointments'
  | 'signatures'
  | 'profile'
  | 'videocall';

interface ClientDashboardProps {
  onNavigate?: (tab: ClientTab) => void;
}

const ClientDashboard: React.FC<ClientDashboardProps> = ({ onNavigate }) => {
  const { client } = useClientAuth();
  const [matters, setMatters] = useState<Matter[]>([]);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [documents, setDocuments] = useState<any[]>([]);
  const [messages, setMessages] = useState<any[]>([]);
  const [notifications, setNotifications] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadData = async () => {
      try {
        const [mattersData, invoicesData, docsData, msgData, notifData] = await Promise.all([
          clientApi.fetchJson('/matters'),
          clientApi.fetchJson('/invoices'),
          clientApi.fetchJson('/documents'),
          clientApi.fetchJson('/messages'),
          clientApi.fetchJson('/notifications')
        ]);

        setMatters(Array.isArray(mattersData) ? mattersData : []);
        setInvoices(Array.isArray(invoicesData) ? invoicesData : []);
        setDocuments(Array.isArray(docsData) ? docsData : []);
        setMessages(Array.isArray(msgData) ? msgData : []);
        setNotifications(Array.isArray(notifData) ? notifData : []);
      } catch (error) {
        console.error('Error loading dashboard data:', error);
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  const activeMatters = matters.filter(m => m.status === 'Open');
  const totalInvoices = invoices.length;
  const unpaidInvoices = invoices.filter((i: any) => {
    const status = i.status?.toLowerCase();
    return status === 'overdue' || status === 'sent';
  }).length;
  const totalAmount = invoices.reduce((sum, inv) => sum + inv.amount, 0);
  const unpaidAmount = invoices
    .filter((i: any) => {
      const status = i.status?.toLowerCase();
      return status === 'overdue' || status === 'sent';
    })
    .reduce((sum, inv) => sum + inv.amount, 0);

  const unreadMessages = messages.filter((m: any) => !m.read).length;
  const unreadNotifs = notifications.filter((n: any) => !n.read).length;

  const todayStart = new Date();
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date();
  todayEnd.setHours(23, 59, 59, 999);
  const todayDocs = documents.filter((d: any) => {
    const ts = new Date(d.updatedAt || d.createdAt).getTime();
    return ts >= todayStart.getTime() && ts <= todayEnd.getTime();
  }).length;

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-slate-900">Welcome back, {client?.name}</h2>
        <p className="text-gray-600 mt-1">Here's an overview of your legal matters</p>
      </div>

      {/* Today cards */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mb-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-bold text-slate-900">What's Today?</h3>
          <div className="text-xs text-gray-400">{new Date().toLocaleDateString()}</div>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
            <div className="text-xs font-bold text-gray-400 uppercase">New Message</div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{unreadMessages}</div>
            <div className="mt-1 text-xs text-gray-600">Unread</div>
          </div>
          <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
            <div className="text-xs font-bold text-gray-400 uppercase">Notification</div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{unreadNotifs}</div>
            <div className="mt-1 text-xs text-gray-600">Unread</div>
          </div>
          <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
            <div className="text-xs font-bold text-gray-400 uppercase">Document</div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{todayDocs}</div>
            <div className="mt-1 text-xs text-gray-600">Uploaded/Updated today</div>
          </div>
          <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
            <div className="text-xs font-bold text-gray-400 uppercase">Payment</div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{unpaidInvoices}</div>
            <div className="mt-1 text-xs text-gray-600">Pending invoices</div>
            {unpaidInvoices > 0 && (
              <button
                type="button"
                onClick={() => onNavigate?.('payments')}
                className="mt-2 text-xs font-semibold text-blue-600 hover:text-blue-700"
              >
                Go to Payments
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Active Cases</div>
          <div className="text-3xl font-bold text-slate-900">{activeMatters.length}</div>
        </div>
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Total Invoices</div>
          <div className="text-3xl font-bold text-slate-900">{totalInvoices}</div>
        </div>
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Unpaid Invoices</div>
          <div className="text-3xl font-bold text-red-600">{unpaidInvoices}</div>
        </div>
        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
          <div className="text-sm text-gray-600 mb-1">Outstanding Balance</div>
          <div className="text-3xl font-bold text-slate-900">${unpaidAmount.toLocaleString()}</div>
        </div>
      </div>

      {/* Recent Matters */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mb-6">
        <h3 className="text-lg font-bold text-slate-900 mb-4">Recent Cases</h3>
        {matters.length === 0 ? (
          <div className="text-center py-8 text-gray-400">
            <p>No cases found</p>
          </div>
        ) : (
          <div className="space-y-3">
            {matters.slice(0, 5).map(matter => (
              <div key={matter.id} className="flex items-center justify-between p-4 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors">
                <div>
                  <div className="font-semibold text-slate-900">{matter.name}</div>
                  <div className="text-sm text-gray-600">Case #: {matter.caseNumber}</div>
                </div>
                <div className="text-right">
                  <span className={`px-3 py-1 rounded-full text-xs font-bold ${matter.status === 'Open' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'
                    }`}>
                    {matter.status}
                  </span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Recent Invoices */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6">
        <h3 className="text-lg font-bold text-slate-900 mb-4">Recent Invoices</h3>
        {invoices.length === 0 ? (
          <div className="text-center py-8 text-gray-400">
            <p>No invoices found</p>
          </div>
        ) : (
          <div className="space-y-3">
            {invoices.slice(0, 5).map(invoice => (
              <div key={invoice.id} className="flex items-center justify-between p-4 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors">
                <div>
                  <div className="font-semibold text-slate-900">{invoice.number}</div>
                  <div className="text-sm text-gray-600">Due: {new Date(invoice.dueDate).toLocaleDateString()}</div>
                </div>
                <div className="text-right">
                  <div className="font-bold text-slate-900">${invoice.amount.toLocaleString()}</div>
                  <span className={`px-3 py-1 rounded-full text-xs font-bold ${
                    invoice.status?.toLowerCase() === 'paid' ? 'bg-green-100 text-green-700' :
                    invoice.status?.toLowerCase() === 'overdue' ? 'bg-red-100 text-red-700' :
                    'bg-yellow-100 text-yellow-700'
                  }`}>
                    {invoice.status}
                  </span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};

export default ClientDashboard;

