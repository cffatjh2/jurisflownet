import React, { Suspense, useEffect, useState } from 'react';
import {
  LayoutDashboard,
  Briefcase,
  Scale,
  BrainCircuit,
  Plus,
  Calendar as CalendarIcon,
  CreditCard,
  Folder,
  Mail,
  Users,
  Settings as SettingsIcon,
  Search,
  Timer,
  CheckSquare,
  Video,
  BarChart3,
  FileText
} from './Icons';
import CommandPalette from './CommandPalette';
import Notifications from './Notifications';
import GlobalTimer from './GlobalTimer';
import { useTranslation } from '../contexts/LanguageContext';
import { useAuth } from '../contexts/AuthContext';
import { DataProvider, useData } from '../contexts/DataContext';

type ActiveTab =
  | 'dashboard'
  | 'matters'
  | 'documents'
  | 'communications'
  | 'crm'
  | 'intake'
  | 'ai'
  | 'billing'
  | 'calendar'
  | 'time'
  | 'tasks'
  | 'settings'
  | 'videocall'
  | 'reports'
  | 'employees'
  | 'trust';

const TAB_ORDER: ActiveTab[] = [
  'dashboard',
  'matters',
  'documents',
  'communications',
  'crm',
  'intake',
  'ai',
  'billing',
  'calendar',
  'time',
  'tasks',
  'settings',
  'videocall',
  'reports',
  'employees',
  'trust'
];

const isActiveTab = (value: unknown): value is ActiveTab =>
  typeof value === 'string' && TAB_ORDER.includes(value as ActiveTab);

const loadDashboard = () => import('./Dashboard');
const loadMatters = () => import('./Matters');
const loadAIDrafter = () => import('./AIDrafter');
const loadBilling = () => import('./Billing');
const loadCalendarView = () => import('./CalendarView');
const loadDocuments = () => import('./Documents');
const loadCommunications = () => import('./Communications');
const loadVideoCall = () => import('./VideoCall');
const loadCRM = () => import('./CRM');
const loadIntake = () => import('./Intake');
const loadTimeTracker = () => import('./TimeTracker');
const loadTasks = () => import('./Tasks');
const loadSettings = () => import('./Settings');
const loadReports = () => import('./Reports');
const loadEmployees = () => import('./Employees');
const loadTrustAccounting = () => import('./TrustAccounting');

const Dashboard = React.lazy(loadDashboard);
const Matters = React.lazy(loadMatters);
const AIDrafter = React.lazy(loadAIDrafter);
const Billing = React.lazy(loadBilling);
const CalendarView = React.lazy(loadCalendarView);
const Documents = React.lazy(loadDocuments);
const Communications = React.lazy(loadCommunications);
const VideoCall = React.lazy(loadVideoCall);
const CRM = React.lazy(loadCRM);
const Intake = React.lazy(loadIntake);
const TimeTracker = React.lazy(loadTimeTracker);
const Tasks = React.lazy(loadTasks);
const Settings = React.lazy(loadSettings);
const Reports = React.lazy(loadReports);
const Employees = React.lazy(loadEmployees);
const TrustAccounting = React.lazy(loadTrustAccounting);

const TAB_PRELOADERS: Record<ActiveTab, () => Promise<unknown>> = {
  dashboard: loadDashboard,
  matters: loadMatters,
  documents: loadDocuments,
  communications: loadCommunications,
  crm: loadCRM,
  intake: loadIntake,
  ai: loadAIDrafter,
  billing: loadBilling,
  calendar: loadCalendarView,
  time: loadTimeTracker,
  tasks: loadTasks,
  settings: loadSettings,
  videocall: loadVideoCall,
  reports: loadReports,
  employees: loadEmployees,
  trust: loadTrustAccounting
};

const LazyLoadFallback = () => (
  <div className="flex-1 flex items-center justify-center h-full bg-gray-50">
    <div className="flex flex-col items-center gap-3">
      <div className="w-8 h-8 border-3 border-slate-300 border-t-slate-700 rounded-full animate-spin" />
      <span className="text-sm text-gray-400 font-medium">Loading...</span>
    </div>
  </div>
);

const ComponentSwitcher = ({ activeTab }: { activeTab: ActiveTab }) => {
  const { matters } = useData();

  switch (activeTab) {
    case 'dashboard':
      return <Dashboard />;
    case 'matters':
      return <Matters />;
    case 'tasks':
      return <Tasks />;
    case 'documents':
      return <Documents />;
    case 'communications':
      return <Communications />;
    case 'videocall':
      return <VideoCall />;
    case 'crm':
      return <CRM />;
    case 'intake':
      return <Intake />;
    case 'billing':
      return <Billing />;
    case 'trust':
      return <TrustAccounting />;
    case 'calendar':
      return <CalendarView />;
    case 'ai':
      return <AIDrafter matters={matters} />;
    case 'time':
      return <TimeTracker />;
    case 'reports':
      return <Reports />;
    case 'employees':
      return <Employees />;
    case 'settings':
      return <Settings />;
    default:
      return <Dashboard />;
  }
};

const MainLayout = () => {
  const [activeTab, setActiveTab] = useState<ActiveTab>('dashboard');
  const [mountedTabs, setMountedTabs] = useState<ActiveTab[]>(['dashboard']);
  const { t } = useTranslation();
  const { user, logout } = useAuth();

  const [showProfileMenu, setShowProfileMenu] = useState(false);
  const [isCmdOpen, setIsCmdOpen] = useState(false);

  const activateTab = (tab: ActiveTab) => {
    setActiveTab(tab);
    setMountedTabs(prev => (prev.includes(tab) ? prev : [...prev, tab]));
  };

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

  useEffect(() => {
    const handler = (e: any) => {
      const tab = e?.detail?.tab;
      if (!isActiveTab(tab)) return;
      activateTab(tab);
    };
    window.addEventListener('jf:navigate', handler as any);
    return () => window.removeEventListener('jf:navigate', handler as any);
  }, []);

  useEffect(() => {
    const preloadTabs: ActiveTab[] = ['matters', 'tasks', 'documents', 'calendar', 'communications', 'billing', 'crm'];
    let disposed = false;
    let timer: number | null = null;
    let idleId: number | null = null;

    const preloadSequentially = () => {
      let index = 0;
      const run = () => {
        if (disposed || index >= preloadTabs.length) return;
        const tab = preloadTabs[index++];
        void TAB_PRELOADERS[tab]();
        timer = window.setTimeout(run, 120);
      };
      run();
    };

    if (typeof window !== 'undefined' && typeof (window as any).requestIdleCallback === 'function') {
      idleId = (window as any).requestIdleCallback(() => preloadSequentially(), { timeout: 2000 });
    } else {
      timer = window.setTimeout(preloadSequentially, 900);
    }

    return () => {
      disposed = true;
      if (timer !== null) window.clearTimeout(timer);
      if (idleId !== null && typeof (window as any).cancelIdleCallback === 'function') {
        (window as any).cancelIdleCallback(idleId);
      }
    };
  }, []);

  const NavButton = ({ tab, icon: Icon, label, badge }: any) => (
    <button
      onClick={() => activateTab(tab as ActiveTab)}
      className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg transition-all duration-200 group mb-1 ${
        activeTab === tab
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
      <CommandPalette isOpen={isCmdOpen} onClose={() => setIsCmdOpen(false)} onNavigate={tab => isActiveTab(tab) && activateTab(tab)} />

      <aside className="w-64 bg-slate-900 flex flex-col z-20 relative border-r border-slate-700">
        <div className="h-16 flex items-center px-6 mb-2">
          <Scale className="w-6 h-6 text-white mr-3" />
          <span className="text-xl font-bold text-white tracking-tight">
            Juris<span className="text-primary-500">Flow</span>
          </span>
        </div>

        <nav className="flex-1 px-3 space-y-1 overflow-y-auto">
          <NavButton tab="dashboard" icon={LayoutDashboard} label={t('nav_dashboard')} />
          <NavButton tab="matters" icon={Briefcase} label={t('nav_matters')} />
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
              onClick={() => {
                setShowProfileMenu(!showProfileMenu);
              }}
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
                <button
                  onClick={() => {
                    activateTab('settings');
                    setShowProfileMenu(false);
                  }}
                  className="w-full text-left px-4 py-3 text-sm text-gray-300 hover:bg-slate-700 hover:text-white border-b border-slate-700"
                >
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

      <main className="flex-1 flex flex-col min-w-0 bg-gray-50 relative">
        <header className="h-14 bg-white border-b border-gray-200 flex items-center justify-between px-4 z-10 sticky top-0">
          <div className="relative w-80 lg:w-96 hidden md:block">
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
              onClick={() => activateTab('matters')}
              className="flex items-center gap-2 bg-slate-800 text-white px-3.5 py-2 rounded-lg text-sm font-medium hover:bg-slate-900 transition-all shadow-sm"
            >
              <Plus className="w-4 h-4" />
              <span>{t('create_btn')}</span>
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-hidden relative">
          {TAB_ORDER.filter(tab => mountedTabs.includes(tab)).map(tab => (
            <section key={tab} className={activeTab === tab ? 'h-full' : 'hidden'} aria-hidden={activeTab !== tab}>
              <Suspense fallback={activeTab === tab ? <LazyLoadFallback /> : null}>
                <ComponentSwitcher activeTab={tab} />
              </Suspense>
            </section>
          ))}
        </div>
      </main>
    </div>
  );
};

const AuthenticatedShell: React.FC = () => {
  return (
    <DataProvider>
      <MainLayout />
    </DataProvider>
  );
};

export default AuthenticatedShell;
