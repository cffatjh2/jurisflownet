import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { Matter, Client, TimeEntry, Message, Expense, CalendarEvent, DocumentFile, Invoice, Lead, Task, TaskStatus, Notification as AppNotification, TaskTemplate, ActiveTimer } from '../types';
import { api } from '../services/api';
import { useAuth } from './AuthContext';

interface DataContextType {
  matters: Matter[];
  clients: Client[];
  timeEntries: TimeEntry[];
  expenses: Expense[];
  messages: Message[];
  events: CalendarEvent[];
  documents: DocumentFile[];
  invoices: Invoice[];
  leads: Lead[];
  tasks: Task[];
  taskTemplates: TaskTemplate[];
  notifications: AppNotification[];

  // Timer Actions
  activeTimer: ActiveTimer | null;
  startTimer: (matterId: string | undefined, description: string, rate?: number, activityCode?: string, isBillable?: boolean) => void;
  stopTimer: () => Promise<boolean>;
  updateTimer: (elapsed: number) => void;
  pauseTimer: () => void;
  resumeTimer: () => void;

  addMatter: (item: any) => Promise<void>;
  updateMatter: (id: string, data: Partial<Matter>) => Promise<void>;
  deleteMatter: (id: string) => Promise<void>;
  addTimeEntry: (item: any) => Promise<boolean>;
  addExpense: (item: any) => Promise<boolean>;
  approveTimeEntry: (id: string) => Promise<boolean>;
  rejectTimeEntry: (id: string, reason?: string) => Promise<boolean>;
  approveExpense: (id: string) => Promise<boolean>;
  rejectExpense: (id: string, reason?: string) => Promise<boolean>;
  addMessage: (item: Message) => void;
  markMessageRead: (id: string) => void;
  addEvent: (item: any) => Promise<void>;
  updateEvent: (id: string, data: Partial<CalendarEvent>) => Promise<void>;
  deleteEvent: (id: string) => Promise<void>;
  addDocument: (item: DocumentFile) => void;
  updateDocument: (id: string, data: Partial<DocumentFile>) => void;
  deleteDocument: (id: string) => void;
  addInvoice: (item: any) => Promise<void>;
  updateInvoice: (id: string, data: any) => void;
  deleteInvoice: (id: string) => void;
  addClient: (item: any) => Promise<Client>;
  updateClient: (id: string, data: Partial<Client> & { statusChangeNote?: string }) => Promise<void>;
  addLead: (item: any) => Promise<void>;
  updateLead: (id: string, data: Partial<Lead>) => Promise<void>;
  deleteLead: (id: string) => Promise<void>;
  addTask: (item: any) => Promise<void>;
  updateTaskStatus: (id: string, status: TaskStatus) => Promise<void>;
  updateTask: (id: string, data: Partial<Task>) => Promise<void>;
  deleteTask: (id: string) => Promise<void>;
  archiveTask: (id: string) => Promise<void>;
  createTasksFromTemplate: (data: { templateId: string; matterId?: string; assignedTo?: string; baseDate?: string }) => Promise<void>;
  markAsBilled: (matterId: string) => Promise<void>;
  markNotificationRead: (id: string) => Promise<void>;
  markNotificationUnread: (id: string) => Promise<void>;
  markAllNotificationsRead: () => Promise<void>;
  updateUserProfile: (data: any) => Promise<void>;
  bulkAssignDocuments: (ids: string[], matterId?: string | null) => Promise<void>;
}

const DataContext = createContext<DataContextType | undefined>(undefined);

export const DataProvider = ({ children }: { children: ReactNode }) => {
  const { isAuthenticated, user } = useAuth();

  // --- STATE ---
  const [activeTimer, setActiveTimer] = useState<ActiveTimer | null>(() => {
    // Load from local storage on init
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem('jf_active_timer');
      return saved ? JSON.parse(saved) : null;
    }
    return null;
  });

  const [matters, setMatters] = useState<Matter[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [timeEntries, setTimeEntries] = useState<TimeEntry[]>([]);
  const [tasks, setTasks] = useState<Task[]>([]);
  const [taskTemplates, setTaskTemplates] = useState<TaskTemplate[]>([]);
  const [events, setEvents] = useState<CalendarEvent[]>([]);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [leads, setLeads] = useState<Lead[]>([]);
  const [notifications, setNotifications] = useState<AppNotification[]>([]);

  // Local-only state
  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [messages, setMessages] = useState<Message[]>([
    { id: 'msg1', from: 'Jessica Pearson', subject: 'Managing Partner Meeting', preview: 'We need to discuss the new associates...', date: '09:00 AM', read: false }
  ]);
  const [documents, setDocuments] = useState<DocumentFile[]>([]);

  // Timer Persistence
  useEffect(() => {
    if (activeTimer) {
      localStorage.setItem('jf_active_timer', JSON.stringify(activeTimer));
    } else {
      localStorage.removeItem('jf_active_timer');
    }
  }, [activeTimer]);

  const startTimer = (matterId: string | undefined, description: string, rate?: number, activityCode?: string, isBillable?: boolean) => {
    const newTimer: ActiveTimer = {
      startTime: Date.now(),
      matterId,
      description,
      isRunning: true,
      elapsed: 0,
      rate,
      activityCode,
      isBillable
    };
    setActiveTimer(newTimer);
  };

  const pauseTimer = () => {
    if (activeTimer && activeTimer.isRunning) {
      const now = Date.now();
      const additional = now - activeTimer.startTime;
      setActiveTimer({
        ...activeTimer,
        isRunning: false,
        elapsed: activeTimer.elapsed + additional
      });
    }
  };

  const resumeTimer = () => {
    if (activeTimer && !activeTimer.isRunning) {
      setActiveTimer({
        ...activeTimer,
        isRunning: true,
        startTime: Date.now()
      });
    }
  };

  const updateTimer = (elapsed: number) => {
    // This is mostly for UI sync if needed, but the real truth is calculated
  };

  const stopTimer = async (): Promise<boolean> => {
    if (!activeTimer) return false;

    // Calculate total duration
    let totalDurationMs = activeTimer.elapsed;
    if (activeTimer.isRunning) {
      totalDurationMs += (Date.now() - activeTimer.startTime);
    }

    // Convert to minutes (minimum 1 minute?)
    const minutes = Math.ceil(totalDurationMs / 1000 / 60);

    // Create Time Entry
    const saved = await addTimeEntry({
      matterId: activeTimer.matterId,
      description: activeTimer.description,
      duration: minutes,
      date: new Date().toISOString(),
      rate: activeTimer.rate || 0, // Use stored rate or default to 0
      billed: false,
      type: 'time',
      activityCode: activeTimer.activityCode,
      isBillable: activeTimer.isBillable ?? true
    });

    if (saved) {
      setActiveTimer(null);
    }
    return saved;
  };

  const parseDocTags = (raw: any): string[] | undefined => {
    if (!raw) return undefined;
    if (Array.isArray(raw)) return raw.map(String);
    if (typeof raw === 'string') {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) return parsed.map(String);
      } catch {
        // allow comma-separated
        return raw.split(',').map(s => s.trim()).filter(Boolean);
      }
    }
    return undefined;
  };

  const normalizeDocument = (d: any): DocumentFile => {
    const mime = (d.mimeType || '').toLowerCase();
    const type: DocumentFile['type'] =
      mime.includes('pdf') ? 'pdf' :
        mime.includes('word') ? 'docx' :
          mime.includes('text') ? 'txt' :
            mime.includes('image') ? 'img' : 'txt';

    const sizeStr = typeof d.fileSize === 'number'
      ? `${(d.fileSize / 1024 / 1024).toFixed(2)} MB`
      : undefined;

    return {
      id: d.id,
      name: d.name,
      type,
      size: sizeStr,
      fileSize: d.fileSize,
      updatedAt: d.updatedAt || d.createdAt || new Date().toISOString(),
      matterId: d.matterId || undefined,
      filePath: d.filePath || undefined,
      description: d.description || undefined,
      tags: parseDocTags(d.tags),
      category: d.category || undefined,
      status: d.status || undefined,
      version: d.version || undefined,
      legalHoldReason: d.legalHoldReason || undefined,
      legalHoldPlacedAt: d.legalHoldPlacedAt || undefined,
      legalHoldReleasedAt: d.legalHoldReleasedAt || undefined,
      legalHoldPlacedBy: d.legalHoldPlacedBy || undefined,
    };
  };

  const clearLoadedData = () => {
    setMatters([]);
    setClients([]);
    setTasks([]);
    setTimeEntries([]);
    setExpenses([]);
    setEvents([]);
    setLeads([]);
    setInvoices([]);
    setNotifications([]);
    setDocuments([]);
    setTaskTemplates([]);
  };

  const applyBootstrapPayload = (payload: any) => {
    if (!payload || typeof payload !== 'object') return;
    if (Array.isArray(payload.matters)) setMatters(payload.matters);
    if (Array.isArray(payload.tasks)) setTasks(payload.tasks);
    if (Array.isArray(payload.timeEntries)) setTimeEntries(payload.timeEntries);
    if (Array.isArray(payload.expenses)) setExpenses(payload.expenses);
    if (Array.isArray(payload.clients)) setClients(payload.clients);
    if (Array.isArray(payload.leads)) setLeads(payload.leads);
    if (Array.isArray(payload.events)) setEvents(payload.events);
    if (Array.isArray(payload.invoices)) setInvoices(payload.invoices);
    if (Array.isArray(payload.notifications)) setNotifications(payload.notifications);
    if (Array.isArray(payload.documents)) setDocuments(payload.documents.map(normalizeDocument));
    if (Array.isArray(payload.taskTemplates)) setTaskTemplates(payload.taskTemplates);
  };

  // --- INITIAL LOAD ---
  useEffect(() => {
    let disposed = false;

    const loadInitialFallback = async () => {
      const [m, t, te, e, n] = await Promise.all([
        api.getMatters().catch(() => null),
        api.getTasks().catch(() => null),
        api.getTimeEntries().catch(() => null),
        api.getEvents().catch(() => null),
        api.getNotifications(user?.id).catch(() => [])
      ]);

      if (disposed) return;

      if (Array.isArray(m)) setMatters(m);
      if (Array.isArray(t)) setTasks(t);
      if (Array.isArray(te)) setTimeEntries(te);
      if (Array.isArray(e)) setEvents(e);
      if (Array.isArray(n)) setNotifications(n);
      console.log('Initial data loaded via endpoint fallback');
    };

    const loadDeferredFallback = async () => {
      const [ex, c, l, i, docs, templates] = await Promise.all([
        api.getExpenses().catch(() => null),
        api.getClients().catch(() => null),
        api.getLeads().catch(() => null),
        api.getInvoices().catch(() => null),
        api.getDocuments().catch(() => []),
        api.getTaskTemplates().catch(() => [])
      ]);

      if (disposed) return;

      if (Array.isArray(ex)) setExpenses(ex);
      if (Array.isArray(c)) setClients(c);
      if (Array.isArray(l)) setLeads(l);
      if (Array.isArray(i)) setInvoices(i);
      if (Array.isArray(docs)) setDocuments(docs.map(normalizeDocument));
      if (Array.isArray(templates)) setTaskTemplates(templates);
      console.log('Deferred data loaded via endpoint fallback');
    };

    const loadData = async () => {
      const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
      try {
        if (!isAuthenticated || !token) {
          clearLoadedData();
          return;
        }

        console.log('Fetching initial data from API...');

        let initialLoaded = false;
        try {
          const bootstrap = await api.bootstrap('initial');
          if (!disposed && bootstrap && typeof bootstrap === 'object') {
            applyBootstrapPayload(bootstrap);
            initialLoaded = true;
            console.log('Initial data loaded via bootstrap endpoint');
          }
        } catch (bootstrapError) {
          console.warn('Initial bootstrap unavailable, falling back to endpoint calls', bootstrapError);
        }

        if (!initialLoaded) {
          await loadInitialFallback();
        }

        if (disposed) return;

        const loadDeferred = async () => {
          let deferredLoaded = false;
          try {
            const bootstrap = await api.bootstrap('deferred');
            if (!disposed && bootstrap && typeof bootstrap === 'object') {
              applyBootstrapPayload(bootstrap);
              deferredLoaded = true;
              console.log('Deferred data loaded via bootstrap endpoint');
            }
          } catch (bootstrapError) {
            console.warn('Deferred bootstrap unavailable, falling back to endpoint calls', bootstrapError);
          }

          if (!deferredLoaded) {
            await loadDeferredFallback();
          }
        };

        void loadDeferred();
      } catch (error) {
        console.warn('Failed to load data from backend.', error);
        if ((!isAuthenticated || !token) && !disposed) {
          clearLoadedData();
        }
      }
    };

    loadData();
    return () => {
      disposed = true;
    };
  }, [isAuthenticated, user?.id]);

  // --- NOTIFICATION POLLING ---
  useEffect(() => {
    if (!isAuthenticated) return;

    const fetchNotifications = async () => {
      try {
        const notifs = await api.getNotifications(user?.id);
        if (notifs) setNotifications(notifs);
      } catch (e) {
        // console.error("Notification Poll Error", e); // Silent fail
      }
    };

    // Initial fetch
    fetchNotifications();

    // Poll every 60 seconds
    const interval = setInterval(fetchNotifications, 60000);
    return () => clearInterval(interval);
  }, [isAuthenticated, user?.id]);

  // --- EVENT REMINDER SYSTEM ---
  useEffect(() => {
    if (!isAuthenticated) return;

    // Request browser notification permission
    if ('Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().then(permission => {
        console.log('Notification permission:', permission);
      });
    }

    const checkEventReminders = () => {
      const now = Date.now();

      events.forEach(event => {
        if (!event.reminderMinutes || event.reminderMinutes === 0) return;
        if (event.reminderSent) return;

        const eventTime = new Date(event.date).getTime();
        const reminderTime = eventTime - (event.reminderMinutes * 60 * 1000);

        // Check if we're within 1 minute of the reminder time
        if (now >= reminderTime && now < reminderTime + 60000) {
          // Create notification
          const minutesUntil = Math.round((eventTime - now) / 60000);
          const timeStr = minutesUntil > 60
            ? `${Math.round(minutesUntil / 60)} hour(s)`
            : `${minutesUntil} minute(s)`;

          const newNotification: AppNotification = {
            id: `notif-event-${event.id}-${Date.now()}`,
            userId: 'current',
            title: `Upcoming Event`,
            message: `"${event.title}" starts in ${timeStr}!`,
            type: 'warning',
            read: false,
            link: 'tab:calendar',
            createdAt: new Date().toISOString()
          };

          setNotifications(prev => [newNotification, ...prev]);

          // Mark reminder as sent in local state
          setEvents(prev => prev.map(e =>
            e.id === event.id ? { ...e, reminderSent: true } : e
          ));

          // Show browser notification if permission granted
          if ('Notification' in window && Notification.permission === 'granted') {
            new window.Notification(`${event.title}`, {
              body: `Event starts in ${timeStr}!`,
              icon: '/icons/icon-192.png',
              tag: `event-${event.id}`
            });
          }

          // Also play a sound if available
          try {
            const audio = new Audio('/notification.mp3');
            audio.volume = 0.5;
            audio.play().catch(() => { }); // Ignore errors if audio can't play
          } catch (e) {
            // Audio not available
          }
        }
      });
    };

    // Check immediately and then every 30 seconds
    checkEventReminders();
    const interval = setInterval(checkEventReminders, 30000);

    return () => clearInterval(interval);
  }, [events, isAuthenticated]);

  // --- ACTIONS (Optimistic Updates) ---

  const addMatter = async (matterData: any) => {
    // Optimistic
    const tempId = `m-temp-${Date.now()}`;
    const optimisticClient = matterData.client || {
      id: matterData.clientId || `c-temp-${Date.now()}`,
      name: matterData.clientName || 'Client',
      email: matterData.clientEmail || '',
      phone: matterData.clientPhone || '',
      type: 'Individual',
      status: 'Active'
    };
    const optimisticMatter = { ...matterData, id: tempId, client: optimisticClient };
    setMatters(prev => [optimisticMatter, ...prev]);

    try {
      const payload = { ...matterData };
      delete payload.client;
      if (!payload.clientId) {
        throw new Error('Client is required to create a matter.');
      }
      const newMatter = await api.createMatter(payload);
      // Replace temp with real
      const hydratedMatter = { ...newMatter, client: newMatter.client || optimisticClient };
      setMatters(prev => [hydratedMatter, ...prev.filter(m => m.id !== tempId)]);
      const freshClients = await api.getClients();
      setClients(freshClients);
    } catch (e) {
      console.error("API Error (addMatter) - operating offline", e);
      setMatters(prev => prev.filter(m => m.id !== tempId));
      throw e;
    }
  };

  const updateMatter = async (id: string, data: Partial<Matter>) => {
    // Optimistic update
    setMatters(prev => prev.map(m => m.id === id ? { ...m, ...data, client: data.client || m.client } : m));
    try {
      const updated = await api.updateMatter(id, data);
      setMatters(prev => prev.map(m => m.id === id ? { ...m, ...updated } : m));
    } catch (e) {
      console.error("API Error (updateMatter)", e);
    }
  };

  const deleteMatter = async (id: string) => {
    const prev = matters;
    setMatters(prev => prev.filter(m => m.id !== id));
    try {
      await api.deleteMatter(id);
    } catch (e) {
      console.error("API Error (deleteMatter)", e);
      setMatters(prev); // revert
    }
  };

  const addTask = async (taskData: any) => {
    const tempId = `t-${Date.now()}`;
    const tempTask = { ...taskData, id: tempId, status: taskData.status || 'To Do', priority: taskData.priority || 'Medium' };

    console.log('[addTask] Creating task with temp ID:', tempId);
    setTasks(prev => [...prev, tempTask]);

    try {
      const newTask = await api.createTask(taskData);
      if (newTask && newTask.id) {
        console.log('[addTask] Task created successfully with real ID:', newTask.id);
        // Replace temp task with real one from server
        setTasks(prev => prev.map(t => t.id === tempId ? { ...t, ...newTask, id: newTask.id } : t));
      } else {
        console.warn('[addTask] API returned null, removing temp task');
        setTasks(prev => prev.filter(t => t.id !== tempId));
      }
    } catch (e) {
      console.error('[addTask] API Error - removing temp task:', e);
      // Remove temp task on error
      setTasks(prev => prev.filter(t => t.id !== tempId));
    }
  };

  const updateTaskStatus = async (id: string, status: TaskStatus) => {
    setTasks(prev => prev.map(t => t.id === id ? { ...t, status } : t));
    try { await api.updateTaskStatus(id, status); } catch (e) { console.error("API Error", e); }
  };

  const updateTask = async (id: string, data: Partial<Task>) => {
    setTasks(prev => prev.map(t => t.id === id ? { ...t, ...data } : t));
    try {
      const updated = await api.updateTask(id, data);
      setTasks(prev => prev.map(t => t.id === id ? { ...t, ...updated } : t));
    } catch (e) {
      console.error("API Error (updateTask)", e);
    }
  };

  const createTasksFromTemplate = async (data: { templateId: string; matterId?: string; assignedTo?: string; baseDate?: string }) => {
    try {
      const res = await api.createTasksFromTemplate(data);
      if (res?.tasks) {
        setTasks(prev => [...res.tasks, ...prev]);
      }
    } catch (e) {
      console.error("API Error (createTasksFromTemplate)", e);
    }
  };

  const deleteTask = async (id: string) => {
    console.log(`[deleteTask] Starting deletion of task: ${id}`);

    // Check if this is a temp task (never saved to server)
    const isTempTask = id.startsWith('t-');

    // Store previous state for potential rollback
    const prevTasks = [...tasks];

    // Optimistically remove from UI immediately
    setTasks(prev => {
      const filtered = prev.filter(t => t.id !== id);
      console.log(`[deleteTask] Optimistically removed. Prev count: ${prev.length}, New count: ${filtered.length}`);
      return filtered;
    });

    // If it's a temp task that was never saved, we're done
    if (isTempTask) {
      console.log('[deleteTask] Temp task removed (never persisted)');
      return;
    }

    try {
      // Call API to delete
      const result = await api.deleteTask(id);
      // Success! API returned (null for 204 is fine)
      console.log(`[deleteTask] API call successful. Result:`, result);
      // DO NOT revert - deletion was successful
    } catch (e: any) {
      // Only revert if there was an actual error
      console.error('[deleteTask] API Error - reverting:', e);
      console.error('[deleteTask] Error message:', e?.message || 'Unknown error');
      setTasks(prevTasks);
    }
  };

  const archiveTask = async (id: string) => {
    setTasks(prev => prev.map(t => t.id === id ? { ...t, status: 'Archived' as TaskStatus } : t));
    try {
      await api.updateTaskStatus(id, 'Archived');
    } catch (e) {
      console.error("API Error (archiveTask)", e);
    }
  };

  const resolveMatterId = (matterId?: string) => {
    if (!matterId) return undefined;
    return matters.some(matter => matter.id === matterId) ? matterId : undefined;
  };

  const isMissingMatterError = (error: unknown) => {
    if (!error) return false;
    const message = error instanceof Error ? error.message : String(error);
    return message.toLowerCase().includes('selected matter was not found');
  };

  const addTimeEntry = async (entryData: any): Promise<boolean> => {
    const payload = {
      ...entryData,
      matterId: resolveMatterId(entryData.matterId),
      isBillable: entryData.isBillable ?? true
    };
    const tempEntry = {
      ...payload,
      id: `te-${Date.now()}`,
      approvalStatus: payload.approvalStatus || 'Pending'
    };
    setTimeEntries(prev => [tempEntry, ...prev]);
    try {
      const newEntry = await api.createTimeEntry(payload);
      if (!newEntry) {
        setTimeEntries(prev => prev.filter(t => t.id !== tempEntry.id));
        return false;
      }
      setTimeEntries(prev => [newEntry, ...prev.filter(t => t.id !== tempEntry.id)]);
      return true;
    } catch (e) {
      if (payload.matterId && isMissingMatterError(e)) {
        setMatters(prev => prev.filter(matter => matter.id !== payload.matterId));
        try {
          const retryEntry = await api.createTimeEntry({ ...payload, matterId: undefined });
          if (retryEntry) {
            setTimeEntries(prev => [retryEntry, ...prev.filter(t => t.id !== tempEntry.id)]);
            return true;
          }
        } catch (retryError) {
          console.error("API Error (addTimeEntry retry)", retryError);
        }
      }
      console.error("API Error (addTimeEntry)", e);
      setTimeEntries(prev => prev.filter(t => t.id !== tempEntry.id));
      return false;
    }
  };

  const addClient = async (clientData: any): Promise<Client> => {
    const temp = { ...clientData, id: `c-${Date.now()}` };
    setClients(prev => [temp, ...prev]);
    try {
      const newClient = await api.createClient(clientData);
      setClients(prev => [newClient, ...prev.filter(c => c.id !== temp.id)]);
      return newClient;
    } catch (e) {
      console.error("API Error", e);
      setClients(prev => prev.filter(c => c.id !== temp.id));
      throw e;
    }
  };

  const updateClient = async (id: string, data: Partial<Client> & { statusChangeNote?: string }) => {
    const prev = clients;
    const { statusChangeNote, ...clientData } = data;
    // Optimistic update
    setClients(prevClients => prevClients.map(c => c.id === id ? { ...c, ...clientData } : c));
    try {
      const updated = await api.updateClient(id, data);
      if (updated) {
        setClients(prevClients => prevClients.map(c => c.id === id ? { ...c, ...updated } : c));
      }
    } catch (e) {
      console.error("API Error (updateClient)", e);
      setClients(prev); // revert
      throw e;
    }
  };

  const addLead = async (leadData: any) => {
    const temp = { ...leadData, id: `l-${Date.now()}` };
    setLeads(prev => [temp, ...prev]);
    try {
      const newLead = await api.createLead(leadData);
      setLeads(prev => [newLead, ...prev.filter(l => l.id !== temp.id)]);
    } catch (e) { console.error("API Error", e); }
  };

  const updateLead = async (id: string, data: Partial<Lead>) => {
    setLeads(prev => prev.map(l => l.id === id ? { ...l, ...data } : l));
    try {
      const updated = await api.updateLead(id, data);
      setLeads(prev => prev.map(l => l.id === id ? updated : l));
    } catch (e) { console.error("API Error (updateLead)", e); }
  };

  const deleteLead = async (id: string) => {
    const prev = leads;
    setLeads(prev => prev.filter(l => l.id !== id));
    try {
      await api.deleteLead(id);
    } catch (e) {
      console.error("API Error (deleteLead)", e);
      setLeads(prev);
    }
  };

  const addEvent = async (eventData: any) => {
    const temp = { ...eventData, id: `ev-${Date.now()}` };
    setEvents(prev => [...prev, temp].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()));
    try {
      const newEvent = await api.createEvent(eventData);
      setEvents(prev => [...prev.filter(e => e.id !== temp.id), newEvent].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()));
    } catch (e) { console.error("API Error", e); }
  };

  const updateEvent = async (id: string, data: Partial<CalendarEvent>) => {
    const prev = events;
    setEvents(prevEvents => prevEvents.map(e => e.id === id ? { ...e, ...data } : e));
    try {
      const updated = await api.updateEvent(id, data);
      if (updated) {
        setEvents(prevEvents => prevEvents.map(e => e.id === id ? updated : e));
      }
    } catch (e) {
      console.error("API Error (updateEvent)", e);
      setEvents(prev);
    }
  };

  const deleteEvent = async (id: string) => {
    const prev = events;
    setEvents(prev => prev.filter(e => e.id !== id));
    try {
      await api.deleteEvent(id);
    } catch (e) {
      console.error("API Error (deleteEvent)", e);
      setEvents(prev);
    }
  };

  const addInvoice = async (invoiceData: any) => {
    const temp = { ...invoiceData, id: `inv-${Date.now()}` };
    setInvoices(prev => [temp, ...prev]);
    try {
      const newInvoice = await api.createInvoice(invoiceData);
      setInvoices(prev => [newInvoice, ...prev.filter(i => i.id !== temp.id)]);
    } catch (e) { console.error("API Error", e); }
  };

  const updateInvoice = (id: string, data: any) => {
    const prev = invoices;
    setInvoices(items => items.map(inv => inv.id === id ? { ...inv, ...data } : inv));
    api.updateInvoice(id, data)
      .then(updated => {
        if (updated) {
          setInvoices(items => items.map(inv => inv.id === id ? { ...inv, ...updated } : inv));
        }
      })
      .catch(e => {
        console.error("API Error (updateInvoice)", e);
        setInvoices(prev);
      });
  };

  const deleteInvoice = (id: string) => {
    const prev = invoices;
    setInvoices(items => items.filter(inv => inv.id !== id));
    api.deleteInvoice(id).catch(e => {
      console.error("API Error (deleteInvoice)", e);
      setInvoices(prev);
    });
  };

  const markAsBilled = async (matterId: string) => {
    // Optimistic
    setTimeEntries(prev => prev.map(e => {
      if (e.matterId !== matterId) return e;
      if (e.isBillable === false) return e;
      const status = (e.approvalStatus || '').toLowerCase();
      if (status && status !== 'approved') return e;
      return { ...e, billed: true };
    }));
    setExpenses(prev => prev.map(e => {
      if (e.matterId !== matterId) return e;
      const status = (e.approvalStatus || '').toLowerCase();
      if (status && status !== 'approved') return e;
      return { ...e, billed: true };
    }));

    try {
      await api.markAsBilled(matterId);
    } catch (e) { console.error("API Error", e); }
  };

  // Local Actions
  const addExpense = async (expenseData: any): Promise<boolean> => {
    const payload = {
      ...expenseData,
      matterId: resolveMatterId(expenseData.matterId)
    };
    const tempExpense = {
      ...payload,
      id: `e-${Date.now()}`,
      approvalStatus: payload.approvalStatus || 'Pending'
    };
    setExpenses(prev => [tempExpense, ...prev]);
    try {
      const newExpense = await api.createExpense(payload);
      if (!newExpense) {
        setExpenses(prev => prev.filter(e => e.id !== tempExpense.id));
        return false;
      }
      setExpenses(prev => [newExpense, ...prev.filter(e => e.id !== tempExpense.id)]);
      return true;
    } catch (e) {
      if (payload.matterId && isMissingMatterError(e)) {
        setMatters(prev => prev.filter(matter => matter.id !== payload.matterId));
        try {
          const retryExpense = await api.createExpense({ ...payload, matterId: undefined });
          if (retryExpense) {
            setExpenses(prev => [retryExpense, ...prev.filter(e => e.id !== tempExpense.id)]);
            return true;
          }
        } catch (retryError) {
          console.error("API Error (addExpense retry)", retryError);
        }
      }
      console.error("API Error (addExpense)", e);
      setExpenses(prev => prev.filter(e => e.id !== tempExpense.id));
      return false;
    }
  };

  const approveTimeEntry = async (id: string): Promise<boolean> => {
    const prev = timeEntries;
    const now = new Date().toISOString();
    setTimeEntries(items => items.map(entry =>
      entry.id === id ? { ...entry, approvalStatus: 'Approved', approvedAt: now, rejectedAt: undefined, rejectionReason: undefined } : entry
    ));
    try {
      const updated = await api.approveTimeEntry(id);
      if (!updated) {
        setTimeEntries(prev);
        return false;
      }
      setTimeEntries(items => items.map(entry => entry.id === id ? { ...entry, ...updated } : entry));
      return true;
    } catch (e) {
      console.error("API Error (approveTimeEntry)", e);
      setTimeEntries(prev);
      return false;
    }
  };

  const rejectTimeEntry = async (id: string, reason?: string): Promise<boolean> => {
    const prev = timeEntries;
    const now = new Date().toISOString();
    setTimeEntries(items => items.map(entry =>
      entry.id === id ? { ...entry, approvalStatus: 'Rejected', rejectedAt: now, rejectionReason: reason } : entry
    ));
    try {
      const updated = await api.rejectTimeEntry(id, reason);
      if (!updated) {
        setTimeEntries(prev);
        return false;
      }
      setTimeEntries(items => items.map(entry => entry.id === id ? { ...entry, ...updated } : entry));
      return true;
    } catch (e) {
      console.error("API Error (rejectTimeEntry)", e);
      setTimeEntries(prev);
      return false;
    }
  };

  const approveExpense = async (id: string): Promise<boolean> => {
    const prev = expenses;
    const now = new Date().toISOString();
    setExpenses(items => items.map(expense =>
      expense.id === id ? { ...expense, approvalStatus: 'Approved', approvedAt: now, rejectedAt: undefined, rejectionReason: undefined } : expense
    ));
    try {
      const updated = await api.approveExpense(id);
      if (!updated) {
        setExpenses(prev);
        return false;
      }
      setExpenses(items => items.map(expense => expense.id === id ? { ...expense, ...updated } : expense));
      return true;
    } catch (e) {
      console.error("API Error (approveExpense)", e);
      setExpenses(prev);
      return false;
    }
  };

  const rejectExpense = async (id: string, reason?: string): Promise<boolean> => {
    const prev = expenses;
    const now = new Date().toISOString();
    setExpenses(items => items.map(expense =>
      expense.id === id ? { ...expense, approvalStatus: 'Rejected', rejectedAt: now, rejectionReason: reason } : expense
    ));
    try {
      const updated = await api.rejectExpense(id, reason);
      if (!updated) {
        setExpenses(prev);
        return false;
      }
      setExpenses(items => items.map(expense => expense.id === id ? { ...expense, ...updated } : expense));
      return true;
    } catch (e) {
      console.error("API Error (rejectExpense)", e);
      setExpenses(prev);
      return false;
    }
  };
  const addMessage = (msg: Message) => setMessages(prev => [msg, ...prev]);
  const markMessageRead = (id: string) => setMessages(prev => prev.map(m => m.id === id ? { ...m, read: true } : m));
  const addDocument = (doc: DocumentFile) => setDocuments(prev => [doc, ...prev]);
  const updateDocument = async (id: string, data: Partial<DocumentFile>) => {
    // optimistic
    setDocuments(prev => prev.map(doc => doc.id === id ? { ...doc, ...data } : doc));
    try {
      const payload: any = {};
      if ('matterId' in data) payload.matterId = data.matterId ?? null;
      if ('description' in data) payload.description = data.description ?? null;
      if ('tags' in data) payload.tags = data.tags ?? null;
      if ('category' in data) payload.category = data.category ?? null;
      if ('status' in data) payload.status = data.status ?? null;
      if ('legalHoldReason' in data) payload.legalHoldReason = data.legalHoldReason ?? null;
      const updated = await api.updateDocument(id, payload);
      setDocuments(prev => prev.map(doc => doc.id === id ? { ...doc, ...normalizeDocument(updated) } : doc));
    } catch (e) {
      console.error("API Error (updateDocument)", e);
    }
  };
  const bulkAssignDocuments = async (ids: string[], matterId?: string | null) => {
    // optimistic
    setDocuments(prev => prev.map(d => ids.includes(d.id) ? { ...d, matterId: matterId || undefined } : d));
    try {
      await api.bulkAssignDocuments({ ids, matterId: matterId || null });
    } catch (e) {
      console.error("API Error (bulkAssignDocuments)", e);
    }
  };
  const deleteDocument = (id: string) => {
    setDocuments(prev => prev.filter(doc => doc.id !== id));
  };

  const markNotificationRead = async (id: string) => {
    setNotifications(prev => prev.map(n => n.id === id ? { ...n, read: true } : n));
    try {
      await api.markNotificationRead(id);
    } catch (e) { console.error("API Error", e); }
  };

  const markNotificationUnread = async (id: string) => {
    setNotifications(prev => prev.map(n => n.id === id ? { ...n, read: false } : n));
    try {
      await api.markNotificationUnread(id);
    } catch (e) { console.error("API Error", e); }
  };

  const markAllNotificationsRead = async () => {
    setNotifications(prev => prev.map(n => ({ ...n, read: true })));
    try {
      await api.markAllNotificationsRead();
    } catch (e) { console.error("API Error", e); }
  };

  const updateUserProfile = async (data: any) => {
    try {
      await api.updateUserProfile(data);
    } catch (e) {
      console.error("API Error", e);
      throw e;
    }
  };

  return (
    <DataContext.Provider value={{
      matters, clients, timeEntries, expenses, messages, events, documents, invoices, leads, tasks, taskTemplates, notifications,
      activeTimer, startTimer, stopTimer, updateTimer, pauseTimer, resumeTimer,
      addMatter, updateMatter, deleteMatter,
      addTimeEntry, addExpense, approveTimeEntry, rejectTimeEntry, approveExpense, rejectExpense, addMessage, markMessageRead, addEvent, updateEvent, deleteEvent, addDocument, updateDocument, deleteDocument, addInvoice, updateInvoice, deleteInvoice, addClient, addLead, updateLead, deleteLead, addTask,
      updateTaskStatus, updateTask, deleteTask, archiveTask, createTasksFromTemplate, markAsBilled, markNotificationRead, markNotificationUnread, markAllNotificationsRead, updateUserProfile, updateClient,
      bulkAssignDocuments
    }}>
      {children}
    </DataContext.Provider>
  );
};

export const useData = () => {
  const context = useContext(DataContext);
  if (!context) {
    throw new Error('useData must be used within a DataProvider');
  }
  return context;
};
