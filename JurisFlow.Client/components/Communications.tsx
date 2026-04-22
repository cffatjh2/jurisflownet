import React, { useState, useEffect } from 'react';
import { Message, Employee, StaffMessage, Client, Matter } from '../types';
import { Mail, Search, Plus, Send, X } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { gmailService } from '../services/gmailService';
import { toast } from './Toast';
import { api } from '../services/api';
import { useAuth } from '../contexts/AuthContext';
import { useRef } from 'react';
import { clearOAuthTokens, getOAuthAccessToken } from '../services/oauthSecurity';
import { getCurrentAppReturnPath } from '../services/returnPath';

const Communications: React.FC = () => {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { messages, addMessage, markMessageRead, clients, matters } = useData();
  const [selectedMsgId, setSelectedMsgId] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'inbox' | 'sent'>('inbox');
  const [showCompose, setShowCompose] = useState(false);
  const [isGmailConnected, setIsGmailConnected] = useState(false);
  const [gmailAccessToken, setGmailAccessToken] = useState<string | null>(
    typeof window !== 'undefined' ? getOAuthAccessToken('gmail') : null
  );

  const [composeData, setComposeData] = useState({ to: '', subject: '', body: '' });

  const selectedMessage = messages.find(m => m.id === selectedMsgId);

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

  useEffect(() => {
    setIsGmailConnected(!!gmailAccessToken);
  }, [gmailAccessToken]);

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

  // Load client thread when a client is selected
  useEffect(() => {
    if (mode !== 'client' || !selectedClient) return;
    const loadClientThread = async () => {
      setClientLoading(true);
      try {
        const thread = await api.get(`/messages/client?clientId=${encodeURIComponent(selectedClient.id)}`);
        setClientMessages(Array.isArray(thread) ? thread : []);

        const unread = (thread || []).filter((m: any) => !m.read && m.senderType !== 'Staff');
        await Promise.all(unread.map((m: any) => api.post(`/client/messages/${m.id}/read`, {})));
      } catch (err) {
        console.error('Failed to load client messages', err);
        setClientMessages([]);
      } finally {
        setClientLoading(false);
      }
    };
    loadClientThread();
  }, [mode, selectedClient]);

  const handleGmailConnect = async () => {
    try {
      const authUrl = await gmailService.getAuthUrl({
        target: 'gmail',
        returnPath: getCurrentAppReturnPath('/#communications')
      });
      if (!authUrl) {
        toast.error('Google OAuth is not configured.');
        return;
      }
      window.location.href = authUrl;
    } catch (error) {
      console.error('Failed to initialize Gmail OAuth', error);
      toast.error('Failed to start Gmail connection. Please try again.');
    }
  };

  const handleGmailSync = async () => {
    if (!gmailAccessToken) return;
    
    try {
      const gmailMessages = await gmailService.getMessages(gmailAccessToken, 20);
      gmailMessages.forEach(gmailMsg => {
        const parsed = gmailService.parseMessage(gmailMsg);
        addMessage({
          id: gmailMsg.id,
          from: parsed.from,
          subject: parsed.subject,
          preview: parsed.preview,
          date: parsed.date,
          read: false
        });
      });
      toast.success('Gmail messages synced successfully!');
    } catch (error) {
      console.error('Gmail sync error:', error);
      toast.error('Failed to sync Gmail messages. Please reconnect.');
      clearOAuthTokens('gmail');
      setGmailAccessToken(null);
    }
  };

  const filteredMessages = messages.filter(m => {
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
    ? matters.filter((m: Matter) => m.client?.id === selectedClient.id)
    : [];

  const sortedDmMessages = [...dmMessages].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
  const sortedClientMessages = [...clientMessages].sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());

  const handleSelectMessage = (id: string) => {
    setSelectedMsgId(id);
    markMessageRead(id);
  };

  const handleSend = (e: React.FormEvent) => {
     e.preventDefault();
     addMessage({
        id: `msg${Date.now()}`,
        from: 'Me',
        subject: composeData.subject,
        preview: composeData.body.substring(0, 50) + '...',
        date: new Date().toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'}),
        read: true
     });
     
     setShowCompose(false);
     setComposeData({ to: '', subject: '', body: '' });
     setActiveTab('sent');
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
    if (!clientCompose.subject.trim() || !clientCompose.body.trim()) return;

    const attachments = await Promise.all(clientAttachments.map(fileToDto));
    const payload: any = {
      clientId: selectedClient.id,
      subject: clientCompose.subject.trim(),
      message: clientCompose.body.trim()
    };
    if (clientCompose.matterId) payload.matterId = clientCompose.matterId;
    if (attachments.length > 0) payload.attachments = attachments;

    try {
      const sent = await api.post('/messages/client/send', payload);
      if (sent) {
        setClientMessages(prev => [sent, ...prev]);
        setClientCompose({ subject: '', body: '', matterId: '' });
        setClientAttachments([]);
      }
    } catch (error) {
      console.error('Failed to send client message', error);
      toast.error('Could not send client message');
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

  return (
    <div className="h-full flex bg-white relative">
      {/* Sidebar List */}
      <div className="w-96 border-r border-gray-200 flex flex-col">
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
                <input type="text" placeholder="Search messages..." className="w-full pl-9 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-100 focus:border-primary-400 transition-all text-slate-800" />
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
              {filteredMessages.length === 0 && (
                  <div className="p-8 text-center text-gray-400 text-sm">No messages found.</div>
              )}
              {filteredMessages.map(msg => (
                <div 
                  key={msg.id} 
                  onClick={() => handleSelectMessage(msg.id)}
                  className={`p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors ${selectedMsgId === msg.id ? 'bg-primary-50 border-primary-200' : ''} ${!msg.read ? 'bg-blue-50/30' : ''}`}
                >
                  <div className="flex justify-between items-start mb-1">
                    <span className={`text-sm ${!msg.read ? 'font-bold text-slate-900' : 'font-semibold text-slate-600'}`}>{msg.from}</span>
                    <span className="text-xs text-gray-400">{msg.date}</span>
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
      <div className="flex-1 flex flex-col bg-gray-50/50">
        {mode === 'email' ? (
          <>
            {selectedMessage ? (
              <div className="flex-1 flex flex-col bg-white m-4 rounded-xl shadow-sm border border-gray-200 overflow-hidden">
                <div className="p-6 border-b border-gray-100">
                  <div className="flex justify-between items-start mb-4">
                    <h2 className="text-xl font-bold text-slate-900">{selectedMessage.subject}</h2>
                    <span className="text-xs font-medium bg-gray-100 text-gray-600 px-2 py-1 rounded">{selectedMessage.date}</span>
                  </div>
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-full bg-indigo-100 text-indigo-600 flex items-center justify-center font-bold">
                        {selectedMessage.from.charAt(0)}
                    </div>
                    <div>
                        <p className="text-sm font-bold text-slate-800">{selectedMessage.from}</p>
                        <p className="text-xs text-gray-500">to Me</p>
                    </div>
                  </div>
                </div>
                <div className="p-8 text-sm text-slate-700 leading-relaxed whitespace-pre-wrap">
                    {selectedMessage.preview}
                    <br/><br/>
                    Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.
                </div>
              </div>
            ) : (
              <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
                <Mail className="w-16 h-16 mb-4 opacity-10" />
                <h3 className="text-lg font-semibold text-gray-500">Select a message</h3>
                <p className="text-sm">Securely communicate with clients and staff.</p>
                <div className="mt-8 p-4 bg-blue-50 border border-blue-200 rounded-lg max-w-sm text-center">
                    {isGmailConnected ? (
                      <>
                        <p className="text-xs text-blue-800 font-bold mb-2">Gmail Connected</p>
                        <p className="text-xs text-blue-700 mb-3">Your Gmail account is connected. Click below to sync messages.</p>
                        <button onClick={handleGmailSync} className="px-4 py-2 bg-blue-600 text-white text-xs font-bold rounded shadow-sm hover:bg-blue-700">
                          Sync Gmail Messages
                        </button>
                        <button 
                          onClick={() => {
                            clearOAuthTokens('gmail');
                            setGmailAccessToken(null);
                            setIsGmailConnected(false);
                          }}
                          className="mt-2 px-4 py-2 bg-white border border-gray-200 text-xs font-bold text-gray-700 rounded shadow-sm hover:bg-gray-50 block w-full"
                        >
                          Disconnect
                        </button>
                      </>
                    ) : (
                      <>
                        <p className="text-xs text-blue-800 font-bold mb-2">Gmail Integration</p>
                        <p className="text-xs text-blue-700 mb-3">Connect your Gmail account to sync emails directly into JurisFlow.</p>
                        <button onClick={handleGmailConnect} className="px-4 py-2 bg-blue-600 text-white text-xs font-bold rounded shadow-sm hover:bg-blue-700">
                          Connect Gmail
                        </button>
                        <p className="text-xs text-gray-500 mt-2">Note: Requires Google Cloud Console OAuth2 setup</p>
                      </>
                    )}
                </div>
              </div>
            )}
          </>
        ) : mode === 'direct' ? (
          <div className="flex-1 flex flex-col bg-white m-4 rounded-xl shadow-sm border border-gray-200">
            {selectedEmployee ? (
              <>
                <div className="p-4 border-b border-gray-100 flex items-center justify-between">
                  <div>
                    <p className="text-xs uppercase text-gray-500">Direct chat</p>
                    <h3 className="text-lg font-semibold text-slate-900">{selectedEmployee.firstName} {selectedEmployee.lastName}</h3>
                    <p className="text-xs text-gray-500">{selectedEmployee.email}</p>
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
                  <div className="flex items-center gap-2">
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
          <div className="flex-1 flex flex-col bg-white m-4 rounded-xl shadow-sm border border-gray-200">
            {selectedClient ? (
              <>
                <div className="p-4 border-b border-gray-100 flex items-center justify-between">
                  <div>
                    <p className="text-xs uppercase text-gray-500">Client thread</p>
                    <h3 className="text-lg font-semibold text-slate-900">{selectedClient.name}</h3>
                    <p className="text-xs text-gray-500">{selectedClient.email}</p>
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
                        <div className={`max-w-[75%] rounded-2xl px-3 py-2 text-sm shadow space-y-2 ${isStaff ? 'bg-emerald-600 text-white' : 'bg-gray-100 text-gray-800'}`}>
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
                            {new Date(msg.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
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

                  <div className="flex items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
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
                      disabled={!clientCompose.subject.trim() || !clientCompose.body.trim()}
                      className="h-9 px-4 bg-emerald-600 text-white rounded-lg hover:bg-emerald-700 disabled:opacity-60 flex items-center gap-2"
                    >
                      <Send className="w-4 h-4" /> Send
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
             <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden animate-in fade-in zoom-in duration-200 flex flex-col h-[600px]">
                 <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                    <h3 className="font-bold text-lg text-slate-800 flex items-center gap-2"><Send className="w-4 h-4"/> {t('compose')}</h3>
                    <button onClick={() => setShowCompose(false)} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5"/></button>
                 </div>
                 <form onSubmit={handleSend} className="flex-1 flex flex-col">
                    <div className="p-6 space-y-4 flex-1 overflow-y-auto">
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
                           <button type="button" onClick={() => setShowCompose(false)} className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg">{t('cancel')}</button>
                           <button type="submit" className="px-6 py-2 text-sm font-bold text-white bg-slate-800 hover:bg-slate-900 rounded-lg flex items-center gap-2">
                               <Send className="w-4 h-4" /> {t('send')}
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
