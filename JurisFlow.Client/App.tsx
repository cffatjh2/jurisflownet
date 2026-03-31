import React, { useState, useEffect, Suspense } from 'react';
import GoogleAuthCallback from './components/GoogleAuthCallback';
import MicrosoftAuthCallback from './components/MicrosoftAuthCallback';
import ZoomAuthCallback from './components/ZoomAuthCallback';
import IntegrationOAuthCallback from './components/IntegrationOAuthCallback';
import { LayoutDashboard, Briefcase, Scale, BrainCircuit, Plus, Calendar as CalendarIcon, CreditCard, Bell, Folder, Mail, Users, Settings as SettingsIcon, Search, Timer, CheckSquare, Video, BarChart3, FileText } from './components/Icons';

// --- Lazy-loaded page components (code-split into separate chunks) ---
const Dashboard = React.lazy(() => import('./components/Dashboard'));
const Matters = React.lazy(() => import('./components/Matters'));
const AIDrafter = React.lazy(() => import('./components/AIDrafter'));
const Billing = React.lazy(() => import('./components/Billing'));
const CalendarView = React.lazy(() => import('./components/CalendarView'));
const Documents = React.lazy(() => import('./components/Documents'));
const Communications = React.lazy(() => import('./components/Communications'));
const VideoCall = React.lazy(() => import('./components/VideoCall'));
const CRM = React.lazy(() => import('./components/CRM'));
const Intake = React.lazy(() => import('./components/Intake'));
const TimeTracker = React.lazy(() => import('./components/TimeTracker'));
const Tasks = React.lazy(() => import('./components/Tasks'));
const Settings = React.lazy(() => import('./components/Settings'));
const Reports = React.lazy(() => import('./components/Reports'));
const Employees = React.lazy(() => import('./components/Employees'));
const TrustAccounting = React.lazy(() => import('./components/TrustAccounting'));

// --- Eagerly-loaded (small, auth-critical, or always-visible) ---
import Login from './components/Login';
import ForgotPassword from './components/ForgotPassword';
import ResetPassword from './components/ResetPassword';
import CommandPalette from './components/CommandPalette';
import Notifications from './components/Notifications';
import GlobalTimer from './components/GlobalTimer';
import ClientPortal from './components/client/ClientPortal';
import { ToastProvider } from './components/Toast';
import { ConfirmProvider } from './components/ConfirmDialog';
import { ClientAuthProvider, useClientAuth } from './contexts/ClientAuthContext';
import { LanguageProvider, useTranslation, Currency } from './contexts/LanguageContext';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { DataProvider, useData } from './contexts/DataContext';
import { ThemeProvider } from './contexts/ThemeContext';
import { Language } from './translations';

// Skeleton loader shown while lazy components load
const LazyLoadFallback = () => (
  <div className="flex-1 flex items-center justify-center h-full bg-gray-50">
    <div className="flex flex-col items-center gap-3">
      <div className="w-8 h-8 border-3 border-slate-300 border-t-slate-700 rounded-full animate-spin" />
      <span className="text-sm text-gray-400 font-medium">Loading…</span>
    </div>
  </div>
);

type ActiveTab = 'dashboard' | 'matters' | 'documents' | 'communications' | 'crm' | 'intake' | 'ai' | 'billing' | 'calendar' | 'time' | 'tasks' | 'settings' | 'videocall' | 'reports' | 'employees' | 'trust';

const FLAGS: Record<Language, string> = {
  en: 'EN',
  tr: 'TR',
  de: 'DE',
  fr: 'FR',
  it: 'IT',
  pl: 'PL',
  ru: 'RU'
};

const MainLayout = () => {
  const [activeTab, setActiveTab] = useState<ActiveTab>('dashboard');
  const { t, language, setLanguage, currency, setCurrency } = useTranslation();
  const { user, logout } = useAuth();

  const [showLangMenu, setShowLangMenu] = useState(false);
  const [showCurrencyMenu, setShowCurrencyMenu] = useState(false);
  const [showProfileMenu, setShowProfileMenu] = useState(false);

  // Command Palette State
  const [isCmdOpen, setIsCmdOpen] = useState(false);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setIsCmdOpen(prev => !prev);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Lightweight in-app navigation bus (used by Notifications / dashboard quick actions)
  useEffect(() => {
    const handler = (e: any) => {
      const tab = e?.detail?.tab;
      if (!tab) return;
      setActiveTab(tab as ActiveTab);
    };
    window.addEventListener('jf:navigate', handler as any);
    return () => window.removeEventListener('jf:navigate', handler as any);
  }, []);

  const NavButton = ({ tab, icon: Icon, label, badge }: any) => (
    <button
      onClick={() => setActiveTab(tab)}
      className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg transition-all duration-200 group mb-1 ${activeTab === tab
        ? 'bg-slate-700 text-white font-medium shadow-sm'
        : 'text-gray-400 hover:bg-slate-800 hover:text-white'
        }`}
    >
      <Icon className={`w-5 h-5 ${activeTab === tab ? 'text-white' : 'text-gray-500 group-hover:text-gray-300'}`} />
      <span className="text-sm">{label}</span>
      {badge && <span className="ml-auto text-[10px] bg-red-500 text-white px-1.5 py-0.5 rounded-full font-bold">{badge}</span>}
    </button>
  );

  return (
    <div className="flex h-screen w-full bg-slate-900 font-sans overflow-hidden">

      <CommandPalette
        isOpen={isCmdOpen}
        onClose={() => setIsCmdOpen(false)}
        onNavigate={(tab) => setActiveTab(tab as ActiveTab)}
      />

      {/* SIDEBAR */}
      <aside className="w-64 bg-slate-900 flex flex-col z-20 relative border-r border-slate-700">
        <div className="h-16 flex items-center px-6 mb-2">
          <Scale className="w-6 h-6 text-white mr-3" />
          <span className="text-xl font-bold text-white tracking-tight">Juris<span className="text-primary-500">Flow</span></span>
        </div>

        <nav className="flex-1 px-3 space-y-1 overflow-y-auto">
          <NavButton tab="dashboard" icon={LayoutDashboard} label={t('nav_dashboard')} />
          <NavButton tab="matters" icon={Briefcase} label={t('nav_matters')} />
          {/* Swapped order: CRM before Tasks */}
          <NavButton tab="crm" icon={Users} label={t('nav_crm')} />
          <NavButton tab="intake" icon={FileText} label="Intake" />
          <NavButton tab="tasks" icon={CheckSquare} label={t('nav_tasks')} />
          <NavButton tab="communications" icon={Mail} label={t('nav_comms')} />
          <NavButton tab="videocall" icon={Video} label="Video Calls" />
          <NavButton tab="documents" icon={Folder} label={t('nav_docs')} />
          <NavButton tab="calendar" icon={CalendarIcon} label={t('nav_calendar')} />
          <NavButton tab="billing" icon={CreditCard} label={t('nav_billing')} />
          <NavButton tab="trust" icon={Scale} label="Trust (IOLTA)" />
          <NavButton tab="time" icon={Timer} label={t('nav_time')} />
          <NavButton tab="employees" icon={Users} label={t('nav_employees') || 'Employees'} />
          <NavButton tab="reports" icon={BarChart3} label="Reports" />
          <NavButton tab="ai" icon={BrainCircuit} label={t('nav_ai')} />
        </nav>

        <div className="p-4 border-t border-slate-700">
          <div className="relative">
            <button
              onClick={() => { setShowProfileMenu(!showProfileMenu); }}
              className="w-full flex items-center gap-3 p-2 rounded-lg hover:bg-slate-800 transition-colors cursor-pointer"
            >
              <div className="w-8 h-8 rounded-full bg-gradient-to-tr from-primary-500 to-indigo-500 text-white font-bold flex items-center justify-center text-xs shadow-lg">
                {user?.initials || 'U'}
              </div>
              <div className="flex flex-col items-start overflow-hidden">
                <span className="text-sm font-semibold text-white truncate w-full text-left">{user?.name}</span>
                <span className="text-xs text-gray-400">{user?.role}</span>
              </div>
              <SettingsIcon className="w-4 h-4 text-gray-500 ml-auto flex-shrink-0" />
            </button>

            {showProfileMenu && (
              <div className="absolute bottom-full left-0 w-full mb-2 bg-slate-800 border border-slate-700 rounded-lg shadow-xl overflow-hidden z-50">
                <button onClick={() => { setActiveTab('settings'); setShowProfileMenu(false); }} className="w-full text-left px-4 py-3 text-sm text-gray-300 hover:bg-slate-700 hover:text-white border-b border-slate-700">
                  {t('settings')}
                </button>
                <button onClick={logout} className="w-full text-left px-4 py-3 text-sm text-red-400 hover:bg-slate-700 hover:text-red-300 font-medium">
                  {t('sign_out')}
                </button>
              </div>
            )}
          </div>
        </div>
      </aside>

      {/* MAIN CONTENT */}
      <main className="flex-1 flex flex-col min-w-0 bg-gray-50 relative">
        <header className="h-14 bg-white border-b border-gray-200 flex items-center justify-between px-4 z-10 sticky top-0">
          <div className="relative w-80 lg:w-96 hidden md:block">
            {/* Added CMD+K hint */}
            <div className="absolute right-3 top-1/2 -translate-y-1/2 flex items-center gap-1 pointer-events-none">
              <kbd className="bg-gray-100 text-gray-500 text-[10px] px-1.5 py-0.5 rounded border border-gray-200 font-bold font-sans">Cmd + K</kbd>
            </div>
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
            <button
              onClick={() => setIsCmdOpen(true)}
              className="w-full h-10 pl-9 pr-16 bg-gray-100 border-transparent hover:bg-white border hover:border-primary-300 rounded-lg text-sm text-left text-gray-400 transition-all whitespace-nowrap overflow-hidden text-ellipsis"
            >
              {String(t('search_placeholder')).replace(/\s*\(Cmd\+K\)\s*$/i, '')}
            </button>
          </div>

          <div className="flex items-center gap-3">
            <div className="relative">
              <GlobalTimer />
            </div>
            <Notifications />
            <button
              onClick={() => setActiveTab('matters')}
              className="flex items-center gap-2 bg-slate-800 text-white px-3.5 py-2 rounded-lg text-sm font-medium hover:bg-slate-900 transition-all shadow-sm"
            >
              <Plus className="w-4 h-4" />
              <span>{t('create_btn')}</span>
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-hidden relative">
          <Suspense fallback={<LazyLoadFallback />}>
            <ComponentSwitcher activeTab={activeTab} />
          </Suspense>
        </div>
      </main>
    </div>
  );
};

// Simplified Component Switcher - No Props Passed
const ComponentSwitcher = ({ activeTab }: { activeTab: ActiveTab }) => {
  const { matters } = useData();

  switch (activeTab) {
    case 'dashboard': return <Dashboard />;
    case 'matters': return <Matters />;
    case 'tasks': return <Tasks />;
    case 'documents': return <Documents />;
    case 'communications': return <Communications />;
    case 'videocall': return <VideoCall />;
    case 'crm': return <CRM />;
    case 'intake': return <Intake />;
    case 'billing': return <Billing />;
    case 'trust': return <TrustAccounting />;
    case 'calendar': return <CalendarView />;
    case 'ai': return <AIDrafter matters={matters} />;
    case 'time': return <TimeTracker />;
    case 'reports': return <Reports />;
    case 'employees': return <Employees />;
    case 'settings': return <Settings />;
    default: return <Dashboard />;
  }
}

const AppContent = () => {
  const { isAuthenticated: isClientAuthenticated, loading: isClientLoading } = useClientAuth();
  const { isAuthenticated } = useAuth();

  // Handle OAuth callback
  useEffect(() => {
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('code') || urlParams.get('error')) {
      if (window.location.pathname.includes('/auth/google/callback')) {
        // GoogleAuthCallback will handle this
        return;
      }
      if (window.location.pathname.includes('/auth/microsoft/callback')) {
        // MicrosoftAuthCallback will handle this
        return;
      }
      if (window.location.pathname.includes('/auth/zoom/callback')) {
        // ZoomAuthCallback will handle this
        return;
      }
      if (window.location.pathname.includes('/auth/integrations/') && window.location.pathname.includes('/callback')) {
        // IntegrationOAuthCallback will handle this
        return;
      }
    }
  }, []);

  const urlParams = new URLSearchParams(window.location.search);
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/google/callback')) {
    return <GoogleAuthCallback />;
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/microsoft/callback')) {
    return <MicrosoftAuthCallback />;
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/zoom/callback')) {
    return <ZoomAuthCallback />;
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/integrations/') && window.location.pathname.includes('/callback')) {
    return <IntegrationOAuthCallback />;
  }

  // Check if client portal route (using hash or pathname)
  const isClientPortal = window.location.pathname === '/client' ||
    window.location.pathname.startsWith('/client/') ||
    window.location.hash === '#/client';

  if (isClientPortal) {
    if (isClientLoading) {
      return (
        <div className="flex items-center justify-center min-h-screen bg-gray-50">
          <div className="text-gray-400">Loading...</div>
        </div>
      );
    }
    return isClientAuthenticated ? <ClientPortal /> : <Login initialUserType="client" />;
  }

  // Check for password reset routes
  if (window.location.pathname === '/forgot-password') {
    return <ForgotPassword />;
  }

  if (window.location.pathname === '/reset-password') {
    return <ResetPassword />;
  }

  return isAuthenticated ? <MainLayout /> : <Login />;
}

const App = () => {
  try {
    return (
      <ThemeProvider>
        <LanguageProvider>
          <AuthProvider>
            <ClientAuthProvider>
              <DataProvider>
                <ToastProvider>
                  <ConfirmProvider>
                    <AppContent />
                  </ConfirmProvider>
                </ToastProvider>
              </DataProvider>
            </ClientAuthProvider>
          </AuthProvider>
        </LanguageProvider>
      </ThemeProvider>
    );
  } catch (error) {
    throw error;
  }
}

export default App;
