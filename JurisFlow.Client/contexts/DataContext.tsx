import React, { createContext, useContext, useState, useEffect, useRef, ReactNode } from 'react';
import { Matter, Client, TimeEntry, Message, Expense, CalendarEvent, DocumentFile, Invoice, InvoiceStatus, Lead, Task, TaskStatus, Notification as AppNotification, TaskTemplate, ActiveTimer } from '../types';
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

  addMatter: (item: any) => Promise<Matter>;
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
  addInvoice: (item: any) => Promise<Invoice>;
  updateInvoice: (id: string, data: any) => Promise<Invoice | null>;
  deleteInvoice: (id: string) => Promise<void>;
  approveInvoice: (id: string) => Promise<Invoice | null>;
  sendInvoice: (id: string, invoiceHint?: Partial<Invoice>) => Promise<Invoice | null>;
  recordInvoicePayment: (id: string, data: { amount: number; reference?: string }, invoiceHint?: Partial<Invoice>) => Promise<Invoice | null>;
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
type MatterWithClientId = Matter & { clientId?: string; relatedClientIds?: string[]; relatedClients?: Client[] };
const DATA_BOOTSTRAP_CACHE_PREFIX = 'jf_bootstrap_cache';
const DATA_BOOTSTRAP_CACHE_TTL_MS = 2 * 60 * 1000;

type CachedBootstrapState = {
  matters: Matter[];
  clients: Client[];
  timeEntries: TimeEntry[];
  expenses: Expense[];
  events: CalendarEvent[];
  invoices: Invoice[];
  leads: Lead[];
  tasks: Task[];
  taskTemplates: TaskTemplate[];
  notifications: AppNotification[];
  documents: DocumentFile[];
};

const getBootstrapCacheKey = (userId?: string | null) => {
  if (typeof window === 'undefined' || !userId) return null;
  const tenantSlug = localStorage.getItem('tenant_slug') || 'default';
  return `${DATA_BOOTSTRAP_CACHE_PREFIX}:${tenantSlug}:${userId}`;
};

const readBootstrapCache = (cacheKey: string): CachedBootstrapState | null => {
  if (typeof window === 'undefined') return null;

  try {
    const raw = sessionStorage.getItem(cacheKey);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as { updatedAt?: number; payload?: CachedBootstrapState } | null;
    if (!parsed?.updatedAt || !parsed?.payload) {
      sessionStorage.removeItem(cacheKey);
      return null;
    }

    if (Date.now() - parsed.updatedAt > DATA_BOOTSTRAP_CACHE_TTL_MS) {
      sessionStorage.removeItem(cacheKey);
      return null;
    }

    return parsed.payload;
  } catch {
    sessionStorage.removeItem(cacheKey);
    return null;
  }
};

const writeBootstrapCache = (cacheKey: string, payload: CachedBootstrapState) => {
  if (typeof window === 'undefined') return;

  try {
    sessionStorage.setItem(cacheKey, JSON.stringify({
      updatedAt: Date.now(),
      payload
    }));
  } catch {
    // Ignore storage failures.
  }
};

const runWhenBrowserIdle = (callback: () => void, timeout = 800) => {
  if (typeof window === 'undefined') {
    callback();
    return () => undefined;
  }

  const requestIdle = (window as any).requestIdleCallback as ((cb: () => void, options?: { timeout: number }) => number) | undefined;
  const cancelIdle = (window as any).cancelIdleCallback as ((id: number) => void) | undefined;

  if (typeof requestIdle === 'function') {
    const idleId = requestIdle(callback, { timeout });
    return () => {
      if (typeof cancelIdle === 'function') {
        cancelIdle(idleId);
      }
    };
  }

  const timerId = window.setTimeout(callback, timeout);
  return () => window.clearTimeout(timerId);
};

const DEFERRED_BOOTSTRAP_PRIORITY_TABS = new Set([
  'dashboard',
  'billing',
  'documents',
  'crm',
  'reports'
]);

const shouldPrioritizeDeferredBootstrap = () => {
  if (typeof window === 'undefined') return false;

  const normalizedHash = window.location.hash
    .trim()
    .toLowerCase()
    .replace(/^#\/?/, '');

  return DEFERRED_BOOTSTRAP_PRIORITY_TABS.has(normalizedHash || 'dashboard');
};

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
  const clientsRef = useRef<Client[]>([]);
  const mattersRef = useRef<Matter[]>([]);

  // Local-only state
  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [messages, setMessages] = useState<Message[]>([
    { id: 'msg1', from: 'Jessica Pearson', subject: 'Managing Partner Meeting', preview: 'We need to discuss the new associates...', date: '09:00 AM', read: false }
  ]);
  const [documents, setDocuments] = useState<DocumentFile[]>([]);

  useEffect(() => {
    clientsRef.current = clients.filter((client): client is Client => !!client && typeof client.id === 'string');
  }, [clients]);

  useEffect(() => {
    mattersRef.current = matters.filter((matter): matter is Matter => !!matter && typeof matter.id === 'string');
  }, [matters]);

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

    const timerSnapshot = activeTimer;

    // Calculate total duration
    let totalDurationMs = timerSnapshot.elapsed;
    if (timerSnapshot.isRunning) {
      totalDurationMs += (Date.now() - timerSnapshot.startTime);
    }

    const minutes = Math.max(1, Math.ceil(totalDurationMs / 1000 / 60));

    // Stop the timer immediately in the UI. If persistence fails, restore a paused snapshot.
    setActiveTimer(null);

    const saved = await addTimeEntry({
      matterId: timerSnapshot.matterId,
      description: timerSnapshot.description,
      duration: minutes,
      date: new Date().toISOString(),
      rate: timerSnapshot.rate || 0,
      billed: false,
      type: 'time',
      activityCode: timerSnapshot.activityCode,
      isBillable: timerSnapshot.isBillable ?? true
    });

    if (!saved) {
      setActiveTimer({
        ...timerSnapshot,
        isRunning: false,
        elapsed: totalDurationMs,
        startTime: Date.now()
      });
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

  const hasStringId = <T extends { id?: unknown }>(item: T | null | undefined): item is T & { id: string } =>
    !!item && typeof item === 'object' && typeof item.id === 'string' && item.id.trim().length > 0;

  const sanitizeIdentifiableItems = <T extends { id?: unknown }>(items: T[] | null | undefined): T[] => {
    if (!Array.isArray(items) || items.length === 0) return [];
    return items.filter(hasStringId);
  };

const sanitizeClients = (items: Client[] | null | undefined): Client[] =>
  sanitizeIdentifiableItems<Client>(items);

  const sanitizeMatters = (items: Matter[] | null | undefined): Matter[] =>
    sanitizeIdentifiableItems<Matter>(items);

  const sanitizeLeads = (items: Lead[] | null | undefined): Lead[] =>
    sanitizeIdentifiableItems<Lead>(items);

  const sanitizeInvoices = (items: Invoice[] | null | undefined): Invoice[] =>
    sanitizeIdentifiableItems<Invoice>(items);

  const sanitizeMatterLinkedItems = <T extends { id?: unknown; matterId?: string | null }>(items: T[] | null | undefined): T[] =>
    sanitizeIdentifiableItems<T>(items);

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

  const getMatterClientId = (matter: Partial<MatterWithClientId>): string | undefined => {
    const fromMatter = typeof matter.clientId === 'string' ? matter.clientId.trim() : '';
    if (fromMatter) return fromMatter;

    const fromClient = typeof matter.client?.id === 'string' ? matter.client.id.trim() : '';
    return fromClient || undefined;
  };

  const getMatterRelatedClientIds = (matter: Partial<MatterWithClientId>): string[] => {
    const fromIds = Array.isArray(matter.relatedClientIds)
      ? matter.relatedClientIds.filter((id): id is string => typeof id === 'string' && id.trim().length > 0).map((id) => id.trim())
      : [];
    const fromClients = Array.isArray(matter.relatedClients)
      ? matter.relatedClients
          .filter((client): client is Client => !!client && typeof client.id === 'string' && client.id.trim().length > 0)
          .map((client) => client.id.trim())
      : [];

    const primaryClientId = getMatterClientId(matter);
    return Array.from(new Set([...fromIds, ...fromClients]))
      .filter((id) => !primaryClientId || id !== primaryClientId);
  };

  const hydrateMatterClient = (matter: Matter, clientById: Map<string, Client>): Matter => {
    const matterWithClientId = matter as MatterWithClientId;
    const clientId = getMatterClientId(matterWithClientId);
    const relatedClientIds = getMatterRelatedClientIds(matterWithClientId);
    const existingRelatedClients = Array.isArray(matterWithClientId.relatedClients)
      ? matterWithClientId.relatedClients.filter((client): client is Client => !!client && typeof client.id === 'string')
      : [];

    const resolvedRelatedClients = relatedClientIds
      .map((id) => clientById.get(id) || existingRelatedClients.find((client) => client.id === id))
      .filter((client): client is Client => !!client);

    if (!clientId) {
      if (relatedClientIds.length === 0) {
        return matter;
      }

      return {
        ...matter,
        relatedClientIds,
        relatedClients: resolvedRelatedClients
      } as Matter;
    }

    const resolvedClient = clientById.get(clientId) || matterWithClientId.client;
    if (!resolvedClient && matterWithClientId.clientId === clientId && relatedClientIds.length === 0) {
      return matter;
    }

    return {
      ...matter,
      ...(resolvedClient ? { client: resolvedClient } : {}),
      clientId,
      relatedClientIds,
      relatedClients: resolvedRelatedClients
    } as Matter;
  };

const hydrateMattersWithClients = (matterItems: Matter[], clientItems: Client[]): Matter[] => {
  const safeMatterItems = sanitizeMatters(matterItems);
  if (safeMatterItems.length === 0) return [];

    const safeClientItems = sanitizeClients(clientItems);
    if (safeClientItems.length === 0) return safeMatterItems;

    const clientById = new Map(safeClientItems.map((client) => [client.id, client]));
  return safeMatterItems.map((matter) => hydrateMatterClient(matter, clientById));
};

const normalizeEmail = (value: unknown) =>
  typeof value === 'string' ? value.trim().toLowerCase() : '';

const getErrorMessage = (error: unknown) =>
  error instanceof Error ? error.message : String(error ?? '');

const isRecoverableCreateError = (error: unknown) => {
  const message = getErrorMessage(error).toLowerCase();
  if (!message) return false;

  return (
    message.includes('failed to fetch') ||
    message.includes('empty response') ||
    message.includes('unexpected non-json') ||
    message.includes('expected json') ||
    message.includes('internal server error') ||
    message.includes('bad gateway') ||
    message.includes('gateway timeout') ||
    message.includes('service unavailable')
  );
};

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  const preserveMatterAssociations = (incomingMatterItems: Matter[], existingMatterItems: Matter[] = mattersRef.current): Matter[] => {
    const safeIncomingMatterItems = sanitizeMatters(incomingMatterItems);
    if (safeIncomingMatterItems.length === 0) return [];

    const existingById = new Map(
      sanitizeMatters(existingMatterItems).map((matter) => [matter.id, matter])
    );

    return safeIncomingMatterItems.map((matter) => {
      const existingMatter = existingById.get(matter.id);
      if (!existingMatter) {
        return matter;
      }

      const incomingMatter = matter as MatterWithClientId;
      const existingMatterWithClientId = existingMatter as MatterWithClientId;
      const incomingClientId = getMatterClientId(incomingMatter);
      const existingClientId = getMatterClientId(existingMatterWithClientId);
      const incomingRelatedClientIds = getMatterRelatedClientIds(incomingMatter);
      const existingRelatedClientIds = getMatterRelatedClientIds(existingMatterWithClientId);

      return {
        ...matter,
        ...(!incomingClientId && existingClientId ? { clientId: existingClientId } : {}),
        ...(!incomingMatter.client && existingMatter.client && (!incomingClientId || !existingClientId || incomingClientId === existingClientId)
          ? { client: existingMatter.client }
          : {}),
        ...(incomingRelatedClientIds.length === 0 && existingRelatedClientIds.length > 0
          ? {
              relatedClientIds: existingRelatedClientIds,
              relatedClients: existingMatterWithClientId.relatedClients
            }
          : {})
      } as Matter;
    });
  };

  const findCreatedMatter = (matterItems: Matter[], payload: { clientId?: string; caseNumber?: string; name?: string }) => {
    return matterItems.find((matter) => {
      const matchedClientId = getMatterClientId(matter as MatterWithClientId);
      return matchedClientId === payload.clientId
        && String(matter.caseNumber || '') === String(payload.caseNumber || '')
        && String(matter.name || '') === String(payload.name || '');
    });
  };

  const recoverClientByEmail = async (email: string): Promise<{ client: Client; freshClients: Client[] } | null> => {
    const normalizedTargetEmail = normalizeEmail(email);
    if (!normalizedTargetEmail) {
      return null;
    }

    for (const delayMs of [0, 150, 350]) {
      if (delayMs > 0) {
        await sleep(delayMs);
      }

      const refreshedClients = sanitizeClients(await api.getClients().catch(() => null));
      const recoveredClient = refreshedClients.find((client) => normalizeEmail(client.email) === normalizedTargetEmail);
      if (recoveredClient) {
        return {
          client: recoveredClient,
          freshClients: refreshedClients
        };
      }
    }

    return null;
  };

  const recoverCreatedMatter = async (
    payload: { clientId?: string; caseNumber?: string; name?: string },
    optimisticClient: Client
  ): Promise<{ matter: Matter; freshClients: Client[] | null } | null> => {
    if (!payload.clientId || !payload.caseNumber || !payload.name) {
      return null;
    }

    for (const delayMs of [0, 200, 450, 900]) {
      if (delayMs > 0) {
        await sleep(delayMs);
      }

      const [refreshedMatterPayload, refreshedClientPayload] = await Promise.all([
        api.getMatters().catch(() => null),
        api.getClients().catch(() => null)
      ]);

      const refreshedMatters = Array.isArray(refreshedMatterPayload)
        ? preserveMatterAssociations(sanitizeMatters(refreshedMatterPayload), mattersRef.current)
        : [];
      const matchedMatter = findCreatedMatter(refreshedMatters, payload);
      if (!matchedMatter) {
        continue;
      }

      const safeClients = Array.isArray(refreshedClientPayload)
        ? sanitizeClients(refreshedClientPayload)
        : null;
      const clientPool = safeClients ?? clientsRef.current;
      const clientById = new Map(clientPool.map((client) => [client.id, client]));
      const fallbackClient = clientById.get(payload.clientId) || optimisticClient;

      return {
        matter: hydrateMatterClient(
          {
            ...matchedMatter,
            client: matchedMatter.client || fallbackClient,
            clientId: getMatterClientId(matchedMatter as MatterWithClientId) || payload.clientId
          } as Matter,
          clientById
        ),
        freshClients: safeClients
      };
    }

    return null;
  };

  const getInvoiceClientId = (invoice: Partial<Invoice>): string | undefined => {
    const fromInvoice = typeof invoice.clientId === 'string' ? invoice.clientId.trim() : '';
    if (fromInvoice) return fromInvoice;

    const fromClient = typeof invoice.client?.id === 'string' ? invoice.client.id.trim() : '';
    return fromClient || undefined;
  };

  const isTemporaryInvoiceId = (id: unknown): id is string =>
    typeof id === 'string' && /^inv-\d+$/.test(id);

  const matchesInvoiceCandidate = (invoice: Partial<Invoice>, candidate: Partial<Invoice>) => {
    const candidateNumber = typeof candidate.number === 'string' ? candidate.number.trim() : '';
    const invoiceNumber = typeof invoice.number === 'string' ? invoice.number.trim() : '';
    const candidateClientId = getInvoiceClientId(candidate);
    const invoiceClientId = getInvoiceClientId(invoice);
    const candidateMatterId = typeof candidate.matterId === 'string' ? candidate.matterId.trim() : '';
    const invoiceMatterId = typeof invoice.matterId === 'string' ? invoice.matterId.trim() : '';
    const candidateAmount = Number(candidate.amount ?? 0);
    const invoiceAmount = Number(invoice.amount ?? 0);

    if (candidateNumber && invoiceNumber && candidateNumber === invoiceNumber) {
      if (candidateClientId && invoiceClientId && candidateClientId !== invoiceClientId) return false;
      if (candidateMatterId && invoiceMatterId && candidateMatterId !== invoiceMatterId) return false;
      return Math.abs(candidateAmount - invoiceAmount) < 0.01 || candidateAmount === 0 || invoiceAmount === 0;
    }

    return !!candidateClientId
      && !!invoiceClientId
      && candidateClientId === invoiceClientId
      && !!candidateMatterId
      && !!invoiceMatterId
      && candidateMatterId === invoiceMatterId
      && Math.abs(candidateAmount - invoiceAmount) < 0.01;
  };

  const recoverCreatedInvoice = async (invoiceData: Partial<Invoice>): Promise<Invoice | null> => {
    for (const delayMs of [0, 200, 500, 1000, 1800]) {
      if (delayMs > 0) {
        await sleep(delayMs);
      }

      const refreshedInvoicesPayload = await api.getInvoices().catch(() => null);
      if (!Array.isArray(refreshedInvoicesPayload)) {
        continue;
      }

      const refreshedInvoices = sanitizeInvoices(refreshedInvoicesPayload)
        .map((invoice) => normalizeInvoice(invoice));
      const matchedInvoice = refreshedInvoices.find((invoice) => matchesInvoiceCandidate(invoice, invoiceData));
      if (!matchedInvoice) {
        continue;
      }

      return hydrateInvoiceClient(
        normalizeInvoice(matchedInvoice),
        new Map(clientsRef.current.map((client) => [client.id, client]))
      );
    }

    return null;
  };

  const resolveInvoiceMutationTarget = async (id: string, invoiceHint?: Partial<Invoice>) => {
    const localInvoice = invoices.find((invoice) => invoice.id === id) || invoiceHint || null;
    if (!localInvoice || !isTemporaryInvoiceId(id)) {
      return {
        resolvedId: id,
        localInvoice
      };
    }

    const recoveredInvoice = await recoverCreatedInvoice(localInvoice);
    if (!recoveredInvoice) {
      throw new Error('Invoice is still being saved. Please wait a moment and try again.');
    }

    setInvoices(items => [
      recoveredInvoice,
      ...items.filter(inv => inv.id !== id && inv.id !== recoveredInvoice.id)
    ]);

    return {
      resolvedId: recoveredInvoice.id,
      localInvoice: recoveredInvoice
    };
  };

  const hydrateInvoiceClient = (invoice: Invoice, clientById: Map<string, Client>): Invoice => {
    const clientId = getInvoiceClientId(invoice);
    if (!clientId) {
      return invoice;
    }

    const resolvedClient = clientById.get(clientId) || invoice.client;
    return {
      ...invoice,
      clientId,
      ...(resolvedClient ? { client: resolvedClient } : {})
    };
  };

  const normalizeInvoice = (invoice: any): Invoice => {
    const amount = Number(invoice?.amount ?? invoice?.total ?? 0);
    const amountPaid = Number(invoice?.amountPaid ?? 0);
    const balance = Number(invoice?.balance ?? Math.max(0, amount - amountPaid));

    return {
      ...invoice,
      subtotal: Number(invoice?.subtotal ?? 0),
      taxAmount: Number(invoice?.taxAmount ?? invoice?.tax ?? 0),
      discount: Number(invoice?.discount ?? 0),
      amount,
      amountPaid,
      balance,
      issueDate: invoice?.issueDate ?? invoice?.createdAt ?? new Date().toISOString(),
      dueDate: invoice?.dueDate ?? invoice?.issueDate ?? invoice?.createdAt ?? new Date().toISOString(),
      lineItems: Array.isArray(invoice?.lineItems) ? invoice.lineItems : undefined,
      payments: Array.isArray(invoice?.payments) ? invoice.payments : undefined
    } as Invoice;
  };

  const hydrateInvoicesWithClients = (invoiceItems: Invoice[], clientItems: Client[]): Invoice[] => {
    const safeInvoiceItems = sanitizeInvoices(invoiceItems).map((invoice) => normalizeInvoice(invoice));
    if (safeInvoiceItems.length === 0) return [];

    const safeClientItems = sanitizeClients(clientItems);
    if (safeClientItems.length === 0) return safeInvoiceItems;

    const clientById = new Map(safeClientItems.map((client) => [client.id, client]));
    return safeInvoiceItems.map((invoice) => hydrateInvoiceClient(invoice, clientById));
  };

  const filterVisibleMatters = (matterItems: Matter[]): Matter[] => {
    return sanitizeMatters(matterItems).filter((matter) => String(matter.status || '').toLowerCase() !== 'deleted');
  };

  const buildVisibleMatterIdSet = (matterItems: Matter[]) => {
    return new Set(filterVisibleMatters(matterItems).map((matter) => matter.id));
  };

  const filterByVisibleMatter = <T extends { matterId?: string | null }>(items: T[], matterItems: Matter[]): T[] => {
    const safeItems = sanitizeMatterLinkedItems(items);
    if (safeItems.length === 0) return [];
    const visibleMatterIds = buildVisibleMatterIdSet(matterItems);
    return safeItems.filter((item) => !item.matterId || visibleMatterIds.has(item.matterId));
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
    const incomingClients = Array.isArray(payload.clients) ? sanitizeClients(payload.clients as Client[]) : null;
    const normalizedMatters = Array.isArray(payload.matters)
      ? preserveMatterAssociations(sanitizeMatters(payload.matters), mattersRef.current)
      : null;
    const hasMatterSource = Array.isArray(payload.matters) || mattersRef.current.length > 0;
    const nextMatters = Array.isArray(normalizedMatters)
      ? filterVisibleMatters(
        incomingClients
          ? hydrateMattersWithClients(normalizedMatters, incomingClients)
          : hydrateMattersWithClients(normalizedMatters, clientsRef.current)
      )
      : mattersRef.current;

    if (Array.isArray(payload.matters)) {
      setMatters(nextMatters);
    }
    if (Array.isArray(payload.tasks)) setTasks(hasMatterSource ? filterByVisibleMatter(payload.tasks, nextMatters) : payload.tasks);
    if (Array.isArray(payload.timeEntries)) setTimeEntries(hasMatterSource ? filterByVisibleMatter(payload.timeEntries, nextMatters) : payload.timeEntries);
    if (Array.isArray(payload.expenses)) setExpenses(hasMatterSource ? filterByVisibleMatter(payload.expenses, nextMatters) : payload.expenses);
    if (incomingClients) {
      setClients(incomingClients);
      setMatters((prev) => filterVisibleMatters(hydrateMattersWithClients(prev, incomingClients)));
      setInvoices((prev) => hydrateInvoicesWithClients(prev, incomingClients));
    }
    if (Array.isArray(payload.leads)) setLeads(sanitizeLeads(payload.leads));
    if (Array.isArray(payload.events)) setEvents(hasMatterSource ? filterByVisibleMatter(payload.events, nextMatters) : payload.events);
    if (Array.isArray(payload.invoices)) {
      const normalizedInvoices = sanitizeInvoices(payload.invoices).map((invoice) => normalizeInvoice(invoice));
      const nextInvoices = hasMatterSource ? filterByVisibleMatter(normalizedInvoices, nextMatters) : normalizedInvoices;
      setInvoices(incomingClients
        ? hydrateInvoicesWithClients(nextInvoices, incomingClients)
        : hydrateInvoicesWithClients(nextInvoices, clientsRef.current));
    }
    if (Array.isArray(payload.notifications)) setNotifications(payload.notifications);
    if (Array.isArray(payload.documents)) {
      const normalizedDocuments = payload.documents.map(normalizeDocument);
      setDocuments(hasMatterSource ? filterByVisibleMatter(normalizedDocuments, nextMatters) : normalizedDocuments);
    }
    if (Array.isArray(payload.taskTemplates)) setTaskTemplates(payload.taskTemplates);
  };

  // --- INITIAL LOAD ---
  useEffect(() => {
    let disposed = false;
    let cancelDeferredLoad: (() => void) | null = null;

    const loadInitialFallback = async () => {
      const [m, t, te, e, n] = await Promise.all([
        api.getMatters().catch(() => null),
        api.getTasks().catch(() => null),
        api.getTimeEntries().catch(() => null),
        api.getEvents().catch(() => null),
        api.getNotifications(user?.id).catch(() => [])
      ]);

      if (disposed) return;

      const visibleMatters = Array.isArray(m)
        ? filterVisibleMatters(hydrateMattersWithClients(preserveMatterAssociations(m, mattersRef.current), clientsRef.current))
        : mattersRef.current;

      if (Array.isArray(m)) setMatters(visibleMatters);
      if (Array.isArray(t)) setTasks(filterByVisibleMatter(t, visibleMatters));
      if (Array.isArray(te)) setTimeEntries(filterByVisibleMatter(te, visibleMatters));
      if (Array.isArray(e)) setEvents(filterByVisibleMatter(e, visibleMatters));
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

      const visibleMatters = mattersRef.current;

      if (Array.isArray(ex)) setExpenses(filterByVisibleMatter(ex, visibleMatters));
      if (Array.isArray(c)) {
        const safeClients = sanitizeClients(c);
        setClients(safeClients);
        setMatters((prev) => filterVisibleMatters(hydrateMattersWithClients(prev, safeClients)));
        setInvoices((prev) => hydrateInvoicesWithClients(prev, safeClients));
      }
      if (Array.isArray(l)) setLeads(sanitizeLeads(l));
      if (Array.isArray(i)) {
        const normalizedInvoices = sanitizeInvoices(i).map((invoice) => normalizeInvoice(invoice));
        const clientPool = Array.isArray(c) ? sanitizeClients(c) : clientsRef.current;
        setInvoices(hydrateInvoicesWithClients(filterByVisibleMatter(normalizedInvoices, visibleMatters), clientPool));
      }
      if (Array.isArray(docs)) setDocuments(filterByVisibleMatter(docs.map(normalizeDocument), visibleMatters));
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

        const cacheKey = getBootstrapCacheKey(user?.id);
        if (cacheKey) {
          const cachedPayload = readBootstrapCache(cacheKey);
          if (cachedPayload) {
            applyBootstrapPayload(cachedPayload);
            console.log('Hydrated initial data from session cache');
          }
        }

        console.log('Fetching initial data from API...');
        const connection = typeof navigator !== 'undefined' ? (navigator as any).connection : null;
        const effectiveType = String(connection?.effectiveType || '').toLowerCase();
        const saveData = Boolean(connection?.saveData);
        const constrainedNetwork =
          saveData ||
          effectiveType === 'slow-2g' ||
          effectiveType === '2g' ||
          effectiveType === '3g';
        const shouldLoadDeferredImmediately =
          shouldPrioritizeDeferredBootstrap() || !constrainedNetwork;

        const loadInitial = async () => {
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
        };

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

        await loadInitial();
        if (disposed) return;

        if (shouldLoadDeferredImmediately) {
          void loadDeferred();
        } else {
          cancelDeferredLoad = runWhenBrowserIdle(() => {
            if (!disposed) {
              void loadDeferred();
            }
          }, 250);
        }
      } catch (error) {
        console.warn('Failed to load data from backend.', error);
        if ((!isAuthenticated || !token) && !disposed) {
          clearLoadedData();
        }
      } finally {
        if (disposed && cancelDeferredLoad) {
          cancelDeferredLoad();
        }
      }
    };

    loadData();
    return () => {
      disposed = true;
      if (cancelDeferredLoad) {
        cancelDeferredLoad();
      }
    };
  }, [isAuthenticated, user?.id]);

  useEffect(() => {
    if (!isAuthenticated || !user?.id || typeof window === 'undefined') return;

    const cacheKey = getBootstrapCacheKey(user.id);
    if (!cacheKey) return;

    const hasCacheableData =
      matters.length > 0 ||
      clients.length > 0 ||
      tasks.length > 0 ||
      timeEntries.length > 0 ||
      expenses.length > 0 ||
      events.length > 0 ||
      invoices.length > 0 ||
      leads.length > 0 ||
      notifications.length > 0 ||
      taskTemplates.length > 0 ||
      documents.length > 0;

    if (!hasCacheableData) {
      sessionStorage.removeItem(cacheKey);
      return;
    }

    const cachePayload: CachedBootstrapState = {
      matters,
      clients,
      timeEntries,
      expenses,
      events,
      invoices,
      leads,
      tasks,
      taskTemplates,
      notifications,
      documents
    };

    return runWhenBrowserIdle(() => writeBootstrapCache(cacheKey, cachePayload), 900);
  }, [isAuthenticated, user?.id, matters, clients, timeEntries, expenses, events, invoices, leads, tasks, taskTemplates, notifications, documents]);

  useEffect(() => {
    setTasks((prev) => filterByVisibleMatter(prev, matters));
    setTimeEntries((prev) => filterByVisibleMatter(prev, matters));
    setExpenses((prev) => filterByVisibleMatter(prev, matters));
    setEvents((prev) => filterByVisibleMatter(prev, matters));
    setDocuments((prev) => filterByVisibleMatter(prev, matters));
    setInvoices((prev) => filterByVisibleMatter(prev, matters));
  }, [matters]);

  useEffect(() => {
    if (clients.length === 0) return;
    setInvoices((prev) => hydrateInvoicesWithClients(prev, clients));
  }, [clients]);

  // --- NOTIFICATION POLLING ---
  useEffect(() => {
    if (!isAuthenticated) return;

    const fetchNotifications = async () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        return;
      }

      try {
        const notifs = await api.getNotifications(user?.id);
        if (notifs) setNotifications(notifs);
      } catch (e) {
        // console.error("Notification Poll Error", e); // Silent fail
      }
    };

    // Initial fetch
    fetchNotifications();

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        void fetchNotifications();
      }
    };
    document.addEventListener('visibilitychange', handleVisibilityChange);

    // Poll every 60 seconds
    const interval = setInterval(fetchNotifications, 60000);
    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
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

  const addMatter = async (matterData: any): Promise<Matter> => {
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
    const payload = { ...matterData };
    delete payload.client;
    delete payload.relatedClients;
    delete payload.relatedClientIds;

    try {
      if (!payload.clientId) {
        throw new Error('Client is required to create a matter.');
      }
      const newMatter = await api.createMatter(payload);
      if (!newMatter) {
        throw new Error('Matter could not be created.');
      }
      // Replace temp with real
      const clientById = new Map(clientsRef.current.map((client) => [client.id, client]));
      const hydratedMatter = hydrateMatterClient(
        {
          ...newMatter,
          client: newMatter.client || optimisticClient,
          clientId: getMatterClientId(newMatter as MatterWithClientId) || payload.clientId
        } as Matter,
        clientById
      );
      setMatters(prev => [hydratedMatter, ...prev.filter(m => m.id !== tempId)]);
      void api.getClients()
        .then((freshClients) => {
          if (Array.isArray(freshClients)) {
            const safeClients = sanitizeClients(freshClients);
            setClients(safeClients);
            setMatters((prev) => hydrateMattersWithClients(prev, safeClients));
          }
        })
        .catch((refreshError) => {
          console.warn("Client list refresh after matter create failed", refreshError);
        });
      return hydratedMatter;
    } catch (e) {
      if (isRecoverableCreateError(e)) {
        try {
          const recovered = await recoverCreatedMatter(payload, optimisticClient);
          if (recovered) {
            if (Array.isArray(recovered.freshClients)) {
              setClients(recovered.freshClients);
            }
            setMatters(prev => [recovered.matter, ...prev.filter(m => m.id !== tempId && m.id !== recovered.matter.id)]);
            if (Array.isArray(recovered.freshClients)) {
              setMatters(prev => hydrateMattersWithClients(prev, recovered.freshClients || clientsRef.current));
            }
            return recovered.matter;
          }
        } catch (recoveryError) {
          console.warn('Matter create recovery failed', recoveryError);
        }
      }

      console.error("API Error (addMatter) - operating offline", e);
      setMatters(prev => prev.filter(m => m.id !== tempId));
      throw e;
    }
  };

  const updateMatter = async (id: string, data: Partial<Matter>) => {
    // Optimistic update
    const clientById = new Map(clients.map((client) => [client.id, client]));
    const existingMatter = matters.find((matter) => matter.id === id);
    setMatters(prev => prev.map(m => {
      if (m.id !== id) return m;
      const merged = {
        ...m,
        ...data,
        client: data.client || m.client,
        clientId: getMatterClientId(data as Partial<MatterWithClientId>) || getMatterClientId(m as MatterWithClientId)
      } as Matter;
      return hydrateMatterClient(merged, clientById);
    }));
    try {
      const mergedPayload = hydrateMatterClient(
        {
          ...(existingMatter || { id }),
          ...data,
          client: data.client || existingMatter?.client,
          clientId: getMatterClientId(data as Partial<MatterWithClientId>)
            || (existingMatter ? getMatterClientId(existingMatter as MatterWithClientId) : undefined)
        } as Matter,
        clientById
      );
      const payload = {
        ...mergedPayload,
        clientId: getMatterClientId(mergedPayload as MatterWithClientId)
      } as Partial<Matter>;
      delete (payload as any).client;
      delete (payload as any).relatedClients;
      delete (payload as any).relatedClientIds;

      const updated = await api.updateMatter(id, payload);
      setMatters(prev => prev.map(m => {
        if (m.id !== id) return m;
        return hydrateMatterClient({ ...m, ...updated } as Matter, clientById);
      }));
    } catch (e) {
      console.error("API Error (updateMatter)", e);
    }
  };

  const deleteMatter = async (id: string) => {
    const prev = matters;
    const prevTasks = tasks;
    const prevTimeEntries = timeEntries;
    const prevExpenses = expenses;
    const prevEvents = events;
    const prevDocuments = documents;
    const prevInvoices = invoices;
    setMatters(prev => prev.filter(m => m.id !== id));
    setTasks(prev => prev.filter(task => task.matterId !== id));
    setTimeEntries(prev => prev.filter(entry => entry.matterId !== id));
    setExpenses(prev => prev.filter(expense => expense.matterId !== id));
    setEvents(prev => prev.filter(event => event.matterId !== id));
    setDocuments(prev => prev.filter(document => document.matterId !== id));
    setInvoices(prev => prev.filter(invoice => invoice.matterId !== id));
    try {
      await api.deleteMatter(id);
    } catch (e) {
      console.error("API Error (deleteMatter)", e);
      setMatters(prev); // revert
      setTasks(prevTasks);
      setTimeEntries(prevTimeEntries);
      setExpenses(prevExpenses);
      setEvents(prevEvents);
      setDocuments(prevDocuments);
      setInvoices(prevInvoices);
      throw e;
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
    setClients(prev => [temp, ...sanitizeClients(prev)]);
    const normalizedEmail = normalizeEmail(clientData?.email);
    try {
      let newClient: any;
      try {
        newClient = await api.createClient(clientData);
        if (!hasStringId(newClient)) {
          throw new Error('Client API returned an empty response.');
        }
      } catch (error) {
        if (!isRecoverableCreateError(error)) {
          throw error;
        }

        if (normalizedEmail) {
          const recoveredClientResult = await recoverClientByEmail(normalizedEmail);
          if (recoveredClientResult) {
            newClient = recoveredClientResult.client;
            setClients(recoveredClientResult.freshClients);
          }
        }

        if (!hasStringId(newClient)) {
          await sleep(200);

          try {
            newClient = await api.createClient(clientData);
            if (!hasStringId(newClient)) {
              throw new Error('Client API returned an empty response.');
            }
          } catch (retryError) {
            if (normalizedEmail) {
              try {
                const recoveredClientResult = await recoverClientByEmail(normalizedEmail);
                if (recoveredClientResult) {
                  newClient = recoveredClientResult.client;
                  setClients(recoveredClientResult.freshClients);
                }
              } catch (recoveryError) {
                console.warn('Client create recovery failed', recoveryError);
              }
            }

            if (!hasStringId(newClient)) {
              throw retryError;
            }
          }
        }
      }

      if (!hasStringId(newClient)) {
        throw new Error('Client API returned an empty response.');
      }
      setClients(prev => {
        const next = [newClient, ...sanitizeClients(prev).filter(c => c.id !== temp.id)];
        setMatters(prevMatters => hydrateMattersWithClients(prevMatters, next));
        return next;
      });
      return newClient;
    } catch (e) {
      console.error("API Error", e);
      setClients(prev => prev.filter(c => c.id !== temp.id));
      throw e;
    }
  };

  const updateClient = async (id: string, data: Partial<Client> & { statusChangeNote?: string }) => {
    const prev = sanitizeClients(clients);
    const { statusChangeNote, ...clientData } = data;
    // Optimistic update
    setClients(prevClients => {
      const next = sanitizeClients(prevClients).map(c => c.id === id ? { ...c, ...clientData } : c);
      setMatters(prevMatters => hydrateMattersWithClients(prevMatters, next));
      return next;
    });
    try {
      const updated = await api.updateClient(id, data);
      if (hasStringId(updated)) {
        setClients(prevClients => {
          const next = sanitizeClients(prevClients).map(c => c.id === id ? { ...c, ...updated } : c);
          setMatters(prevMatters => hydrateMattersWithClients(prevMatters, next));
          return next;
        });
      }
    } catch (e) {
      console.error("API Error (updateClient)", e);
      setClients(prev); // revert
      throw e;
    }
  };

  const addLead = async (leadData: any) => {
    const temp = { ...leadData, id: `l-${Date.now()}` };
    setLeads(prev => [temp, ...sanitizeLeads(prev)]);
    try {
      const newLead = await api.createLead(leadData);
      if (!hasStringId(newLead)) {
        throw new Error('Lead API returned an empty response.');
      }
      setLeads(prev => [newLead, ...sanitizeLeads(prev).filter(l => l.id !== temp.id)]);
    } catch (e) {
      console.error("API Error", e);
      setLeads(prev => sanitizeLeads(prev).filter(l => l.id !== temp.id));
    }
  };

  const updateLead = async (id: string, data: Partial<Lead>) => {
    setLeads(prev => sanitizeLeads(prev).map(l => l.id === id ? { ...l, ...data } : l));
    try {
      const updated = await api.updateLead(id, data);
      if (hasStringId(updated)) {
        setLeads(prev => sanitizeLeads(prev).map(l => l.id === id ? updated : l));
      }
    } catch (e) { console.error("API Error (updateLead)", e); }
  };

  const deleteLead = async (id: string) => {
    const prev = sanitizeLeads(leads);
    setLeads(prev => sanitizeLeads(prev).filter(l => l.id !== id));
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

  const addInvoice = async (invoiceData: any): Promise<Invoice> => {
    const temp = { ...invoiceData, id: `inv-${Date.now()}` };
    setInvoices(prev => [temp, ...prev]);
    try {
      const newInvoice = await api.createInvoice(invoiceData);
      if (!hasStringId(newInvoice)) {
        throw new Error('Invoice API returned an empty response.');
      }
      const hydratedInvoice = hydrateInvoiceClient(normalizeInvoice(newInvoice), new Map(clientsRef.current.map((client) => [client.id, client])));
      setInvoices(prev => [hydratedInvoice, ...prev.filter(i => i.id !== temp.id)]);
      return hydratedInvoice;
    } catch (e) {
      if (isRecoverableCreateError(e)) {
        try {
          const recoveredInvoice = await recoverCreatedInvoice(invoiceData);
          if (recoveredInvoice) {
            setInvoices(prev => [recoveredInvoice, ...prev.filter(i => i.id !== temp.id && i.id !== recoveredInvoice.id)]);
            return recoveredInvoice;
          }
        } catch (recoveryError) {
          console.warn("Invoice create recovery failed", recoveryError);
        }
      }

      console.error("API Error (addInvoice)", e);
      setInvoices(prev => prev.filter(i => i.id !== temp.id));
      throw e;
    }
  };

  const updateInvoice = async (id: string, data: any): Promise<Invoice | null> => {
    const prev = invoices;
    setInvoices(items => items.map(inv => inv.id === id ? { ...inv, ...data } : inv));
    try {
      const updated = await api.updateInvoice(id, data);
      if (updated) {
        const hydratedInvoice = hydrateInvoiceClient(normalizeInvoice(updated), new Map(clientsRef.current.map((client) => [client.id, client])));
        setInvoices(items => items.map(inv => inv.id === id ? { ...inv, ...hydratedInvoice } : inv));
        return hydratedInvoice;
      }
      return null;
    } catch (e) {
      console.error("API Error (updateInvoice)", e);
      setInvoices(prev);
      throw e;
    }
  };

  const deleteInvoice = async (id: string): Promise<void> => {
    const prev = invoices;
    setInvoices(items => items.filter(inv => inv.id !== id));
    try {
      await api.deleteInvoice(id);
    } catch (e) {
      console.error("API Error (deleteInvoice)", e);
      setInvoices(prev);
      throw e;
    }
  };

  const approveInvoice = async (id: string): Promise<Invoice | null> => {
    const prev = invoices;
    setInvoices(items => items.map(inv => inv.id === id ? { ...inv, status: InvoiceStatus.APPROVED } : inv));
    try {
      const updated = await api.approveInvoice(id);
      if (updated) {
        const hydratedInvoice = hydrateInvoiceClient(normalizeInvoice(updated), new Map(clientsRef.current.map((client) => [client.id, client])));
        setInvoices(items => items.map(inv => inv.id === id ? { ...inv, ...hydratedInvoice } : inv));
        return hydratedInvoice;
      }
      return null;
    } catch (e) {
      console.error("API Error (approveInvoice)", e);
      setInvoices(prev);
      throw e;
    }
  };

  const sendInvoice = async (id: string, invoiceHint?: Partial<Invoice>): Promise<Invoice | null> => {
    const prev = invoices;

    try {
      const { resolvedId } = await resolveInvoiceMutationTarget(id, invoiceHint);

      setInvoices(items => items.map(inv => {
        if (inv.id === resolvedId) {
          return { ...inv, status: InvoiceStatus.SENT };
        }
        return inv;
      }));

      const updated = await api.sendInvoice(resolvedId);
      if (updated) {
        const hydratedInvoice = hydrateInvoiceClient(normalizeInvoice(updated), new Map(clientsRef.current.map((client) => [client.id, client])));
        setInvoices(items => items.map(inv => inv.id === resolvedId ? { ...inv, ...hydratedInvoice } : inv));
        return hydratedInvoice;
      }
      return null;
    } catch (e) {
      console.error("API Error (sendInvoice)", e);
      setInvoices(prev);
      throw e;
    }
  };

  const recordInvoicePayment = async (
    id: string,
    data: { amount: number; reference?: string },
    invoiceHint?: Partial<Invoice>
  ): Promise<Invoice | null> => {
    const prev = invoices;

    try {
      const { resolvedId, localInvoice } = await resolveInvoiceMutationTarget(id, invoiceHint);
      const nextAmountPaid = Number(localInvoice?.amountPaid ?? 0) + Number(data.amount ?? 0);
      const invoiceAmount = Number(localInvoice?.amount ?? 0);
      const nextBalance = Math.max(0, invoiceAmount - nextAmountPaid);
      const nextStatus = nextBalance === 0
        ? InvoiceStatus.PAID
        : InvoiceStatus.PARTIALLY_PAID;

      setInvoices(items => items.map(inv => {
        if (inv.id !== resolvedId) return inv;
        return {
          ...inv,
          amountPaid: nextAmountPaid,
          balance: nextBalance,
          status: nextStatus
        };
      }));

      const updated = await api.recordPayment(resolvedId, {
        amount: data.amount,
        reference: data.reference
      });
      if (updated) {
        const hydratedInvoice = hydrateInvoiceClient(normalizeInvoice(updated), new Map(clientsRef.current.map((client) => [client.id, client])));
        setInvoices(items => items.map(inv => inv.id === resolvedId ? { ...inv, ...hydratedInvoice } : inv));
        return hydratedInvoice;
      }

      return null;
    } catch (e) {
      console.error("API Error (recordInvoicePayment)", e);
      setInvoices(prev);
      throw e;
    }
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
      addTimeEntry, addExpense, approveTimeEntry, rejectTimeEntry, approveExpense, rejectExpense, addMessage, markMessageRead, addEvent, updateEvent, deleteEvent, addDocument, updateDocument, deleteDocument, addInvoice, updateInvoice, deleteInvoice, approveInvoice, sendInvoice, recordInvoicePayment, addClient, addLead, updateLead, deleteLead, addTask,
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
