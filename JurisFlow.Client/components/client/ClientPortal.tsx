import React, { useEffect, useState } from 'react';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import ClientLogin from './ClientLogin';
import ClientDashboard from './ClientDashboard';
import ClientMatters from './ClientMatters';
import ClientInvoices from './ClientInvoices';
import ClientDocuments from './ClientDocuments';
import ClientMessages from './ClientMessages';
import ClientProfile from './ClientProfile';
import VideoCall from '../VideoCall';
import ClientCalendar from './ClientCalendar';
import ClientAppointments from './ClientAppointments';
import ClientPayments from './ClientPayments';
import ClientSignatures from './ClientSignatures';
import ClientNotificationsPanel from './ClientNotificationsPanel';
import { Briefcase, CreditCard, Folder, Mail, User, Bell, Scale, X, Calendar as CalendarIcon, Video, Clock, Edit3, DollarSign, Menu } from '../Icons';
import { clientApi } from '../../services/clientApi';

type ClientTab = 'dashboard' | 'matters' | 'invoices' | 'payments' | 'documents' | 'messages' | 'calendar' | 'appointments' | 'signatures' | 'profile' | 'videocall';

const ClientPortal: React.FC = () => {
  const { isAuthenticated, client, logout, loading } = useClientAuth();
  const [activeTab, setActiveTab] = useState<ClientTab>('dashboard');
  const [showNotifications, setShowNotifications] = useState(false);
  const [unreadNotifications, setUnreadNotifications] = useState(0);
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false);

  useEffect(() => {
    if (!isAuthenticated) return;
    const loadNotifications = async () => {
      try {
        const data = await clientApi.fetchJson('/notifications');
        const count = Array.isArray(data) ? data.filter((n: any) => !n.read).length : 0;
        setUnreadNotifications(count);
      } catch (error) {
        console.error('Failed to load client notifications', error);
      }
    };
    loadNotifications();
  }, [isAuthenticated]);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-50">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return <ClientLogin />;
  }

  const handleNavigate = (tab: ClientTab) => {
    setActiveTab(tab);
    setIsMobileNavOpen(false);
  };

  const NavButton = ({ tab, icon: Icon, label }: { tab: ClientTab; icon: any; label: string }) => (
    <button
      onClick={() => handleNavigate(tab)}
      className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg transition-all duration-200 ${activeTab === tab
        ? 'bg-blue-600 text-white font-medium shadow-sm'
        : 'text-gray-600 hover:bg-gray-100'
        }`}
    >
      <Icon className={`w-5 h-5 ${activeTab === tab ? 'text-white' : 'text-gray-500'}`} />
      <span className="text-sm">{label}</span>
    </button>
  );

  const getPageTitle = () => {
    switch (activeTab) {
      case 'dashboard': return 'Dashboard';
      case 'matters': return 'My Cases';
      case 'invoices': return 'Invoices';
      case 'payments': return 'Payments';
      case 'documents': return 'Documents';
      case 'messages': return 'Messages';
      case 'calendar': return 'Calendar';
      case 'appointments': return 'Appointments';
      case 'signatures': return 'E-Signature';
      case 'profile': return 'Profile';
      case 'videocall': return 'Video Call';
      default: return '';
    }
  };

  useEffect(() => {
    if (!isMobileNavOpen) return;

    const originalOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    return () => {
      document.body.style.overflow = originalOverflow;
    };
  }, [isMobileNavOpen]);

  return (
    <div className="flex h-[100dvh] w-full bg-gray-50 font-sans overflow-hidden">
      {isMobileNavOpen && (
        <button
          type="button"
          aria-label="Close navigation"
          onClick={() => setIsMobileNavOpen(false)}
          className="fixed inset-0 z-20 bg-slate-950/50 md:hidden"
        />
      )}

      {/* SIDEBAR */}
      <aside className={`fixed inset-y-0 left-0 z-30 flex w-72 max-w-[85vw] flex-col border-r border-gray-200 bg-white transition-transform duration-200 md:static md:z-20 md:w-64 md:max-w-none ${isMobileNavOpen ? 'translate-x-0' : '-translate-x-full md:translate-x-0'}`}>
        <div className="h-16 flex items-center px-6 mb-2 border-b border-gray-200">
          <Scale className="w-6 h-6 text-blue-600 mr-3" />
          <span className="text-xl font-bold text-slate-900 tracking-tight">Juris<span className="text-blue-600">Flow</span></span>
          <button
            type="button"
            onClick={() => setIsMobileNavOpen(false)}
            className="ml-auto rounded-lg p-2 text-gray-500 hover:bg-gray-100 hover:text-gray-900 md:hidden"
            aria-label="Close navigation"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="px-4 py-4 border-b border-gray-200">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-gradient-to-tr from-blue-500 to-indigo-500 text-white font-bold flex items-center justify-center text-sm shadow-lg">
              {client?.name?.charAt(0) || 'C'}
            </div>
            <div className="flex flex-col items-start overflow-hidden">
              <span className="text-sm font-semibold text-slate-900 truncate w-full text-left">{client?.name}</span>
              <span className="text-xs text-gray-500">Client Portal</span>
            </div>
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">
          <NavButton tab="dashboard" icon={Briefcase} label="Dashboard" />
          <NavButton tab="matters" icon={Briefcase} label="My Cases" />

          <div className="pt-3 pb-1">
            <span className="px-3 text-xs font-semibold text-gray-400 uppercase">Finance</span>
          </div>
          <NavButton tab="invoices" icon={CreditCard} label="Invoices" />
          <NavButton tab="payments" icon={DollarSign} label="Payments" />

          <div className="pt-3 pb-1">
            <span className="px-3 text-xs font-semibold text-gray-400 uppercase">Communication</span>
          </div>
          <NavButton tab="messages" icon={Mail} label="Messages" />
          <NavButton tab="appointments" icon={Clock} label="Appointments" />
          <NavButton tab="videocall" icon={Video} label="Video Call" />

          <div className="pt-3 pb-1">
            <span className="px-3 text-xs font-semibold text-gray-400 uppercase">Documents</span>
          </div>
          <NavButton tab="documents" icon={Folder} label="Documents" />
          <NavButton tab="signatures" icon={Edit3} label="E-Signature" />

          <div className="pt-3 pb-1">
            <span className="px-3 text-xs font-semibold text-gray-400 uppercase">Other</span>
          </div>
          <NavButton tab="calendar" icon={CalendarIcon} label="Calendar" />
          <NavButton tab="profile" icon={User} label="Profile" />
        </nav>

        <div className="p-4 border-t border-gray-200">
          <button
            onClick={() => {
              logout();
            }}
            className="w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm text-red-600 hover:bg-red-50 transition-colors"
          >
            <X className="w-5 h-5" />
            <span>Log Out</span>
          </button>
        </div>
      </aside>

      {/* MAIN CONTENT */}
      <main className="flex-1 flex flex-col min-w-0 bg-gray-50 relative">
        <header className="h-14 bg-white border-b border-gray-200 flex items-center justify-between px-3 md:px-4 z-10 sticky top-0">
          <div className="flex min-w-0 items-center gap-2">
            <button
              type="button"
              onClick={() => setIsMobileNavOpen(true)}
              className="inline-flex rounded-lg p-2 text-gray-600 hover:bg-gray-100 md:hidden"
              aria-label="Open navigation"
            >
              <Menu className="h-5 w-5" />
            </button>
            <h1 className="truncate text-base md:text-lg font-bold text-slate-900">{getPageTitle()}</h1>
          </div>

          <div className="flex items-center gap-2 md:gap-4">
            <button
              onClick={() => setShowNotifications(!showNotifications)}
              className="relative p-2 text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded-lg transition-colors"
            >
              <Bell className="w-5 h-5" />
              {unreadNotifications > 0 && (
                <span className="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full"></span>
              )}
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-hidden relative">
          {activeTab === 'dashboard' && <ClientDashboard onNavigate={setActiveTab} />}
          {activeTab === 'matters' && <ClientMatters />}
          {activeTab === 'invoices' && <ClientInvoices />}
          {activeTab === 'payments' && <ClientPayments clientId={client?.id || ''} />}
          {activeTab === 'documents' && <ClientDocuments />}
          {activeTab === 'messages' && <ClientMessages />}
          {activeTab === 'calendar' && <ClientCalendar />}
          {activeTab === 'appointments' && <ClientAppointments clientId={client?.id || ''} />}
          {activeTab === 'signatures' && <ClientSignatures clientId={client?.id || ''} />}
          {activeTab === 'videocall' && <VideoCall />}
          {activeTab === 'profile' && <ClientProfile />}
        </div>
      </main>

      <ClientNotificationsPanel
        open={showNotifications}
        onClose={() => setShowNotifications(false)}
        onUnreadChange={setUnreadNotifications}
      />
    </div>
  );
};

export default ClientPortal;


