import React, { useEffect, useState, useMemo } from 'react';
import { Task, TaskStatus, TaskOutcome } from '../types';
import { CheckSquare, Plus, Filter, X, Trash2, Archive, Search, List, LayoutGrid, Clock, AlertTriangle, Check } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { useConfirm } from './ConfirmDialog';
import { toast } from './Toast';

const Tasks: React.FC = () => {
  const { t, formatDate } = useTranslation();
  const { tasks, addTask, updateTaskStatus, updateTask, deleteTask, archiveTask, matters, taskTemplates, createTasksFromTemplate } = useData();
  const { confirm } = useConfirm();

  // UI State
  const [showModal, setShowModal] = useState(false);
  const [showTemplateModal, setShowTemplateModal] = useState(false);
  const [highlightTaskId, setHighlightTaskId] = useState<string | null>(null);
  const [showCompletedPanel, setShowCompletedPanel] = useState(false);
  const [viewMode, setViewMode] = useState<'kanban' | 'list'>('kanban');

  // Filter State
  const [searchQuery, setSearchQuery] = useState('');
  const [filterPriority, setFilterPriority] = useState<string>('');
  const [filterStatus, setFilterStatus] = useState<string>('');

  // Form State
  const [newTaskTitle, setNewTaskTitle] = useState('');
  const [newTaskPriority, setNewTaskPriority] = useState<'High' | 'Medium' | 'Low'>('Medium');
  const [newTaskMatterId, setNewTaskMatterId] = useState('');
  const [newTaskStartDate, setNewTaskStartDate] = useState('');
  const [newTaskDueDate, setNewTaskDueDate] = useState('');
  const [newTaskDescription, setNewTaskDescription] = useState('');

  const COLUMNS: TaskStatus[] = ['To Do', 'In Progress', 'Review', 'Done'];

  // Deep-link from Command Palette
  useEffect(() => {
    const targetId = localStorage.getItem('cmd_target_task');
    if (!targetId) return;
    const exists = tasks.some(t => t.id === targetId);
    if (exists) {
      setHighlightTaskId(targetId);
      setTimeout(() => setHighlightTaskId(null), 4000);
      localStorage.removeItem('cmd_target_task');
    }
  }, [tasks]);

  // Filter and search tasks
  const filteredTasks = useMemo(() => {
    return tasks.filter(task => {
      // Exclude archived and completed tasks from main view
      if (task.status === 'Archived' || task.outcome) return false;

      // Search filter
      if (searchQuery && !task.title.toLowerCase().includes(searchQuery.toLowerCase())) return false;

      // Priority filter
      if (filterPriority && task.priority !== filterPriority) return false;

      // Status filter
      if (filterStatus && task.status !== filterStatus) return false;

      return true;
    });
  }, [tasks, searchQuery, filterPriority, filterStatus]);

  // Completed tasks (with outcome)
  const completedTasks = useMemo(() => {
    return tasks.filter(task => task.outcome && task.status !== 'Archived');
  }, [tasks]);

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

  const handleAddTask = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTaskTitle) return;

    addTask({
      id: `tsk${Date.now()}`,
      title: newTaskTitle,
      description: newTaskDescription || undefined,
      priority: newTaskPriority,
      status: 'To Do',
      startDate: newTaskStartDate ? new Date(newTaskStartDate).toISOString() : undefined,
      dueDate: newTaskDueDate ? new Date(newTaskDueDate).toISOString() : new Date().toISOString(),
      matterId: newTaskMatterId,
      assignedTo: 'ME'
    });
    setShowModal(false);
    resetForm();
  };

  const resetForm = () => {
    setNewTaskTitle('');
    setNewTaskMatterId('');
    setNewTaskStartDate('');
    setNewTaskDueDate('');
    setNewTaskDescription('');
  };

  // Mark task as completed with outcome
  const handleMarkComplete = async (taskId: string, outcome: TaskOutcome) => {
    await updateTask(taskId, {
      outcome,
      status: 'Done',
      isCompleted: true,
      completedAt: new Date().toISOString()
    });
    toast.success(outcome === 'success' ? 'Task completed successfully!' : 'Task marked as failed');
  };

  // Template Modal State
  const [templateId, setTemplateId] = useState('');
  const [templateMatterId, setTemplateMatterId] = useState('');
  const [templateAssignedTo, setTemplateAssignedTo] = useState('');
  const [templateBaseDate, setTemplateBaseDate] = useState(() => new Date().toISOString().slice(0, 10));

  const handleCreateFromTemplate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!templateId) return;
    await createTasksFromTemplate({
      templateId,
      matterId: templateMatterId || undefined,
      assignedTo: templateAssignedTo || undefined,
      baseDate: templateBaseDate ? new Date(templateBaseDate).toISOString() : undefined,
    });
    setShowTemplateModal(false);
    setTemplateId('');
    setTemplateMatterId('');
    setTemplateAssignedTo('');
    setTemplateBaseDate(new Date().toISOString().slice(0, 10));
  };

  const getPriorityColor = (p: string) => {
    if (p === 'High') return 'bg-red-100 text-red-700 border-red-200';
    if (p === 'Medium') return 'bg-amber-100 text-amber-700 border-amber-200';
    return 'bg-blue-100 text-blue-700 border-blue-200';
  };

  const getOutcomeColor = (outcome?: TaskOutcome) => {
    if (outcome === 'success') return 'bg-green-100 text-green-700 border-green-200';
    if (outcome === 'failed') return 'bg-red-100 text-red-700 border-red-200';
    if (outcome === 'cancelled') return 'bg-gray-100 text-gray-600 border-gray-200';
    return 'bg-slate-100 text-slate-600 border-slate-200';
  };

  // Task Card Component
  const TaskCard = ({ task }: { task: Task }) => {
    const matter = matters.find(m => m.id === task.matterId);
    const deadlineStatus = getDeadlineStatus(task.dueDate);
    const [showOutcomeMenu, setShowOutcomeMenu] = useState(false);

    return (
      <div
        className={`bg-white p-4 rounded-lg shadow-sm border border-gray-200 group hover:shadow-md transition-all relative ${highlightTaskId === task.id ? 'ring-2 ring-indigo-400 border-indigo-200' : ''
          } ${deadlineStatus === 'overdue' ? 'border-l-4 border-l-red-500' : deadlineStatus === 'urgent' ? 'border-l-4 border-l-amber-500' : ''}`}
      >
        {/* Deadline Warning */}
        {deadlineStatus !== 'normal' && (
          <div className={`absolute -top-2 -right-2 w-6 h-6 rounded-full flex items-center justify-center ${deadlineStatus === 'overdue' ? 'bg-red-500 animate-pulse' : 'bg-amber-500'
            }`}>
            <AlertTriangle className="w-3 h-3 text-white" />
          </div>
        )}

        <div className="flex justify-between items-start mb-2">
          <span className={`text-[10px] px-2 py-0.5 rounded border font-bold uppercase ${getPriorityColor(task.priority)}`}>
            {task.priority}
          </span>
          {task.assignedTo && (
            <div className="w-6 h-6 rounded-full bg-slate-800 text-white flex items-center justify-center text-[10px] font-bold">
              {task.assignedTo}
            </div>
          )}
        </div>

        <h4 className="text-sm font-bold text-slate-800 leading-snug mb-1">{task.title}</h4>
        {matter && <p className="text-xs text-blue-600 font-medium mb-2 truncate">{matter.name}</p>}

        {/* Dates */}
        <div className="flex flex-wrap gap-2 text-[10px] text-gray-500 mb-2">
          {task.startDate && (
            <span className="flex items-center gap-1">
              <Clock className="w-3 h-3" />
              Start: {formatDate(task.startDate)}
            </span>
          )}
          {task.dueDate && (
            <span className={`flex items-center gap-1 ${deadlineStatus === 'overdue' ? 'text-red-600 font-bold' : deadlineStatus === 'urgent' ? 'text-amber-600 font-bold' : ''}`}>
              Due: {formatDate(task.dueDate)}
            </span>
          )}
        </div>

        <div className="flex items-center justify-between mt-3 pt-3 border-t border-gray-50">
          {/* Completion Button */}
          <div className="relative">
            <button
              onClick={() => setShowOutcomeMenu(!showOutcomeMenu)}
              className="flex items-center gap-1.5 px-2 py-1 rounded-md bg-green-50 text-green-700 hover:bg-green-100 transition-colors text-xs font-medium"
            >
              <Check className="w-3 h-3" />
              Complete
            </button>

            {showOutcomeMenu && (
              <div className="absolute top-full left-0 mt-1 bg-white rounded-lg shadow-lg border border-gray-200 z-20 min-w-[140px]">
                <button
                  onClick={() => { handleMarkComplete(task.id, 'success'); setShowOutcomeMenu(false); }}
                  className="w-full px-3 py-2 text-left text-xs hover:bg-green-50 text-green-700 flex items-center gap-2"
                >
                  ✅ Successful
                </button>
                <button
                  onClick={() => { handleMarkComplete(task.id, 'failed'); setShowOutcomeMenu(false); }}
                  className="w-full px-3 py-2 text-left text-xs hover:bg-red-50 text-red-700 flex items-center gap-2"
                >
                  ❌ Failed
                </button>
                <button
                  onClick={() => { handleMarkComplete(task.id, 'cancelled'); setShowOutcomeMenu(false); }}
                  className="w-full px-3 py-2 text-left text-xs hover:bg-gray-50 text-gray-600 flex items-center gap-2"
                >
                  ⛔ Cancelled
                </button>
              </div>
            )}
          </div>

          {/* Task Actions */}
          <div className="flex gap-1 items-center">
            {task.status !== 'To Do' && (
              <button onClick={() => updateTaskStatus(task.id, COLUMNS[COLUMNS.indexOf(task.status as any) - 1])} className="text-gray-400 hover:text-slate-800 text-xs px-1" title="Move Back">←</button>
            )}
            {task.status !== 'Done' && (
              <button onClick={() => updateTaskStatus(task.id, COLUMNS[COLUMNS.indexOf(task.status as any) + 1])} className="text-gray-400 hover:text-slate-800 text-xs px-1" title="Move Forward">→</button>
            )}
            <button
              onClick={async () => {
                const ok = await confirm({
                  title: t('delete_task'),
                  message: t('confirm_delete'),
                  confirmText: t('delete_task'),
                  cancelText: t('cancel'),
                  variant: 'danger'
                });
                if (ok) {
                  deleteTask(task.id);
                  toast.success(t('task_deleted'));
                }
              }}
              className="text-gray-400 hover:text-red-600 text-xs p-1 rounded hover:bg-red-50"
              title={t('delete_task')}
            >
              <Trash2 className="w-3 h-3" />
            </button>
          </div>
        </div>
      </div>
    );
  };

  return (
    <div className="h-full flex flex-col bg-gray-50/50 overflow-hidden">
      {/* Header */}
      <div className="px-6 py-4 bg-white border-b border-gray-200 shrink-0">
        <div className="flex justify-between items-center mb-4">
          <div>
            <h1 className="text-2xl font-bold text-slate-900">{t('tasks_title')}</h1>
            <p className="text-sm text-gray-500 mt-1">{t('tasks_subtitle')}</p>
          </div>
          <div className="flex items-center gap-3">
            {/* View Toggle */}
            <div className="flex bg-gray-100 rounded-lg p-1">
              <button
                onClick={() => setViewMode('kanban')}
                className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors flex items-center gap-1.5 ${viewMode === 'kanban' ? 'bg-white shadow-sm text-slate-800' : 'text-gray-500 hover:text-slate-800'
                  }`}
              >
                <LayoutGrid className="w-3.5 h-3.5" />
                Kanban
              </button>
              <button
                onClick={() => setViewMode('list')}
                className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors flex items-center gap-1.5 ${viewMode === 'list' ? 'bg-white shadow-sm text-slate-800' : 'text-gray-500 hover:text-slate-800'
                  }`}
              >
                <List className="w-3.5 h-3.5" />
                List
              </button>
            </div>

            <button
              onClick={() => setShowCompletedPanel(!showCompletedPanel)}
              className={`px-4 py-2.5 rounded-lg text-sm font-medium transition-colors flex items-center gap-2 ${showCompletedPanel ? 'bg-green-600 text-white' : 'bg-white border border-gray-200 text-slate-700 hover:bg-gray-50'
                }`}
            >
              <CheckSquare className="w-4 h-4" />
              Completed
              {completedTasks.length > 0 && (
                <span className={`text-xs px-1.5 py-0.5 rounded-full ${showCompletedPanel ? 'bg-white/20' : 'bg-green-100 text-green-700'}`}>
                  {completedTasks.length}
                </span>
              )}
            </button>

            <button
              onClick={() => setShowTemplateModal(true)}
              className="bg-white border border-gray-200 text-slate-700 px-5 py-2.5 rounded-lg shadow-sm hover:bg-gray-50 transition-colors text-sm font-medium"
            >
              Template
            </button>
            <button
              onClick={() => setShowModal(true)}
              className="bg-slate-800 text-white px-5 py-2.5 rounded-lg shadow hover:bg-slate-700 transition-colors text-sm font-medium flex items-center gap-2"
            >
              <Plus className="w-4 h-4" />
              {t('add_task')}
            </button>
          </div>
        </div>

        {/* Search and Filters */}
        <div className="flex gap-4 items-center">
          <div className="relative flex-1 max-w-md">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
            <input
              type="text"
              placeholder="Search tasks..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full pl-10 pr-4 py-2 rounded-lg border border-gray-200 bg-white text-sm focus:ring-2 focus:ring-slate-500 outline-none"
            />
          </div>

          <select
            value={filterPriority}
            onChange={(e) => setFilterPriority(e.target.value)}
            className="px-3 py-2 rounded-lg border border-gray-200 bg-white text-sm focus:ring-2 focus:ring-slate-500 outline-none"
          >
            <option value="">All Priorities</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>

          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="px-3 py-2 rounded-lg border border-gray-200 bg-white text-sm focus:ring-2 focus:ring-slate-500 outline-none"
          >
            <option value="">All Status</option>
            <option value="To Do">To Do</option>
            <option value="In Progress">In Progress</option>
            <option value="Review">Review</option>
            <option value="Done">Done</option>
          </select>

          {(searchQuery || filterPriority || filterStatus) && (
            <button
              onClick={() => { setSearchQuery(''); setFilterPriority(''); setFilterStatus(''); }}
              className="text-gray-500 hover:text-slate-800 text-sm flex items-center gap-1"
            >
              <X className="w-4 h-4" />
              Clear
            </button>
          )}
        </div>
      </div>

      {/* Main Content */}
      <div className="flex-1 overflow-x-auto p-6">
        {viewMode === 'kanban' ? (
          /* Kanban Board */
          <div className="flex gap-6 h-full min-w-[1000px]">
            {COLUMNS.map(column => {
              const colTasks = filteredTasks.filter(t => t.status === column);
              return (
                <div key={column} className="flex-1 flex flex-col min-w-[280px] bg-gray-100/50 rounded-xl border border-gray-200/50 h-full max-h-full">
                  <div className="p-4 border-b border-gray-200/50 flex justify-between items-center bg-gray-50/50 rounded-t-xl">
                    <h3 className="font-bold text-slate-700 text-sm flex items-center gap-2">
                      <span className={`w-2 h-2 rounded-full ${column === 'Done' ? 'bg-green-500' : column === 'To Do' ? 'bg-slate-400' : 'bg-blue-500'}`}></span>
                      {column}
                    </h3>
                    <span className="text-xs font-bold bg-white px-2 py-1 rounded text-gray-500 border border-gray-100">{colTasks.length}</span>
                  </div>

                  <div className="p-3 space-y-3 overflow-y-auto flex-1">
                    {colTasks.map(task => <TaskCard key={task.id} task={task} />)}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          /* List View */
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="divide-y divide-gray-100">
              {filteredTasks.length === 0 ? (
                <div className="p-8 text-center text-gray-400">
                  <CheckSquare className="w-12 h-12 mx-auto opacity-20 mb-2" />
                  <p>No tasks found</p>
                </div>
              ) : (
                filteredTasks.map(task => {
                  const matter = matters.find(m => m.id === task.matterId);
                  const deadlineStatus = getDeadlineStatus(task.dueDate);

                  return (
                    <div key={task.id} className={`px-6 py-4 flex items-center gap-4 hover:bg-gray-50 ${deadlineStatus === 'overdue' ? 'bg-red-50/50' : deadlineStatus === 'urgent' ? 'bg-amber-50/50' : ''
                      }`}>
                      {/* Status indicator */}
                      <div className={`w-3 h-3 rounded-full shrink-0 ${task.status === 'Done' ? 'bg-green-500' :
                          task.status === 'In Progress' ? 'bg-blue-500' :
                            task.status === 'Review' ? 'bg-purple-500' : 'bg-gray-300'
                        }`} />

                      {/* Priority */}
                      <span className={`text-[10px] px-2 py-0.5 rounded border font-bold uppercase shrink-0 ${getPriorityColor(task.priority)}`}>
                        {task.priority}
                      </span>

                      {/* Title and Matter */}
                      <div className="flex-1 min-w-0">
                        <h4 className="font-semibold text-slate-800 truncate">{task.title}</h4>
                        {matter && <p className="text-xs text-blue-600 truncate">{matter.name}</p>}
                      </div>

                      {/* Dates */}
                      <div className="text-xs text-gray-500 shrink-0">
                        {task.startDate && <div>Start: {formatDate(task.startDate)}</div>}
                        <div className={deadlineStatus !== 'normal' ? 'text-red-600 font-bold' : ''}>
                          Due: {formatDate(task.dueDate || '')}
                        </div>
                      </div>

                      {/* Deadline Warning */}
                      {deadlineStatus !== 'normal' && (
                        <div className={`w-6 h-6 rounded-full flex items-center justify-center shrink-0 ${deadlineStatus === 'overdue' ? 'bg-red-500' : 'bg-amber-500'
                          }`}>
                          <AlertTriangle className="w-3 h-3 text-white" />
                        </div>
                      )}

                      {/* Actions */}
                      <div className="flex items-center gap-2 shrink-0">
                        <button
                          onClick={() => handleMarkComplete(task.id, 'success')}
                          className="px-2 py-1 rounded bg-green-100 text-green-700 hover:bg-green-200 text-xs font-medium"
                        >
                          ✓ Done
                        </button>
                        <button
                          onClick={async () => {
                            const ok = await confirm({
                              title: t('delete_task'),
                              message: t('confirm_delete'),
                              confirmText: t('delete_task'),
                              cancelText: t('cancel'),
                              variant: 'danger'
                            });
                            if (ok) deleteTask(task.id);
                          }}
                          className="text-gray-400 hover:text-red-600 p-1"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}
      </div>

      {/* Completed Tasks Panel */}
      {showCompletedPanel && (
        <div className="px-6 pb-6">
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="px-6 py-4 border-b border-gray-200 bg-gradient-to-r from-green-50 to-emerald-50 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CheckSquare className="w-5 h-5 text-green-600" />
                <h3 className="font-bold text-slate-800">Completed Tasks</h3>
                <span className="text-xs bg-green-100 text-green-700 px-2 py-0.5 rounded-full font-bold">
                  {completedTasks.length}
                </span>
              </div>
              <div className="flex gap-4 text-xs">
                <span className="flex items-center gap-1 text-green-600">
                  ✅ Success: {completedTasks.filter(t => t.outcome === 'success').length}
                </span>
                <span className="flex items-center gap-1 text-red-600">
                  ❌ Failed: {completedTasks.filter(t => t.outcome === 'failed').length}
                </span>
                <span className="flex items-center gap-1 text-gray-500">
                  ⛔ Cancelled: {completedTasks.filter(t => t.outcome === 'cancelled').length}
                </span>
              </div>
            </div>
            <div className="divide-y divide-gray-100 max-h-[300px] overflow-y-auto">
              {completedTasks.length === 0 ? (
                <div className="p-8 text-center text-gray-400">
                  <CheckSquare className="w-12 h-12 mx-auto opacity-20 mb-2" />
                  <p>No completed tasks yet</p>
                </div>
              ) : (
                completedTasks.map(task => {
                  const matter = matters.find(m => m.id === task.matterId);
                  return (
                    <div key={task.id} className="px-6 py-4 flex items-center justify-between hover:bg-gray-50">
                      <div className="flex items-center gap-3 flex-1">
                        <span className={`text-[10px] px-2 py-0.5 rounded border font-bold uppercase ${getOutcomeColor(task.outcome)}`}>
                          {task.outcome}
                        </span>
                        <div>
                          <h4 className="font-medium text-slate-800">{task.title}</h4>
                          {matter && <span className="text-xs text-blue-600">{matter.name}</span>}
                        </div>
                      </div>
                      <div className="flex items-center gap-3">
                        <span className="text-xs text-gray-400">
                          Completed: {formatDate(task.completedAt || task.updatedAt || '')}
                        </span>
                        <button
                          onClick={() => updateTask(task.id, { outcome: undefined, status: 'To Do', isCompleted: false })}
                          className="text-xs text-blue-600 hover:text-blue-800 font-medium"
                        >
                          Restore
                        </button>
                        <button
                          onClick={() => archiveTask(task.id)}
                          className="text-gray-400 hover:text-green-600 p-1"
                          title="Archive"
                        >
                          <Archive className="w-4 h-4" />
                        </button>
                        <button
                          onClick={async () => {
                            const ok = await confirm({
                              title: t('delete_task'),
                              message: t('confirm_delete'),
                              confirmText: t('delete_task'),
                              cancelText: t('cancel'),
                              variant: 'danger'
                            });
                            if (ok) deleteTask(task.id);
                          }}
                          className="text-gray-400 hover:text-red-600"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}

      {/* Add Task Modal */}
      {showModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl p-6 w-[480px] max-h-[90vh] overflow-y-auto">
            <h3 className="font-bold text-lg mb-4 text-slate-800">{t('add_task')}</h3>
            <form onSubmit={handleAddTask} className="space-y-4">
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('title')} *</label>
                <input required className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none" placeholder="Enter task title..." value={newTaskTitle} onChange={e => setNewTaskTitle(e.target.value)} />
              </div>
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Description</label>
                <textarea
                  className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none"
                  placeholder="Optional details..."
                  value={newTaskDescription}
                  onChange={e => setNewTaskDescription(e.target.value)}
                  rows={3}
                />
              </div>
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('nav_matters')}</label>
                <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none" value={newTaskMatterId} onChange={e => setNewTaskMatterId(e.target.value)}>
                  <option value="">-- No Matter --</option>
                  {matters.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Priority</label>
                  <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none" value={newTaskPriority} onChange={e => setNewTaskPriority(e.target.value as any)}>
                    <option>High</option>
                    <option>Medium</option>
                    <option>Low</option>
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Start Date</label>
                  <input type="date" className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none" value={newTaskStartDate} onChange={e => setNewTaskStartDate(e.target.value)} />
                </div>
              </div>
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Due Date</label>
                <input type="date" className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none" value={newTaskDueDate} onChange={e => setNewTaskDueDate(e.target.value)} />
              </div>
              <div className="flex justify-end gap-2 mt-6">
                <button type="button" onClick={() => setShowModal(false)} className="px-4 py-2.5 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg">{t('cancel')}</button>
                <button type="submit" className="px-4 py-2.5 bg-slate-800 text-white font-bold rounded-lg text-sm">{t('save')}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Template Modal */}
      {showTemplateModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg">
            <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center">
              <div>
                <h3 className="font-bold text-lg text-slate-800">Create Tasks from Template</h3>
                <p className="text-sm text-gray-500 mt-1">Select a template and create tasks in one step.</p>
              </div>
              <button onClick={() => setShowTemplateModal(false)} className="text-gray-400 hover:text-gray-600">
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleCreateFromTemplate} className="p-6 space-y-4">
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Template</label>
                <select
                  value={templateId}
                  onChange={e => setTemplateId(e.target.value)}
                  className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none"
                  required
                >
                  <option value="">-- Select template --</option>
                  {taskTemplates.map(tp => (
                    <option key={tp.id} value={tp.id}>
                      {tp.category ? `[${tp.category}] ` : ''}{tp.name}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('nav_matters')}</label>
                <select
                  value={templateMatterId}
                  onChange={e => setTemplateMatterId(e.target.value)}
                  className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none"
                >
                  <option value="">-- No Matter --</option>
                  {matters.map(m => <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>)}
                </select>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Start Date</label>
                  <input
                    type="date"
                    value={templateBaseDate}
                    onChange={e => setTemplateBaseDate(e.target.value)}
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Assignee (Initials)</label>
                  <input
                    value={templateAssignedTo}
                    onChange={e => setTemplateAssignedTo(e.target.value)}
                    placeholder="MR / JP / ..."
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 text-sm focus:ring-2 focus:ring-slate-500 outline-none"
                  />
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button type="button" onClick={() => setShowTemplateModal(false)} className="px-4 py-2.5 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg">
                  {t('cancel')}
                </button>
                <button type="submit" className="px-4 py-2.5 bg-slate-800 text-white font-bold rounded-lg text-sm">
                  Create
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default Tasks;