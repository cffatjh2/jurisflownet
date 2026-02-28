import React from 'react';
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { Matter, TimeEntry, Task, CalendarEvent } from '../types';
import { Clock, Briefcase, Scale, CreditCard, CheckSquare, Calendar, ChevronRight, AlertTriangle, Check } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { useAuth } from '../contexts/AuthContext';

// We now ignore props and use DataContext to ensure global sync
const StatCard = ({ title, value, subtext, icon: Icon, colorClass }: any) => (
  <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100 flex items-start justify-between hover:shadow-elevation transition-all duration-300">
    <div>
      <p className="text-xs font-bold text-gray-400 uppercase tracking-wide">{title}</p>
      <h3 className="text-3xl font-bold text-slate-800 mt-2 tracking-tight">{value}</h3>
      <p className="text-xs font-medium mt-2 flex items-center gap-1">
        {subtext}
      </p>
    </div>
    <div className={`p-3 rounded-lg ${colorClass} bg-opacity-10`}>
      <Icon className={`w-6 h-6 ${colorClass.replace('bg-', 'text-')}`} />
    </div>
  </div>
);

const Dashboard: React.FC = () => {
  const { t, formatCurrency, formatDate } = useTranslation();
  const { matters, timeEntries, tasks, events, invoices, expenses, documents, updateTask } = useData();
  const { user } = useAuth();

  // Time-based greeting
  const getGreeting = () => {
    const hour = new Date().getHours();
    if (hour >= 5 && hour < 12) return 'Good morning';
    if (hour >= 12 && hour < 18) return 'Good afternoon';
    return 'Good evening';
  };

  const todayStart = new Date();
  todayStart.setHours(0, 0, 0, 0);
  const todayEnd = new Date();
  todayEnd.setHours(23, 59, 59, 999);

  const todayEvents = events.filter(ev => {
    const d = new Date(ev.date).getTime();
    return d >= todayStart.getTime() && d <= todayEnd.getTime();
  });
  const todayCourtEvents = todayEvents.filter(e => e.type === 'Court');

  const todayTasksDue = tasks.filter(tsk => {
    if (tsk.status === 'Done' || tsk.outcome) return false;
    if (!tsk.dueDate) return false;
    const d = new Date(tsk.dueDate).getTime();
    return d >= todayStart.getTime() && d <= todayEnd.getTime();
  });

  const overdueTasks = tasks.filter(tsk => {
    if (tsk.status === 'Done' || tsk.outcome) return false;
    if (!tsk.dueDate) return false;
    return new Date(tsk.dueDate).getTime() < todayStart.getTime();
  });

  // Active tasks (not completed)
  const activeTasks = tasks.filter(t => t.status !== 'Archived' && !t.outcome);

  const todayTimeMinutes = timeEntries
    .filter(te => {
      const d = new Date(te.date).getTime();
      return d >= todayStart.getTime() && d <= todayEnd.getTime();
    })
    .reduce((sum, te) => sum + (te.duration || 0), 0);

  const todayNewDocs = documents.filter(doc => {
    const d = new Date(doc.updatedAt).getTime();
    return d >= todayStart.getTime() && d <= todayEnd.getTime();
  });

  const navigate = (tab: string) => {
    window.dispatchEvent(new CustomEvent('jf:navigate', { detail: { tab } }));
  };

  // Dynamic Calculations
  const totalTrust = matters.reduce((sum, m) => sum + (m.trustBalance || 0), 0);
  const totalBillableHours = timeEntries.reduce((sum, t) => sum + t.duration, 0) / 60;

  const unbilledTimeValue = timeEntries
    .filter(te => !te.billed)
    .reduce((sum, te) => sum + ((te.duration / 60) * (te.rate || 0)), 0);
  const unbilledExpenseValue = expenses
    .filter(e => !e.billed)
    .reduce((sum, e) => sum + (e.amount || 0), 0);
  const totalWIP = unbilledTimeValue + unbilledExpenseValue;

  const overdueInvoices = invoices.filter((inv: any) => inv.status === 'OVERDUE');
  const paidInvoices = invoices.filter((inv: any) => inv.status === 'PAID');
  const paidInvoicesTotal = paidInvoices.reduce((sum, inv) => sum + (inv.amount || 0), 0);
  const outstandingInvoicesTotal = invoices
    .filter((inv: any) => inv.status === 'SENT' || inv.status === 'OVERDUE')
    .reduce((sum, inv) => sum + (inv.amount || 0), 0);

  const upcomingEvents = events
    .slice()
    .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
    .filter(e => new Date(e.date).getTime() >= Date.now());

  const recentDocs = documents
    .slice()
    .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
    .slice(0, 5);

  const recentTime = timeEntries
    .slice()
    .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())
    .slice(0, 5);

  // Mock chart data if no real financial data exists yet
  const financialData = [
    { name: 'Week 1', revenue: 0 },
    { name: 'Week 2', revenue: 0 },
    { name: 'Week 3', revenue: 0 },
    { name: 'Week 4', revenue: 0 },
  ];

  // Check deadline status
  const getDeadlineStatus = (dueDate?: string): 'overdue' | 'urgent' | 'normal' => {
    if (!dueDate) return 'normal';
    const now = new Date();
    const due = new Date(dueDate);
    const hoursUntilDue = (due.getTime() - now.getTime()) / (1000 * 60 * 60);

    if (hoursUntilDue < 0) return 'overdue';
    if (hoursUntilDue <= 24) return 'urgent';
    return 'normal';
  };

  // Quick complete task
  const handleQuickComplete = async (taskId: string) => {
    await updateTask(taskId, {
      outcome: 'success',
      status: 'Done',
      isCompleted: true,
      completedAt: new Date().toISOString()
    });
  };

  return (
    <div className="p-8 overflow-y-auto h-full space-y-8 bg-gray-50/50">

      {/* Header */}
      <div className="flex justify-between items-end">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 tracking-tight">
            {(user?.role || 'User')} Dashboard
          </h1>
          <p className="text-gray-500 mt-1">
            {getGreeting()}, {user?.name || 'User'}. {t('dashboard_overview')} {formatDate(new Date().toString())}.
          </p>
        </div>
        <div className="text-right hidden md:block">
          <p className="text-xs font-bold text-gray-400 uppercase tracking-wider">{t('trust_account')}</p>
          <p className="text-2xl font-bold text-slate-800 font-mono tracking-tight">{formatCurrency(totalTrust)}</p>
        </div>
      </div>

      {/* KPI Stats */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
        <StatCard
          title={t('billable_hours')}
          value={`${totalBillableHours.toFixed(1)} h`}
          subtext={<span className="text-emerald-600 font-semibold">Tracked</span>}
          icon={Clock}
          colorClass="bg-primary-600 text-primary-600"
        />
        <StatCard
          title={t('active_matters')}
          value={matters.length}
          subtext={<span className="text-slate-600 font-semibold">{matters.filter(m => m.status === 'Open').length} {t('open_cases')}</span>}
          icon={Scale}
          colorClass="bg-slate-800 text-slate-800"
        />
        <StatCard
          title="Unbilled WIP"
          value={formatCurrency(totalWIP)}
          subtext={<span className="text-gray-500">Time + Expenses</span>}
          icon={CreditCard}
          colorClass="bg-emerald-600 text-emerald-600"
        />
        <StatCard
          title={t('priority_tasks')}
          value={activeTasks.length}
          subtext={
            overdueTasks.length > 0
              ? <span className="text-red-600 font-semibold">{overdueTasks.length} Overdue</span>
              : <span className="text-amber-600 font-semibold">{tasks.filter(t => t.priority === 'High' && t.status !== 'Done' && !t.outcome).length} High Priority</span>
          }
          icon={CheckSquare}
          colorClass="bg-amber-500 text-amber-500"
        />
      </div>

      {/* Today cards (role-based) */}
      <div className="bg-white rounded-xl shadow-card border border-gray-100 p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-base font-bold text-slate-800">{t('today_overview')}</h3>
          <div className="text-xs text-gray-400">{formatDate(new Date().toISOString())}</div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <button
            onClick={() => navigate('calendar')}
            className="text-left p-4 rounded-xl border border-gray-200 hover:border-primary-200 hover:shadow-sm transition-all bg-gray-50/50"
          >
            <div className="flex items-center justify-between">
              <div className="text-xs font-bold text-gray-400 uppercase">{t('court_hearing')}</div>
              <Calendar className="w-4 h-4 text-primary-600" />
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{todayEvents.length}</div>
            <div className="mt-1 text-xs text-gray-600">
              {todayCourtEvents.length > 0 ? `${todayCourtEvents.length} ${t('hearings_count')}` : t('no_hearings_today')}
            </div>
          </button>

          <button
            onClick={() => navigate('tasks')}
            className="text-left p-4 rounded-xl border border-gray-200 hover:border-amber-200 hover:shadow-sm transition-all bg-gray-50/50"
          >
            <div className="flex items-center justify-between">
              <div className="text-xs font-bold text-gray-400 uppercase">{t('task')}</div>
              <CheckSquare className="w-4 h-4 text-amber-600" />
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{todayTasksDue.length}</div>
            <div className="mt-1 text-xs text-gray-600">
              {overdueTasks.length > 0 ? `${overdueTasks.length} ${t('overdue')}` : t('no_overdue')}
            </div>
          </button>

          <button
            onClick={() => navigate('time')}
            className="text-left p-4 rounded-xl border border-gray-200 hover:border-emerald-200 hover:shadow-sm transition-all bg-gray-50/50"
          >
            <div className="flex items-center justify-between">
              <div className="text-xs font-bold text-gray-400 uppercase">{t('time')}</div>
              <Clock className="w-4 h-4 text-emerald-600" />
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900">{(todayTimeMinutes / 60).toFixed(1)} h</div>
            <div className="mt-1 text-xs text-gray-600">{t('recorded_today')}</div>
          </button>

          {user?.role === 'Admin' || user?.role === 'Partner' ? (
            <button
              onClick={() => navigate('billing')}
              className="text-left p-4 rounded-xl border border-gray-200 hover:border-red-200 hover:shadow-sm transition-all bg-gray-50/50"
            >
              <div className="flex items-center justify-between">
                <div className="text-xs font-bold text-gray-400 uppercase">{t('collection_risk')}</div>
                <CreditCard className="w-4 h-4 text-red-600" />
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-900">{overdueInvoices.length}</div>
              <div className="mt-1 text-xs text-gray-600">
                {overdueInvoices.length > 0 ? t('overdue_invoice') : t('no_overdue_invoice')}
              </div>
            </button>
          ) : (
            <button
              onClick={() => navigate('documents')}
              className="text-left p-4 rounded-xl border border-gray-200 hover:border-indigo-200 hover:shadow-sm transition-all bg-gray-50/50"
            >
              <div className="flex items-center justify-between">
                <div className="text-xs font-bold text-gray-400 uppercase">{t('document')}</div>
                <Briefcase className="w-4 h-4 text-indigo-600" />
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-900">{todayNewDocs.length}</div>
              <div className="mt-1 text-xs text-gray-600">{t('updated_today')}</div>
            </button>
          )}
        </div>
      </div>

      {/* Main Grid - TASKS FIRST (LARGER), then Chart and other widgets */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">

        {/* Left Column: Tasks (Large) & Activity */}
        <div className="lg:col-span-2 space-y-8">

          {/* TASKS LIST - NOW BIGGER AND FIRST */}
          <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100">
            <div className="flex justify-between items-center mb-6">
              <div className="flex items-center gap-3">
                <h3 className="text-lg font-bold text-slate-800">{t('high_priority_tasks')}</h3>
                {overdueTasks.length > 0 && (
                  <span className="flex items-center gap-1 px-2 py-1 bg-red-100 text-red-700 rounded-full text-xs font-bold animate-pulse">
                    <AlertTriangle className="w-3 h-3" />
                    {overdueTasks.length} Overdue
                  </span>
                )}
              </div>
              <button
                onClick={() => navigate('tasks')}
                className="text-sm font-bold text-primary-600 hover:underline flex items-center gap-1"
              >
                View All <ChevronRight className="w-4 h-4" />
              </button>
            </div>

            <div className="space-y-3">
              {activeTasks.length === 0 && <div className="text-gray-400 text-sm py-4 text-center">No pending tasks. Great job! 🎉</div>}
              {activeTasks.slice(0, 6).map(task => {
                const matter = matters.find(m => m.id === task.matterId);
                const deadlineStatus = getDeadlineStatus(task.dueDate);

                return (
                  <div
                    key={task.id}
                    className={`flex items-center p-4 rounded-xl border transition-all group hover:shadow-md ${deadlineStatus === 'overdue' ? 'border-red-200 bg-red-50/50' :
                        deadlineStatus === 'urgent' ? 'border-amber-200 bg-amber-50/50' :
                          'border-gray-100 hover:bg-gray-50'
                      }`}
                  >
                    {/* Quick Complete Button */}
                    <button
                      onClick={() => handleQuickComplete(task.id)}
                      className="w-8 h-8 rounded-full border-2 border-gray-300 flex items-center justify-center mr-4 hover:border-green-500 hover:bg-green-100 transition-colors group/check"
                      title="Mark as completed"
                    >
                      <Check className="w-4 h-4 text-gray-300 group-hover/check:text-green-600" />
                    </button>

                    {/* Priority Indicator */}
                    <div className={`w-2 h-10 rounded-full mr-4 ${task.priority === 'High' ? 'bg-red-500' :
                        task.priority === 'Medium' ? 'bg-amber-500' : 'bg-blue-500'
                      }`}></div>

                    {/* Task Info */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <p className="text-sm font-bold text-slate-800 truncate">{task.title}</p>
                        {deadlineStatus !== 'normal' && (
                          <span className={`w-5 h-5 rounded-full flex items-center justify-center ${deadlineStatus === 'overdue' ? 'bg-red-500 animate-pulse' : 'bg-amber-500'
                            }`}>
                            <AlertTriangle className="w-3 h-3 text-white" />
                          </span>
                        )}
                      </div>
                      <div className="flex items-center gap-3 mt-1">
                        {matter && <span className="text-xs text-blue-600 font-medium">{matter.name}</span>}
                        <span className={`text-xs ${task.priority === 'High' ? 'text-red-600' :
                            task.priority === 'Medium' ? 'text-amber-600' : 'text-blue-600'
                          } font-bold uppercase`}>{task.priority}</span>
                        <span className="text-xs text-gray-400">{task.status}</span>
                      </div>
                    </div>

                    {/* Dates */}
                    <div className="text-right ml-4 shrink-0">
                      {task.startDate && (
                        <div className="text-[10px] text-gray-400">
                          Started: {formatDate(task.startDate)}
                        </div>
                      )}
                      <div className={`text-xs font-medium ${deadlineStatus === 'overdue' ? 'text-red-600' :
                          deadlineStatus === 'urgent' ? 'text-amber-600' : 'text-gray-600'
                        }`}>
                        Due: {formatDate(task.dueDate || '')}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Financial Chart - Now after tasks */}
          <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100">
            <div className="flex justify-between items-center mb-6">
              <h3 className="text-base font-bold text-slate-800">{t('financial_perf')}</h3>
            </div>
            <div className="h-56 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={financialData}>
                  <defs>
                    <linearGradient id="colorRevenue" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#0ea5e9" stopOpacity={0.2} />
                      <stop offset="95%" stopColor="#0ea5e9" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f1f5f9" />
                  <XAxis dataKey="name" axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 12, fontWeight: 500 }} dy={10} />
                  <YAxis axisLine={false} tickLine={false} tick={{ fill: '#94a3b8', fontSize: 12, fontWeight: 500 }} tickFormatter={(val) => `${val}`} />
                  <Tooltip
                    contentStyle={{ borderRadius: '8px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1)' }}
                    itemStyle={{ color: '#0f172a', fontWeight: 'bold' }}
                    formatter={(value) => formatCurrency(value as number)}
                  />
                  <Area type="monotone" dataKey="revenue" stroke="#0ea5e9" strokeWidth={3} fillOpacity={1} fill="url(#colorRevenue)" />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </div>

          {(user?.role === 'Admin' || user?.role === 'Partner') && (
            <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-base font-bold text-slate-800">{t('reporting_snapshot')}</h3>
                <button onClick={() => navigate('billing')} className="text-xs font-bold text-primary-600 hover:underline">
                  {t('open_billing')} →
                </button>
              </div>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
                  <div className="text-xs font-bold text-gray-400 uppercase">{t('collection_paid')}</div>
                  <div className="mt-2 text-xl font-bold text-slate-900">{formatCurrency(paidInvoicesTotal)}</div>
                  <div className="mt-1 text-xs text-gray-600">{paidInvoices.length} invoice</div>
                </div>
                <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
                  <div className="text-xs font-bold text-gray-400 uppercase">{t('outstanding')}</div>
                  <div className="mt-2 text-xl font-bold text-slate-900">{formatCurrency(outstandingInvoicesTotal)}</div>
                  <div className="mt-1 text-xs text-gray-600">{overdueInvoices.length} overdue</div>
                </div>
                <div className="p-4 rounded-xl border border-gray-200 bg-gray-50/50">
                  <div className="text-xs font-bold text-gray-400 uppercase">{t('wip_unbilled')}</div>
                  <div className="mt-2 text-xl font-bold text-slate-900">{formatCurrency(totalWIP)}</div>
                  <div className="mt-1 text-xs text-gray-600">{t('time_expenses')}</div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Right Column: Calendar & Recent Activity */}
        <div className="space-y-8">

          {/* Calendar Widget */}
          <div className="bg-slate-800 text-white p-6 rounded-xl shadow-elevation relative overflow-hidden">
            <div className="absolute top-0 right-0 -mt-8 -mr-8 w-32 h-32 bg-primary-500 rounded-full opacity-20 blur-3xl"></div>
            <h3 className="text-base font-bold mb-6 flex items-center gap-2 relative z-10">
              <Calendar className="w-4 h-4 text-primary-400" />
              {t('upcoming_events')}
            </h3>
            <div className="space-y-5 relative z-10">
              {upcomingEvents.length === 0 && <div className="text-gray-400 text-sm">No upcoming events.</div>}
              {upcomingEvents.slice(0, 3).map(event => (
                <div key={event.id} className="flex gap-4 items-start">
                  <div className="flex flex-col text-center min-w-[3rem] bg-slate-700/50 rounded-lg p-1.5">
                    <span className="text-[10px] text-gray-400 uppercase font-bold tracking-wider">{new Date(event.date).toLocaleDateString('en-US', { month: 'short' })}</span>
                    <span className="text-xl font-bold text-white leading-none mt-1">{new Date(event.date).getDate()}</span>
                  </div>
                  <div>
                    <p className="text-sm font-bold text-white">{event.title}</p>
                    <p className="text-xs text-gray-400 mt-1">{event.type}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Recent Activity */}
          <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100">
            <h3 className="text-base font-bold text-slate-800 mb-4">{t('recent_activity')}</h3>
            <div className="space-y-4">
              {recentTime.length === 0 && <div className="text-gray-400 text-sm">No recent activity.</div>}
              {recentTime.map(entry => {
                const matter = matters.find(m => m.id === entry.matterId);
                return (
                  <div key={entry.id} className="flex items-center gap-3 text-sm border-b border-gray-50 last:border-0 pb-2 last:pb-0">
                    <div className="w-8 h-8 rounded-full bg-slate-100 flex items-center justify-center text-slate-600 font-bold text-xs shrink-0">
                      {matter?.client?.name?.charAt(0) || '?'}
                    </div>
                    <div className="overflow-hidden flex-1">
                      <p className="font-semibold text-slate-800 truncate text-xs">{matter?.name || 'Unknown Matter'}</p>
                      <p className="text-gray-500 text-xs truncate mt-0.5">{entry.description}</p>
                    </div>
                    <div className="ml-auto text-xs font-mono font-medium text-primary-600 whitespace-nowrap bg-primary-50 px-1.5 py-0.5 rounded">
                      {entry.duration}m
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          {/* Recent Documents */}
          <div className="bg-white p-6 rounded-xl shadow-card border border-gray-100">
            <h3 className="text-base font-bold text-slate-800 mb-4">Recent Documents</h3>
            <div className="space-y-3">
              {recentDocs.length === 0 && <div className="text-gray-400 text-sm">No documents yet.</div>}
              {recentDocs.map(doc => (
                <div key={doc.id} className="flex items-center gap-3 p-3 rounded-lg border border-gray-100 hover:bg-gray-50 transition-colors">
                  <div className="w-9 h-9 rounded-lg bg-primary-50 text-primary-700 flex items-center justify-center text-xs font-bold">
                    {(doc.type || 'DOC').toUpperCase()}
                  </div>
                  <div className="flex-1 overflow-hidden">
                    <div className="text-sm font-semibold text-slate-800 truncate">{doc.name}</div>
                    <div className="text-xs text-gray-500 truncate">
                      Updated: {new Date(doc.updatedAt).toLocaleString()}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
