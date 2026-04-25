import React, { useState, useEffect, useCallback } from 'react';
import { Employee, StaffMessage, Client, Matter, Message } from '../types';
import { Mail, Search, Plus, Send, X, ArrowLeft } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { toast } from './Toast';
import { api } from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import { useRef } from 'react';
import { startEmailAccountOAuth } from '../services/emailAccountOAuthService';
import { getCurrentAppReturnPath } from '../services/returnPath';
import { useConfirm } from './ConfirmDialog';

type ConnectedEmailAccount = {
  id: string;
  provider: string;
  emailAddress: string;
  displayName?: string | null;
  isActive: boolean;
  syncEnabled?: boolean;
  lastSyncAt?: string | null;
  syncError?: string | null;
};

type EmailListItem = Message & {
  folder?: string;
  fromAddress?: string;
  toAddresses?: string;
};

const Communications: React.FC = () => {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { messages, addMessage, markMessageRead, clients, matters } = useData();
  const { confirm } = useConfirm();
  const [selectedMsgId, setSelectedMsgId] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'inbox' | 'sent'>('inbox');
  const [showCompose, setShowCompose] = useState(false);
  const [sendingEmail, setSendingEmail] = useState(false);
  const [emailAccounts, setEmailAccounts] = useState<ConnectedEmailAccount[]>([]);
  const [emailAccountsLoading, setEmailAccountsLoading] = useState(false);
  const [selectedEmailAccountId, setSelectedEmailAccountId] = useState<string | null>(null);
  const [syncingMailboxId, setSyncingMailboxId] = useState<string | null>(null);
  const [disconnectingMailboxId, setDisconnectingMailboxId] = useState<string | null>(null);
  const [emailMessages, setEmailMessages] = useState<EmailListItem[]>([]);
  const [emailMessagesLoading, setEmailMessagesLoading] = useState(false);
  const [emailSearch, setEmailSearch] = useState('');
  const [selectedEmailDetail, setSelectedEmailDetail] = useState<any | null>(null);

  const [composeData, setComposeData] = useState({ to: '', subject: '', body: '' });

  // Direct messages state
  const [mode, setMode] = useState<'email' | 'direct' | 'client'>('email');
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [dmMessages, setDmMessages] = useState<StaffMessage[]>([]);
  const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null);
  const [dmInput, setDmInput] = useState('');
  const [dmLoading, setDmLoading] = useState(false);
  const [currentEmployeeId, setCurrentEmployeeId] = useState<string | null>(null);
  const [teamSearch, setTeamSearch] = useState('');
  const [dmAttachments, setDmAttachments] = useState<File[]>([]);
  const dmFileInput = useRef<HTMLInputElement>(null);

  // Client messaging state
  const [selectedClient, setSelectedClient] = useState<Client | null>(null);
  const [clientMessages, setClientMessages] = useState<any[]>([]);
  const [clientLoading, setClientLoading] = useState(false);
  const [clientSending, setClientSending] = useState(false);
  const [clientSearch, setClientSearch] = useState('');
  const [clientCompose, setClientCompose] = useState({ subject: '', body: '', matterId: '' });
  const [clientAttachments, setClientAttachments] = useState<File[]>([]);
  const clientFileInput = useRef<HTMLInputElement>(null);

  const openAttachment = async (att: any) => {
    const url = att?.filePath || att?.url;
    if (!url) return;
    if (url.startsWith('/api/')) {
      const endpoint = url.replace('/api', '');
      try {
        const file = await api.downloadFile(endpoint);
        if (!file?.blob) return;
        const blobUrl = window.URL.createObjectURL(file.blob);
        const link = document.createElement('a');
        link.href = blobUrl;
        link.download = file.filename || att.fileName || att.name || 'attachment';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(blobUrl);
      } catch (error) {
        console.error('Failed to download attachment', error);
        toast.error('Unable to download attachment.');
      }
      return;
    }

    window.open(url, '_blank', 'noreferrer');
  };

  const loadEmailAccounts = async () => {
    setEmailAccountsLoading(true);
    try {
      const accounts = await api.emails.accounts.list();
      const normalized = Array.isArray(accounts) ? accounts as ConnectedEmailAccount[] : [];
      setEmailAccounts(normalized);
    } catch (error) {
      console.error('Failed to load email accounts', error);
      toast.error('Could not load connected mailboxes.');
    } finally {
      setEmailAccountsLoading(false);
    }
  };

  const formatEmailDate = (value?: string | null) => {
    if (!value) return '';
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return '';
    return parsed.toLocaleDateString([], { month: 'short', day: 'numeric' });
  };

  const mapSyncedEmail = (email: any): EmailListItem => {
    const folder = email.folder || email.Folder || 'Inbox';
    const isSent = folder.toLowerCase() === 'sent';
    const bodyText = email.bodyText || email.BodyText || '';
    const fromName = email.fromName || email.FromName || '';
    const fromAddress = email.fromAddress || email.FromAddress || '';
    const toAddresses = email.toAddresses || email.ToAddresses || '';
    return {
      id: email.id || email.Id,
      from: isSent ? 'Me' : (fromName || fromAddress || 'Unknown sender'),
      subject: email.subject || email.Subject || '(No subject)',
      preview: bodyText || (isSent ? `To: ${toAddresses}` : fromAddress),
      date: formatEmailDate(email.receivedAt || email.ReceivedAt || email.sentAt || email.SentAt),
      read: Boolean(email.isRead ?? email.IsRead),
      matterId: email.matterId || email.MatterId || undefined,
      folder,
      fromAddress,
      toAddresses
    };
  };

  const loadSyncedEmails = async (tab: 'inbox' | 'sent' = activeTab) => {
    if (emailAccounts.length === 0) {
      setEmailMessages([]);
      return;
    }

    setEmailMessagesLoading(true);
    try {
      const folder = tab === 'sent' ? 'Sent' : 'Inbox';
      const payload = await api.emails.list({ folder, limit: 100 });
      const next = Array.isArray(payload) ? payload.map(mapSyncedEmail) : [];
      setEmailMessages(next);
      if (selectedMsgId && !next.some(message => message.id === selectedMsgId)) {
        setSelectedMsgId(null);
        setSelectedEmailDetail(null);
      }
    } catch (error) {
      console.error('Failed to load synced emails', error);
      toast.error(error instanceof Error ? `Could not load synced emails: ${error.message}` : 'Could not load synced emails.');
    } finally {
      setEmailMessagesLoading(false);
    }
  };

  useEffect(() => {
    if (mode !== 'email') return;
    void loadEmailAccounts();
  }, [mode]);

  useEffect(() => {
    if (mode !== 'email') return;
    void loadSyncedEmails(activeTab);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, activeTab, emailAccounts.length]);

  useEffect(() => {
    if (emailAccounts.length === 0) {
      setSelectedEmailAccountId(null);
      return;
    }

    if (selectedEmailAccountId && emailAccounts.some(account => account.id === selectedEmailAccountId)) {
      return;
    }

    setSelectedEmailAccountId(emailAccounts[0].id);
  }, [emailAccounts, selectedEmailAccountId]);

  // Load employees when switching to direct messaging
  useEffect(() => {
    if (mode !== 'direct') return;
    const loadEmployees = async () => {
      try {
      const list = await api.getStaffDirectory();
        setEmployees(list || []);
        const self = (list || []).find(e => e.email?.toLowerCase() === (user?.email || '').toLowerCase());
        setCurrentEmployeeId(self?.id || user?.id || null);
      } catch (err) {
        console.error('Failed to load employees for direct messaging', err);
      }
    };
    loadEmployees();
  }, [mode, user]);

  // Load thread when teammate selected
  useEffect(() => {
    if (mode !== 'direct' || !selectedEmployee || !currentEmployeeId) return;
    const loadThread = async () => {
      setDmLoading(true);
      try {
        const thread = await api.staffMessages.thread(selectedEmployee.id);
        setDmMessages(thread || []);

        const unread = (thread || []).filter(m => m.status === 'Unread' && m.recipientId === currentEmployeeId);
        await Promise.all(unread.map(m => api.staffMessages.markRead(m.id).catch(() => null)));
      } catch (err) {
        console.error('Failed to load direct message thread', err);
      } finally {
        setDmLoading(false);
      }
    };
    loadThread();
  }, [mode, selectedEmployee, currentEmployeeId]);

  const loadClientThread = useCallback(async (showLoading = true) => {
    if (!selectedClient) return;
    if (showLoading) setClientLoading(true);
    try {
      const thread = await api.get(`/messages/client?clientId=${encodeURIComponent(selectedClient.id)}`);
      const safeThread = Array.isArray(thread) ? thread : [];
      setClientMessages(safeThread);

      const unread = safeThread.filter((m: any) => !m.read && String(m.senderType || '').toLowerCase() !== 'staff');
      if (unread.length > 0) {
        void Promise.all(unread.map((m: any) => api.post(`/client/messages/${m.id}/read`, {}).catch(() => null)));
      }
    } catch (err) {
      console.error('Failed to load client messages', err);
      setClientMessages([]);
    } finally {
      if (showLoading) setClientLoading(false);
    }
  }, [selectedClient]);

  // Load client thread when a client is selected
  useEffect(() => {
    if (mode !== 'client' || !selectedClient) return;
    void loadClientThread();
  }, [mode, selectedClient, loadClientThread]);

  const handleConnectMailbox = async (provider: 'gmail' | 'outlook') => {
    try {
      await startEmailAccountOAuth(provider, getCurrentAppReturnPath('/#communications'));
    } catch (error) {
      console.error('Failed to initialize mailbox OAuth', error);
      toast.error(error instanceof Error ? error.message : 'Failed to start mailbox connection.');
    }
  };

  const handleSyncMailbox = async (accountId?: string | null) => {
    const targetAccountId = accountId || selectedEmailAccountId;
    if (!targetAccountId) {
      toast.error('Select a connected mailbox first.');
      return;
    }

    setSyncingMailboxId(targetAccountId);
    try {
      const result = await api.emails.accounts.sync(targetAccountId);
      await loadEmailAccounts();
      await loadSyncedEmails(activeTab);
      const created = typeof result?.created === 'number' ? result.created : 0;
      const updated = typeof result?.updated === 'number' ? result.updated : 0;
      toast.success(`Mailbox sync completed. ${created} new, ${updated} updated.`);
    } catch (error) {
      console.error('Mailbox sync error:', error);
      toast.error(error instanceof Error ? `Mailbox sync failed: ${error.message}` : 'Mailbox sync failed. Client messages are unaffected.');
    } finally {
      setSyncingMailboxId(null);
    }
  };

  const handleDisconnectMailbox = async (accountId?: string | null) => {
    const targetAccountId = accountId || selectedEmailAccountId;
    if (!targetAccountId) {
      toast.error('Select a connected mailbox first.');
      return;
    }

    const targetAccount = emailAccounts.find(account => account.id === targetAccountId);
    const label = targetAccount?.emailAddress || 'this mailbox';
    const ok = await confirm({
      title: 'Disconnect mailbox',
      message: `Disconnect ${label}? Synced emails from this mailbox will be removed from JurisFlow.`,
      confirmText: 'Disconnect',
      cancelText: 'Cancel',
      variant: 'danger'
    });
    if (!ok) {
      return;
    }

    setDisconnectingMailboxId(targetAccountId);
    try {
      await api.emails.accounts.disconnect(targetAccountId);
      setEmailAccounts(prev => prev.filter(account => account.id !== targetAccountId));
      if (selectedEmailAccountId === targetAccountId) {
        setSelectedEmailAccountId(null);
      }
      await loadEmailAccounts();
      toast.success('Mailbox disconnected.');
    } catch (error) {
      console.error('Mailbox disconnect error:', error);
      toast.error(error instanceof Error ? error.message : 'Failed to disconnect mailbox.');
    } finally {
      setDisconnectingMailboxId(null);
    }
  };

  const currentEmailMessages = emailAccounts.length > 0 ? emailMessages : messages;
  const selectedMessage = currentEmailMessages.find(m => m.id === selectedMsgId);
  const filteredMessages = currentEmailMessages.filter(m => {
     if (emailSearch.trim()) {
       const term = emailSearch.trim().toLowerCase();
       if (!`${m.from} ${m.subject} ${m.preview}`.toLowerCase().includes(term)) return false;
     }
     if (emailAccounts.length > 0) return true;
     if (activeTab === 'sent') return m.from === 'Me';
     return m.from !== 'Me';
  });

  const filteredTeam = employees.filter(e => {
    if (currentEmployeeId && e.id === currentEmployeeId) return false;
    const term = teamSearch.trim().toLowerCase();
    return !term || `${e.firstName} ${e.lastName} ${e.email}`.toLowerCase().includes(term) || (e.role || '').toLowerCase().includes(term);
  });

  const filteredClients = clients.filter(c => {
    const term = clientSearch.trim().toLowerCase();
    if (!term) return true;
    return `${c.name} ${c.email ?? ''} ${c.company ?? ''}`.toLowerCase().includes(term);
  });

  const clientMatters = selectedClient
    ? matters.filter((m: Matter) =>
        m.client?.id === selectedClient.id ||
        m.clientId === selectedClient.id ||
        (Array.isArray(m.relatedClientIds) && m.relatedClientIds.includes(selectedClient.id)) ||
        (Array.isArray(m.relatedClients) && m.relatedClients.some(client => client.id === selectedClient.id))
      )
    : [];

  const sortedDmMessages = [...dmMessages].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
  const sortedClientMessages = [...clientMessages].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());

  const handleSelectMessage = async (id: string) => {
    setSelectedMsgId(id);
    if (emailAccounts.length === 0) {
      markMessageRead(id);
      return;
    }

    setEmailMessages(prev => prev.map(message => message.id === id ? { ...message, read: true } : message));
    setSelectedEmailDetail(null);
    try {
      const detail = await api.emails.get(id);
      setSelectedEmailDetail(detail);
    } catch (error) {
      console.error('Failed to load email detail', error);
      toast.error('Could not load email body.');
    }
  };

  const handleSend = async (e: React.FormEvent) => {
     e.preventDefault();
     if (sendingEmail) return;
     if (!selectedEmailAccountId) {
       toast.error('Connect and select a mailbox before sending.');
       return;
     }

     setSendingEmail(true);
     try {
       const response = await api.emails.send({
         toAddress: composeData.to.trim(),
         subject: composeData.subject.trim(),
         bodyText: composeData.body,
         emailAccountId: selectedEmailAccountId || undefined
       });

       addMessage({
          id: response?.emailId || `msg${Date.now()}`,
          from: 'Me',
          subject: composeData.subject,
          preview: composeData.body.substring(0, 50) + '...',
          date: new Date().toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}),
          read: true
       });

       toast.success(response?.message || 'Email queued for delivery.');
       setShowCompose(false);
       setComposeData({ to: '', subject: '', body: '' });
       setActiveTab('sent');
       await loadEmailAccounts();
       await loadSyncedEmails('sent');
     } catch (error) {
       console.error('Failed to send email', error);
       toast.error(error instanceof Error ? error.message : 'Could not send email.');
     } finally {
       setSendingEmail(false);
     }
  };

  const handleSendDm = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!selectedEmployee || !dmInput.trim()) return;
    const senderId = currentEmployeeId || user?.id || 'current-user';
    const attachments = await Promise.all(dmAttachments.map(fileToDto));
    try {
      const sent = await api.staffMessages.send({
        recipientId: selectedEmployee.id,
        body: dmInput.trim(),
        ...(attachments.length > 0 ? { attachments } : {})
      });
      const safeMessage: StaffMessage = sent || {
        id: `temp-${Date.now()}`,
        senderId,
        recipientId: selectedEmployee.id,
        body: dmInput.trim(),
        status: 'Unread',
        createdAt: new Date().toISOString(),
        attachments: attachments as any
      };
      setDmMessages(prev => [...prev, safeMessage]);
      setDmInput('');
      setDmAttachments([]);
    } catch (error) {
      console.error('Failed to send direct message', error);
      toast.error('Could not send message');
    }
  };

  const parseClientAttachments = (raw?: string | null) => {
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  };

  const getMessageAttachments = (msg: any) => {
    if (Array.isArray(msg?.attachments)) return msg.attachments;
    return parseClientAttachments(msg?.attachmentsJson);
  };

  const handleSendClientMessage = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!selectedClient) return;
    if (clientSending) return;
    if (!clientCompose.subject.trim() || !clientCompose.body.trim()) return;

    const subject = clientCompose.subject.trim();
    const message = clientCompose.body.trim();
    const matterId = clientCompose.matterId;
    const optimisticId = `pending-client-message-${Date.now()}`;
    const optimisticAttachments = clientAttachments.map(file => ({
      name: file.name,
      fileName: file.name,
      size: file.size,
      mimeType: file.type
    }));

    setClientSending(true);
    setClientMessages(prev => [
      ...prev,
      {
        id: optimisticId,
        clientId: selectedClient.id,
        subject,
        message,
        read: false,
        createdAt: new Date().toISOString(),
        matterId: matterId || undefined,
        attachments: optimisticAttachments,
        senderType: 'Staff',
        senderName: user?.name || 'Firm Staff',
        pending: true
      }
    ]);

    try {
      const attachments = await Promise.all(clientAttachments.map(fileToDto));
      const payload: any = {
        clientId: selectedClient.id,
        subject,
        message
      };
      if (matterId) payload.matterId = matterId;
      if (attachments.length > 0) payload.attachments = attachments;

      const sent = await api.post('/messages/client/send', payload);
      if (sent) {
        setClientMessages(prev => prev.map(msg => msg.id === optimisticId ? sent : msg));
        setClientCompose({ subject: '', body: '', matterId: '' });
        setClientAttachments([]);
        toast.success('Client message sent.', 2500);
        void loadClientThread(false);
      }
    } catch (error) {
      console.error('Failed to send client message', error);
      setClientMessages(prev => prev.filter(msg => msg.id !== optimisticId));
      toast.error(error instanceof Error ? error.message : 'Could not send client message');
    } finally {
      setClientSending(false);
    }
  };

  const fileToDto = (file: File) => {
    return new Promise<{ fileName: string; size: number; type: string; data: string }>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve({ fileName: file.name, size: file.size, type: file.type, data: reader.result as string });
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
  };

  const hasSelectedThread =
    mode === 'email' ? Boolean(selectedMessage) :
      mode === 'direct' ? Boolean(selectedEmployee) :
        Boolean(selectedClient);

  const clearSelectedThread = () => {
    setSelectedMsgId(null);
    setSelectedEmployee(null);
    setSelectedClient(null);
  };

  const selectedEmailAccount = emailAccounts.find(account => account.id === selectedEmailAccountId) || null;

  return (
    <div className="h-full flex flex-col lg:flex-row bg-white relative">
      {/* Sidebar List */}
      <div className={`${hasSelectedThread ? 'hidden lg:flex' : 'flex'} w-full lg:w-96 border-r border-gray-200 flex-col min-h-0`}>
        <div className="p-4 border-b border-gray-200 overflow-hidden">
          <div className="flex flex-wrap items-start gap-2 mb-3">
            <h2 className="text-lg font-bold text-slate-800 min-w-0 flex-1 truncate">{t('comms_title')}</h2>
            <div className="flex items-center gap-1.5 flex-wrap justify-end ml-auto">
              <button
                onClick={() => setMode('email')}
                className={`px-2.5 py-1.5 text-xs font-semibold rounded-lg ${mode === 'email' ? 'bg-slate-900 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
              >
                Email
              </button>
              <button
                onClick={() => setMode('direct')}
                className={`px-2.5 py-1.5 text-xs font-semibold rounded-lg ${mode === 'direct' ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
              >
                Direct
              </button>
              <button
                onClick={() => setMode('client')}
                className={`px-2.5 py-1.5 text-xs font-semibold rounded-lg ${mode === 'client' ? 'bg-emerald-600 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
              >
                Clients
              </button>
              {mode === 'email' && (
                <button
                  onClick={() => setShowCompose(true)}
                  className="p-2 bg-slate-800 text-white rounded-lg shadow hover:bg-slate-900 transition-colors shrink-0"
                  aria-label="Compose email"
                >
                    <Plus className="w-5 h-5" />
                </button>
              )}
            </div>
          </div>

          {mode === 'email' ? (
            <>
              <div className="flex gap-2 mb-4 bg-gray-100 p-1 rounded-lg">
                <button onClick={() => setActiveTab('inbox')} className={`flex-1 text-xs font-bold py-1.5 rounded ${activeTab === 'inbox' ? 'bg-white shadow text-slate-900' : 'text-gray-500'}`}>{t('inbox')}</button>
                <button onClick={() => setActiveTab('sent')} className={`flex-1 text-xs font-bold py-1.5 rounded ${activeTab === 'sent' ? 'bg-white shadow text-slate-900' : 'text-gray-500'}`}>{t('sent')}</button>
              </div>
              <div className="relative">
                <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
                <input
                  type="text"
                  value={emailSearch}
                  onChange={(e) => setEmailSearch(e.target.value)}
                  placeholder="Search messages..."
                  className="w-full pl-9 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-100 focus:border-primary-400 transition-all text-slate-800"
                />
              </div>
            </>
          ) : mode === 'direct' ? (
            <div className="relative">
              <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
              <input
                type="text"
                value={teamSearch}
                onChange={(e) => setTeamSearch(e.target.value)}
                placeholder="Search teammates..."
                className="w-full pl-9 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-100 focus:border-primary-400 transition-all text-slate-800"
              />
            </div>
          ) : (
            <div className="relative">
              <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
              <input
                type="text"
                value={clientSearch}
                onChange={(e) => setClientSearch(e.target.value)}
                placeholder="Search clients..."
                className="w-full pl-9 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-100 focus:border-primary-400 transition-all text-slate-800"
              />
            </div>
          )}
        </div>
        <div className="flex-1 overflow-y-auto">
          {mode === 'email' ? (
            <>
              {emailMessagesLoading && (
                  <div className="p-4 text-center text-gray-400 text-sm">Loading synced emails...</div>
              )}
              {!emailMessagesLoading && filteredMessages.length === 0 && (
                  <div className="p-8 text-center text-gray-400 text-sm">No messages found.</div>
              )}
              {!emailMessagesLoading && filteredMessages.map(msg => (
                <div 
                  key={msg.id} 
                  onClick={() => handleSelectMessage(msg.id)}
                  className={`p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors ${selectedMsgId === msg.id ? 'bg-primary-50 border-primary-200' : ''} ${!msg.read ? 'bg-blue-50/30' : ''}`}
                >
                  <div className="flex justify-between items-start mb-1">
                    <span className={`text-sm ${!msg.read ? 'font-bold text-slate-900' : 'font-semibold text-slate-600'}`}>{msg.from}</span>
                    <span className="shrink-0 text-xs text-gray-400">{msg.date}</span>
                  </div>
                  <h4 className={`text-sm mb-1 truncate ${!msg.read ? 'font-bold text-slate-800' : 'font-medium text-slate-700'}`}>{msg.subject}</h4>
                  <p className="text-xs text-gray-500 line-clamp-2">{msg.preview}</p>
                </div>
              ))}
            </>
          ) : mode === 'direct' ? (
            <div>
              {filteredTeam.length === 0 && (
                <div className="p-8 text-center text-gray-400 text-sm">No teammates found.</div>
              )}
              {filteredTeam.map(emp => {
                const isActive = selectedEmployee?.id === emp.id;
                return (
                  <div
                    key={emp.id}
                    onClick={() => setSelectedEmployee(emp)}
                    className={`p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors ${isActive ? 'bg-blue-50 border-blue-200' : ''}`}
                  >
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-full bg-gradient-to-br from-blue-500 to-indigo-600 text-white font-semibold flex items-center justify-center">
                        {emp.firstName[0]}{emp.lastName[0]}
                      </div>
                      <div className="flex-1">
                        <div className="flex items-center gap-2">
                          <p className="text-sm font-semibold text-slate-800">{emp.firstName} {emp.lastName}</p>
                          <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full">{emp.role}</span>
                        </div>
                        <p className="text-xs text-gray-500">{emp.email}</p>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <div>
              {filteredClients.length === 0 && (
                <div className="p-8 text-center text-gray-400 text-sm">No clients found.</div>
              )}
              {filteredClients.map(clientItem => {
                const isActive = selectedClient?.id === clientItem.id;
                return (
                  <div
                    key={clientItem.id}
                    onClick={() => setSelectedClient(clientItem)}
                    className={`p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors ${isActive ? 'bg-emerald-50 border-emerald-200' : ''}`}
                  >
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-full bg-gradient-to-br from-emerald-500 to-teal-600 text-white font-semibold flex items-center justify-center">
                        {clientItem.name?.charAt(0) || 'C'}
                      </div>
                      <div className="flex-1">
                        <p className="text-sm font-semibold text-slate-800">{clientItem.name}</p>
                        <p className="text-xs text-gray-500">{clientItem.email}</p>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Message View */}
      <div className={`${hasSelectedThread ? 'flex' : 'hidden lg:flex'} flex-1 min-w-0 flex-col bg-gray-50/50`}>
        {mode === 'email' ? (
          <>
            {selectedMessage ? (
              <div className="flex-1 flex flex-col bg-white m-3 lg:m-4 rounded-xl shadow-sm border border-gray-200 overflow-hidden">
                <div className="p-4 lg:p-6 border-b border-gray-100">
                  <div className="flex justify-between items-start mb-4">
                    <div className="flex min-w-0 items-start gap-3">
                      <button
                        type="button"
                        onClick={clearSelectedThread}
                        className="mt-0.5 rounded-lg p-1.5 text-gray-500 hover:bg-gray-100 lg:hidden"
                        aria-label="Back to messages"
                      >
                        <ArrowLeft className="w-4 h-4" />
                      </button>
                      <h2 className="min-w-0 text-lg lg:text-xl font-bold text-slate-900 break-words">{selectedMessage.subject}</h2>
                    </div>
                    <span className="text-xs font-medium bg-gray-100 text-gray-600 px-2 py-1 rounded">{selectedMessage.date}</span>
                  </div>
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-full bg-indigo-100 text-indigo-600 flex items-center justify-center font-bold">
                        {selectedMessage.from.charAt(0)}
                    </div>
                    <div>
                        <p className="text-sm font-bold text-slate-800">{selectedMessage.from}</p>
                        <p className="text-xs text-gray-500">
                          {emailAccounts.length > 0
                            ? (activeTab === 'sent' ? `to ${(selectedMessage as EmailListItem).toAddresses || 'recipient'}` : (selectedMessage as EmailListItem).fromAddress || 'to Me')
                            : 'to Me'}
                        </p>
                    </div>
                  </div>
                </div>
                <div className="p-4 lg:p-8 text-sm text-slate-700 leading-relaxed whitespace-pre-wrap overflow-y-auto">
                    {selectedEmailDetail?.bodyText || selectedEmailDetail?.BodyText || selectedMessage.preview || 'No email body available.'}
                </div>
              </div>
            ) : (
              <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
                <Mail className="w-16 h-16 mb-4 opacity-10" />
                <h3 className="text-lg font-semibold text-gray-500">Select a message</h3>
                <p className="text-sm">Securely communicate with clients and staff.</p>
                <div className="mt-8 p-4 bg-blue-50 border border-blue-200 rounded-lg max-w-sm text-center">
                    {emailAccountsLoading ? (
                      <p className="text-xs text-blue-700">Loading connected mailboxes...</p>
                    ) : emailAccounts.length > 0 ? (
                      <>
                        <p className="text-xs text-blue-800 font-bold mb-2">Connected mailbox</p>
                        <p className="text-xs text-blue-700 mb-2">
                          {selectedEmailAccount?.displayName || selectedEmailAccount?.emailAddress || `${emailAccounts.length} mailbox connected`}
                        </p>
                        <p className="text-[11px] text-gray-500 mb-3">
                          Email is sent as the connected Gmail or Outlook account, not from platform SMTP.
                        </p>
                        <div className="flex flex-wrap justify-center gap-2">
                          <button
                            onClick={() => handleSyncMailbox()}
                            disabled={syncingMailboxId === selectedEmailAccountId || disconnectingMailboxId === selectedEmailAccountId}
                            className="px-4 py-2 bg-blue-600 text-white text-xs font-bold rounded shadow-sm hover:bg-blue-700 disabled:opacity-60"
                          >
                            {syncingMailboxId === selectedEmailAccountId ? 'Syncing...' : 'Sync mailbox'}
                          </button>
                          <button
                            onClick={() => setShowCompose(true)}
                            disabled={disconnectingMailboxId === selectedEmailAccountId}
                            className="px-4 py-2 bg-white border border-gray-200 text-xs font-bold text-gray-700 rounded shadow-sm hover:bg-gray-50 disabled:opacity-60"
                          >
                            Compose
                          </button>
                          <button
                            onClick={() => handleDisconnectMailbox()}
                            disabled={disconnectingMailboxId === selectedEmailAccountId || syncingMailboxId === selectedEmailAccountId}
                            className="px-4 py-2 bg-white border border-red-200 text-xs font-bold text-red-600 rounded shadow-sm hover:bg-red-50 disabled:opacity-60"
                          >
                            {disconnectingMailboxId === selectedEmailAccountId ? 'Disconnecting...' : 'Disconnect'}
                          </button>
                        </div>
                      </>
                    ) : (
                      <>
                        <p className="text-xs text-blue-800 font-bold mb-2">Connect a mailbox</p>
                        <p className="text-xs text-blue-700 mb-3">Each staff member can connect Gmail or Outlook and send from their own mailbox.</p>
                        <div className="flex flex-wrap justify-center gap-2">
                          <button onClick={() => handleConnectMailbox('gmail')} className="px-4 py-2 bg-red-500 text-white text-xs font-bold rounded shadow-sm hover:bg-red-600">
                            Connect Gmail
                          </button>
                          <button onClick={() => handleConnectMailbox('outlook')} className="px-4 py-2 bg-blue-600 text-white text-xs font-bold rounded shadow-sm hover:bg-blue-700">
                            Connect Outlook
                          </button>
                        </div>
                      </>
                    )}
                </div>
              </div>
            )}
          </>
        ) : mode === 'direct' ? (
          <div className="flex-1 flex flex-col bg-white m-3 lg:m-4 rounded-xl shadow-sm border border-gray-200 min-h-0">
            {selectedEmployee ? (
              <>
                <div className="p-4 border-b border-gray-100 flex items-center justify-between gap-3">
                  <div className="min-w-0 flex items-start gap-3">
                    <button
                      type="button"
                      onClick={clearSelectedThread}
                      className="mt-0.5 rounded-lg p-1.5 text-gray-500 hover:bg-gray-100 lg:hidden"
                      aria-label="Back to teammates"
                    >
                      <ArrowLeft className="w-4 h-4" />
                    </button>
                    <div className="min-w-0">
                    <p className="text-xs uppercase text-gray-500">Direct chat</p>
                    <h3 className="text-lg font-semibold text-slate-900">{selectedEmployee.firstName} {selectedEmployee.lastName}</h3>
                    <p className="text-xs text-gray-500">{selectedEmployee.email}</p>
                    </div>
                  </div>
                  <span className="text-xs bg-gray-100 text-gray-600 px-2 py-1 rounded">{selectedEmployee.role}</span>
                </div>
                <div className="flex-1 overflow-y-auto p-4 space-y-3">
                  {dmLoading && <div className="text-xs text-gray-500">Loading messages...</div>}
                  {!dmLoading && sortedDmMessages.length === 0 && (
                    <div className="text-sm text-gray-500">Start the conversation.</div>
                  )}
                  {sortedDmMessages.map(msg => {
                    const isOwn = msg.senderId === (currentEmployeeId || user?.id);
                    const attachments = (msg as any)?.attachments || [];
                    const parsedAttachments = (msg as any)?.attachmentsJson ? JSON.parse((msg as any).attachmentsJson) : attachments;
                    return (
                      <div key={msg.id} className={`flex ${isOwn ? 'justify-end' : 'justify-start'}`}>
                        <div className={`max-w-[75%] rounded-2xl px-3 py-2 text-sm shadow space-y-2 ${isOwn ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-800'}`}>
                          <p className="whitespace-pre-wrap">{msg.body}</p>
                          {Array.isArray(parsedAttachments) && parsedAttachments.length > 0 && (
                            <div className={`rounded-lg ${isOwn ? 'bg-blue-500/30' : 'bg-white/60'} p-2 text-xs space-y-1`}>
                              {parsedAttachments.map((att: any, idx: number) => (
                                <button
                                  key={idx}
                                  type="button"
                                  onClick={() => openAttachment(att)}
                                  className="flex items-center gap-2 underline"
                                >
                                  📎 {att.fileName || 'attachment'}
                                </button>
                              ))}
                            </div>
                          )}
                          <span className="block text-[10px] mt-1 opacity-70">{new Date(msg.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                        </div>
                      </div>
                    );
                  })}
                </div>
                <form onSubmit={handleSendDm} className="border-t border-gray-100 p-4 flex flex-col gap-3 bg-gray-50">
                  <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
                    <textarea
                      value={dmInput}
                      onChange={(e) => setDmInput(e.target.value)}
                      placeholder="Message your teammate..."
                      className="flex-1 border border-gray-200 rounded-lg p-3 text-sm focus:ring-2 focus:ring-blue-200 focus:border-blue-400 resize-none h-20"
                    />
                    <input type="file" className="hidden" multiple ref={dmFileInput} onChange={(e) => {
                      if (e.target.files) setDmAttachments([...dmAttachments, ...Array.from(e.target.files)]);
                    }} />
                    <button
                      type="button"
                      onClick={() => dmFileInput.current?.click()}
                      className="h-10 px-3 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-white"
                    >
                      Attach
                    </button>
                    <button
                      type="submit"
                      disabled={!dmInput.trim()}
                      className="h-10 px-4 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-60 flex items-center gap-2"
                    >
                      <Send className="w-4 h-4" /> Send
                    </button>
                  </div>
                  {dmAttachments.length > 0 && (
                    <div className="flex flex-wrap gap-2 text-xs text-gray-600">
                      {dmAttachments.map((f, idx) => (
                        <span key={idx} className="px-2 py-1 bg-white border border-gray-200 rounded-lg flex items-center gap-2">
                          {f.name}
                          <button type="button" onClick={() => setDmAttachments(dmAttachments.filter((_, i) => i !== idx))} className="text-red-500">×</button>
                        </span>
                      ))}
                    </div>
                  )}
                </form>
              </>
            ) : (
              <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
                <Mail className="w-16 h-16 mb-4 opacity-10" />
                <h3 className="text-lg font-semibold text-gray-500">Select a teammate</h3>
                <p className="text-sm">Start a direct, private conversation.</p>
              </div>
            )}
          </div>
        ) : (
          <div className="flex-1 flex flex-col bg-white m-3 lg:m-4 rounded-xl shadow-sm border border-gray-200 min-h-0">
            {selectedClient ? (
              <>
                <div className="p-4 border-b border-gray-100 flex items-center justify-between gap-3">
                  <div className="min-w-0 flex items-start gap-3">
                    <button
                      type="button"
                      onClick={clearSelectedThread}
                      className="mt-0.5 rounded-lg p-1.5 text-gray-500 hover:bg-gray-100 lg:hidden"
                      aria-label="Back to clients"
                    >
                      <ArrowLeft className="w-4 h-4" />
                    </button>
                    <div className="min-w-0">
                    <p className="text-xs uppercase text-gray-500">Client thread</p>
                    <h3 className="text-lg font-semibold text-slate-900">{selectedClient.name}</h3>
                    <p className="text-xs text-gray-500">{selectedClient.email}</p>
                    </div>
                  </div>
                  <span className="text-xs bg-gray-100 text-gray-600 px-2 py-1 rounded">{selectedClient.status || 'Active'}</span>
                </div>
                <div className="flex-1 overflow-y-auto p-4 space-y-3">
                  {clientLoading && <div className="text-xs text-gray-500">Loading messages...</div>}
                  {!clientLoading && sortedClientMessages.length === 0 && (
                    <div className="text-sm text-gray-500">No messages yet.</div>
                  )}
                  {sortedClientMessages.map((msg: any) => {
                    const isStaff = String(msg.senderType || 'Client').toLowerCase() === 'staff';
                    const attachments = getMessageAttachments(msg);
                    return (
                      <div key={msg.id} className={`flex ${isStaff ? 'justify-end' : 'justify-start'}`}>
                        <div className={`max-w-[75%] rounded-2xl px-3 py-2 text-sm shadow space-y-2 ${isStaff ? 'bg-emerald-600 text-white' : 'bg-gray-100 text-gray-800'} ${msg.pending ? 'opacity-75' : ''}`}>
                          <div className="text-[11px] uppercase opacity-80">{msg.subject || 'Message'}</div>
                          <p className="whitespace-pre-wrap">{msg.message}</p>
                          {attachments.length > 0 && (
                            <div className={`rounded-lg ${isStaff ? 'bg-emerald-500/30' : 'bg-white/60'} p-2 text-xs space-y-1`}>
                              {attachments.map((att: any, idx: number) => (
                                <button
                                  key={idx}
                                  type="button"
                                  onClick={() => openAttachment(att)}
                                  className="flex items-center gap-2 underline"
                                >
                                  Attachment: {att.fileName || att.name || 'attachment'}
                                </button>
                              ))}
                            </div>
                          )}
                          <span className="block text-[10px] mt-1 opacity-70">
                            {msg.pending ? 'Sending...' : new Date(msg.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          </span>
                        </div>
                      </div>
                    );
                  })}
                </div>
                <form onSubmit={handleSendClientMessage} className="border-t border-gray-100 p-4 space-y-3 bg-gray-50">
                  <div className="grid gap-3">
                    <input
                      value={clientCompose.subject}
                      onChange={(e) => setClientCompose(prev => ({ ...prev, subject: e.target.value }))}
                      placeholder="Subject"
                      className="w-full border border-gray-200 rounded-lg p-2 text-sm focus:ring-2 focus:ring-emerald-200 focus:border-emerald-400"
                    />
                    <select
                      value={clientCompose.matterId}
                      onChange={(e) => setClientCompose(prev => ({ ...prev, matterId: e.target.value }))}
                      className="w-full border border-gray-200 rounded-lg p-2 text-sm focus:ring-2 focus:ring-emerald-200 focus:border-emerald-400"
                    >
                      <option value="">Select matter (optional)</option>
                      {clientMatters.map(m => (
                        <option key={m.id} value={m.id}>
                          {m.caseNumber} - {m.name}
                        </option>
                      ))}
                    </select>
                    <textarea
                      value={clientCompose.body}
                      onChange={(e) => setClientCompose(prev => ({ ...prev, body: e.target.value }))}
                      placeholder="Write a message..."
                      className="w-full border border-gray-200 rounded-lg p-3 text-sm focus:ring-2 focus:ring-emerald-200 focus:border-emerald-400 resize-none h-24"
                    />
                  </div>

                  <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
                    <div className="flex items-center gap-2 flex-wrap">
                      <input type="file" className="hidden" multiple ref={clientFileInput} onChange={(e) => {
                        if (e.target.files) setClientAttachments([...clientAttachments, ...Array.from(e.target.files)]);
                      }} />
                      <button
                        type="button"
                        onClick={() => clientFileInput.current?.click()}
                        className="h-9 px-3 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-white"
                      >
                        Attach
                      </button>
                      {clientAttachments.length > 0 && (
                        <span className="text-xs text-gray-500">{clientAttachments.length} file(s)</span>
                      )}
                    </div>
                    <button
                      type="submit"
                      disabled={clientSending || !clientCompose.subject.trim() || !clientCompose.body.trim()}
                      className="h-9 px-4 bg-emerald-600 text-white rounded-lg hover:bg-emerald-700 disabled:opacity-60 flex items-center gap-2"
                    >
                      <Send className="w-4 h-4" /> {clientSending ? 'Sending...' : 'Send'}
                    </button>
                  </div>
                </form>
              </>
            ) : (
              <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
                <Mail className="w-16 h-16 mb-4 opacity-10" />
                <h3 className="text-lg font-semibold text-gray-500">Select a client</h3>
                <p className="text-sm">Send secure messages directly from JurisFlow.</p>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Compose Modal */}
      {showCompose && mode === 'email' && (
          <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
             <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden animate-in fade-in zoom-in duration-200 flex flex-col h-[min(600px,calc(100dvh-2rem))]">
                 <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                    <h3 className="font-bold text-lg text-slate-800 flex items-center gap-2"><Send className="w-4 h-4"/> {t('compose')}</h3>
                    <button onClick={() => setShowCompose(false)} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5"/></button>
                 </div>
                 <form onSubmit={handleSend} className="flex-1 flex flex-col">
                    <div className="p-6 space-y-4 flex-1 overflow-y-auto">
                        <div>
                           <label className="block text-xs font-bold text-gray-500 uppercase mb-1">From</label>
                           {emailAccounts.length > 1 ? (
                             <select
                               required
                               className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-primary-400 focus:ring-1 focus:ring-primary-400"
                               value={selectedEmailAccountId || ''}
                               onChange={e => setSelectedEmailAccountId(e.target.value)}
                             >
                               <option value="" disabled>Select a mailbox</option>
                               {emailAccounts.map(account => (
                                 <option key={account.id} value={account.id}>
                                   {account.provider} - {account.emailAddress}
                                 </option>
                               ))}
                             </select>
                           ) : selectedEmailAccount ? (
                             <div className="w-full border border-gray-200 bg-gray-50 text-slate-700 rounded-lg p-2.5 text-sm">
                               {selectedEmailAccount.provider} - {selectedEmailAccount.emailAddress}
                             </div>
                           ) : (
                             <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                               <p className="text-xs font-semibold text-amber-900">No mailbox connected</p>
                               <p className="text-xs text-amber-800 mt-1">Connect Gmail or Outlook first. Mail is sent as the connected staff mailbox.</p>
                               <div className="flex flex-wrap gap-2 mt-3">
                                 <button type="button" onClick={() => handleConnectMailbox('gmail')} className="px-3 py-1.5 text-xs font-semibold text-white bg-red-500 rounded hover:bg-red-600">
                                   Connect Gmail
                                 </button>
                                 <button type="button" onClick={() => handleConnectMailbox('outlook')} className="px-3 py-1.5 text-xs font-semibold text-white bg-blue-600 rounded hover:bg-blue-700">
                                   Connect Outlook
                                 </button>
                               </div>
                             </div>
                           )}
                        </div>
                        <div>
                           <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('to')}</label>
                           <input required type="email" placeholder="client@example.com" className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-primary-400 focus:ring-1 focus:ring-primary-400" value={composeData.to} onChange={e => setComposeData({...composeData, to: e.target.value})} />
                        </div>
                        <div>
                           <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('subject')}</label>
                           <input required type="text" placeholder="Case Update..." className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-primary-400 focus:ring-1 focus:ring-primary-400" value={composeData.subject} onChange={e => setComposeData({...composeData, subject: e.target.value})} />
                        </div>
                        <div className="flex-1 h-64">
                           <label className="block text-xs font-bold text-gray-500 uppercase mb-1">{t('message')}</label>
                           <textarea required className="w-full h-full border border-gray-300 bg-white text-slate-900 rounded-lg p-4 text-sm outline-none focus:border-primary-400 focus:ring-1 focus:ring-primary-400 resize-none" value={composeData.body} onChange={e => setComposeData({...composeData, body: e.target.value})}></textarea>
                        </div>
                    </div>
                    <div className="p-4 border-t border-gray-100 flex justify-between items-center bg-gray-50">
                       <button type="button" onClick={() => window.open(`mailto:${composeData.to}`)} className="text-xs text-gray-500 hover:text-primary-600 underline">{t('open_external')}</button>
                       <div className="flex gap-3">
                           <button type="button" onClick={() => setShowCompose(false)} disabled={sendingEmail} className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg disabled:opacity-60">{t('cancel')}</button>
                           <button type="submit" disabled={sendingEmail || !selectedEmailAccountId} className="px-6 py-2 text-sm font-bold text-white bg-slate-800 hover:bg-slate-900 rounded-lg flex items-center gap-2 disabled:opacity-60">
                               <Send className="w-4 h-4" /> {sendingEmail ? 'Sending...' : t('send')}
                           </button>
                       </div>
                    </div>
                 </form>
             </div>
          </div>
      )}
    </div>
  );
};

export default Communications;
