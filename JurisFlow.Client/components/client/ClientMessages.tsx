import React, { useState, useEffect, useRef } from 'react';
import { Send, Plus, Mail, Paperclip, X, Search } from '../Icons';
import { gmailService, GmailMessage } from '../../services/gmailService';
import { toast } from '../Toast';
import { clientApi } from '../../services/clientApi';
import { clearOAuthTokens, getOAuthAccessToken } from '../../services/oauthSecurity';
import { getCurrentAppReturnPath } from '../../services/returnPath';

interface ClientMessage {
  id: string;
  matterId?: string;
  matter?: { id: string; name: string; caseNumber: string };
  subject: string;
  message: string;
  read: boolean;
  createdAt: string;
  attachmentsJson?: string | null;
  attachments?: any[];
  source?: 'internal' | 'gmail';
  senderType?: 'Client' | 'Staff';
  senderName?: string;
}

const ClientMessages: React.FC = () => {
  const [messages, setMessages] = useState<ClientMessage[]>([]);
  const [selectedMessage, setSelectedMessage] = useState<ClientMessage | null>(null);
  const [showCompose, setShowCompose] = useState(false);
  const [loading, setLoading] = useState(true);
  const [matters, setMatters] = useState<any[]>([]);
  const [activeTab, setActiveTab] = useState<'internal' | 'gmail'>('internal');
  const [gmailMessages, setGmailMessages] = useState<any[]>([]);
  const [gmailLoading, setGmailLoading] = useState(false);
  const [isGmailConnected, setIsGmailConnected] = useState(false);
  const [gmailAccessToken, setGmailAccessToken] = useState<string | null>(
    getOAuthAccessToken('gmail')
  );
  const [searchQuery, setSearchQuery] = useState('');
  const [showUnreadOnly, setShowUnreadOnly] = useState(false);

  const [composeData, setComposeData] = useState({
    matterId: '',
    subject: '',
    message: ''
  });

  const openAttachment = async (att: any) => {
    const url = att?.filePath || att?.url;
    if (!url) return;
    if (url.startsWith('/api/')) {
      const endpoint = url.replace('/api', '');
      try {
        const res = await clientApi.fetch(endpoint);
        if (!res.ok) {
          throw new Error('Download failed');
        }
        const blob = await res.blob();
        const blobUrl = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = blobUrl;
        link.download = att.fileName || att.name || 'attachment';
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
  const [gmailComposeData, setGmailComposeData] = useState({
    to: '',
    subject: '',
    body: ''
  });
  const [showGmailCompose, setShowGmailCompose] = useState(false);
  const [attachedFiles, setAttachedFiles] = useState<File[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setIsGmailConnected(!!gmailAccessToken);
  }, [gmailAccessToken]);

  const unreadCount = messages.filter(msg => !msg.read).length;

  const formatDate = (value: string) => {
    if (!value) return '';
    return new Date(value).toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  const parseAttachments = (raw?: string | null) => {
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  };

  const handleSelectMessage = async (msg: ClientMessage) => {
    setSelectedMessage(msg);
    if (msg.source !== 'internal' || msg.read) return;
    try {
      await clientApi.fetchJson(`/messages/${msg.id}/read`, { method: 'POST' });
      setMessages(prev => prev.map(m => m.id === msg.id ? { ...m, read: true } : m));
      setSelectedMessage(prev => prev ? { ...prev, read: true } : prev);
    } catch (error) {
      console.error('Error marking message as read:', error);
    }
  };

  useEffect(() => {
    const loadData = async () => {
      try {
        const [messagesData, mattersData] = await Promise.all([
          clientApi.fetchJson('/messages'),
          clientApi.fetchJson('/matters')
        ]);

        const normalizedMessages = Array.isArray(messagesData)
          ? messagesData.map((msg: ClientMessage) => ({ ...msg, source: 'internal' as const }))
          : [];

        setMessages(normalizedMessages);
        setMatters(Array.isArray(mattersData) ? mattersData : []);
      } catch (error) {
        console.error('Error loading messages:', error);
        toast.error('Failed to load messages.');
      } finally {
        setLoading(false);
      }
    };
    
    loadData();
  }, []);

  const handleGmailConnect = async () => {
    try {
      const authUrl = await gmailService.getAuthUrl({
        target: 'gmail',
        returnPath: getCurrentAppReturnPath('/client')
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

  const loadGmailMessages = async () => {
    if (!gmailAccessToken) return;
    
    setGmailLoading(true);
    try {
      const gmailMsgs = await gmailService.getMessages(gmailAccessToken, 50);
      const parsed = gmailMsgs.map(msg => gmailService.parseMessage(msg));
      setGmailMessages(parsed);
    } catch (error) {
      console.error('Error loading Gmail:', error);
      toast.error('Failed to load Gmail messages. Please reconnect.');
      clearOAuthTokens('gmail');
      setGmailAccessToken(null);
      setIsGmailConnected(false);
    } finally {
      setGmailLoading(false);
    }
  };

  const handleGmailSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!gmailAccessToken) return;
    
    try {
      await gmailService.sendEmail(
        gmailAccessToken,
        gmailComposeData.to,
        gmailComposeData.subject,
        gmailComposeData.body
      );
      toast.success('Email sent successfully!');
      setShowGmailCompose(false);
      setGmailComposeData({ to: '', subject: '', body: '' });
      loadGmailMessages();
    } catch (error) {
      console.error('Error sending email:', error);
      toast.error('Failed to send email. Please try again.');
    }
  };

  const handleFileAttach = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setAttachedFiles([...attachedFiles, ...Array.from(e.target.files)]);
    }
  };

  const handleRemoveFile = (index: number) => {
    setAttachedFiles(attachedFiles.filter((_, i) => i !== index));
  };

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      // Convert files to base64 for JSON transmission
      const attachments = await Promise.all(
        attachedFiles.map(async (file) => {
          return new Promise<{name: string; size: number; type: string; data: string}>((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve({
              name: file.name,
              size: file.size,
              type: file.type,
              data: reader.result as string
            });
            reader.onerror = reject;
            reader.readAsDataURL(file);
          });
        })
      );
      
      const payload = {
        ...composeData,
        ...(attachments.length > 0 && { attachments })
      };

      const newMessage = await clientApi.fetchJson('/messages', {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      setMessages([{ ...newMessage, source: 'internal', senderType: 'Client' }, ...messages]);
      setShowCompose(false);
      setComposeData({ matterId: '', subject: '', message: '' });
      setAttachedFiles([]);
    } catch (error) {
      console.error('Error sending message:', error);
      toast.error('Failed to send message. Please try again.');
    }
  };

  const normalizedQuery = searchQuery.trim().toLowerCase();
  const filteredInternalMessages = messages.filter(msg => {
    if (showUnreadOnly && msg.read) return false;
    if (!normalizedQuery) return true;
    const haystack = [
      msg.subject,
      msg.message,
      msg.matter?.caseNumber,
      msg.matter?.name
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
    return haystack.includes(normalizedQuery);
  });

  const filteredGmailMessages = gmailMessages.filter((msg: any) => {
    if (!normalizedQuery) return true;
    const haystack = [
      msg.subject,
      msg.preview,
      msg.from
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
    return haystack.includes(normalizedQuery);
  });

  const selectedAttachments = selectedMessage?.source === 'internal'
    ? (Array.isArray(selectedMessage.attachments) ? selectedMessage.attachments : parseAttachments(selectedMessage.attachmentsJson))
    : [];

  const handleReply = () => {
    if (!selectedMessage) return;
    if (selectedMessage.source === 'gmail') {
      setShowGmailCompose(true);
      setGmailComposeData(prev => ({
        ...prev,
        subject: selectedMessage.subject ? `Re: ${selectedMessage.subject}` : prev.subject
      }));
      return;
    }
    setComposeData({
      matterId: selectedMessage.matterId || '',
      subject: selectedMessage.subject.startsWith('Re:') ? selectedMessage.subject : `Re: ${selectedMessage.subject}`,
      message: ''
    });
    setShowCompose(true);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  return (
    <div className="h-full flex bg-white relative">
      {/* Sidebar */}
      <div className="w-96 border-r border-gray-200 flex flex-col">
        <div className="p-4 border-b border-gray-200">
          <div className="flex justify-between items-center mb-4">
            <div className="flex items-center gap-2">
              <h2 className="text-lg font-bold text-slate-800">Messages</h2>
              {unreadCount > 0 && (
                <span className="px-2 py-0.5 rounded-full bg-blue-100 text-blue-700 text-xs font-bold">
                  {unreadCount} unread
                </span>
              )}
            </div>
            <button 
              onClick={() => {
                if (activeTab === 'internal') setShowCompose(true);
                else setShowGmailCompose(true);
              }}
              className="p-2 bg-blue-600 text-white rounded-lg shadow hover:bg-blue-700 transition-colors"
            >
              <Plus className="w-5 h-5" />
            </button>
          </div>
          
          {/* Tab Switcher */}
          <div className="flex gap-2 bg-gray-100 p-1 rounded-lg">
            <button
              onClick={() => setActiveTab('internal')}
              className={`flex-1 px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                activeTab === 'internal'
                  ? 'bg-white text-slate-900 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              Internal
            </button>
            <button
              onClick={() => {
                setActiveTab('gmail');
                if (isGmailConnected && gmailMessages.length === 0) {
                  loadGmailMessages();
                }
              }}
              className={`flex-1 px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                activeTab === 'gmail'
                  ? 'bg-white text-slate-900 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              Gmail
            </button>
          </div>

          <div className="mt-3 flex items-center gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg">
            <Search className="w-4 h-4 text-gray-400" />
            <input
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={activeTab === 'gmail' ? 'Search Gmail...' : 'Search messages...'}
              className="w-full text-sm text-slate-700 placeholder:text-gray-400 outline-none"
            />
          </div>

          {activeTab === 'internal' && (
            <label className="mt-3 flex items-center gap-2 text-xs text-gray-500">
              <input
                type="checkbox"
                className="rounded border-gray-300"
                checked={showUnreadOnly}
                onChange={(e) => setShowUnreadOnly(e.target.checked)}
              />
              Show unread only
            </label>
          )}
          
          {/* Gmail Connect Button */}
          {activeTab === 'gmail' && !isGmailConnected && (
            <button
              onClick={handleGmailConnect}
              className="w-full mt-3 px-4 py-2 bg-red-600 text-white rounded-lg text-sm font-medium hover:bg-red-700 transition-colors"
            >
              Connect Gmail
            </button>
          )}
          
          {activeTab === 'gmail' && isGmailConnected && (
            <button
              onClick={loadGmailMessages}
              disabled={gmailLoading}
              className="w-full mt-3 px-4 py-2 bg-gray-100 text-gray-700 rounded-lg text-sm font-medium hover:bg-gray-200 transition-colors disabled:opacity-50"
            >
              {gmailLoading ? 'Loading...' : 'Refresh Gmail'}
            </button>
          )}
        </div>

        <div className="flex-1 overflow-y-auto">
          {activeTab === 'internal' ? (
            filteredInternalMessages.length === 0 ? (
              <div className="p-8 text-center text-gray-400 text-sm">No messages</div>
            ) : (
              filteredInternalMessages.map(msg => (
                <div
                  key={msg.id}
                  onClick={() => handleSelectMessage(msg)}
                  className={`p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors ${
                    selectedMessage?.id === msg.id ? 'bg-blue-50' : ''
                  }`}
                >
                  <div className="flex justify-between items-start mb-1">
                    <span className={`text-sm ${!msg.read ? 'font-bold text-slate-900' : 'font-semibold text-slate-600'}`}>
                      {msg.subject}
                    </span>
                    <span className="text-xs text-gray-400">{formatDate(msg.createdAt)}</span>
                  </div>
                  <div className="flex items-center justify-between text-xs text-gray-500 mb-1">
                    <span>{msg.senderType === 'Staff' ? 'Firm' : 'You'}</span>
                    {msg.matter && (
                      <span className="text-blue-600">Case: {msg.matter.caseNumber}</span>
                    )}
                  </div>
                  <p className="text-xs text-gray-500 line-clamp-2">{msg.message}</p>
                </div>
              ))
            )
          ) : (
            gmailLoading ? (
              <div className="p-8 text-center text-gray-400 text-sm">Loading Gmail...</div>
            ) : filteredGmailMessages.length === 0 ? (
              <div className="p-8 text-center text-gray-400 text-sm">
                {isGmailConnected ? 'No Gmail messages' : 'Connect Gmail to view emails'}
              </div>
            ) : (
              filteredGmailMessages.map((msg, idx) => (
                <div
                  key={idx}
                  onClick={() => setSelectedMessage({
                    id: `gmail-${idx}`,
                    subject: msg.subject,
                    message: msg.preview,
                    read: true,
                    createdAt: msg.date,
                    source: 'gmail'
                  })}
                  className="p-4 border-b border-gray-100 cursor-pointer hover:bg-gray-50 transition-colors"
                >
                  <div className="flex justify-between items-start mb-1">
                    <span className="text-sm font-semibold text-slate-600">{msg.subject}</span>
                    <span className="text-xs text-gray-400">{msg.date}</span>
                  </div>
                  <p className="text-xs text-gray-500 line-clamp-2">{msg.from}</p>
                  <p className="text-xs text-gray-400 mt-1 line-clamp-1">{msg.preview}</p>
                </div>
              ))
            )
          )}
        </div>
      </div>

      {/* Message View */}
      <div className="flex-1 flex flex-col bg-gray-50">
        {selectedMessage ? (
            <div className="flex-1 flex flex-col bg-white m-4 rounded-xl shadow-sm border border-gray-200 overflow-hidden">
              <div className="p-6 border-b border-gray-100 flex items-start justify-between gap-4">
                <div>
                  <h2 className="text-xl font-bold text-slate-900">{selectedMessage.subject}</h2>
                  <div className="text-xs text-gray-500 mt-2">
                    {formatDate(selectedMessage.createdAt)}
                    {selectedMessage.matter && ` - Case: ${selectedMessage.matter.caseNumber}`}
                    {selectedMessage.source === 'gmail' && ' - Gmail'}
                  </div>
                  {selectedMessage.source === 'internal' && (
                    <div className="text-xs text-gray-500 mt-1">
                      From {selectedMessage.senderName || (selectedMessage.senderType === 'Staff' ? 'Legal Team' : 'You')}
                    </div>
                  )}
                </div>
              {selectedMessage.source === 'gmail' ? (
                <button
                  onClick={() => setShowGmailCompose(true)}
                  className="px-3 py-2 text-xs font-semibold text-red-600 bg-red-50 rounded-lg hover:bg-red-100"
                >
                  Compose Email
                </button>
              ) : (
                <button
                  onClick={handleReply}
                  className="px-3 py-2 text-xs font-semibold text-blue-600 bg-blue-50 rounded-lg hover:bg-blue-100"
                >
                  Reply
                </button>
              )}
            </div>
            <div className="p-8 text-sm text-slate-700 leading-relaxed whitespace-pre-wrap flex-1 overflow-y-auto">
              {selectedMessage.message}
              {selectedAttachments.length > 0 && (
                <div className="mt-6 space-y-2">
                  <div className="text-xs font-bold text-gray-500 uppercase">Attachments</div>
                  {selectedAttachments.map((att: any, idx: number) => (
                    <button
                      key={`${att.filePath || att.url || att.fileName || att.name}-${idx}`}
                      type="button"
                      onClick={() => openAttachment(att)}
                      className="flex items-center justify-between gap-3 px-3 py-2 bg-gray-50 border border-gray-200 rounded-lg text-sm text-blue-700 hover:bg-gray-100"
                    >
                      <span className="truncate">{att.fileName || att.name || 'attachment'}</span>
                      {att.size ? (
                        <span className="text-xs text-gray-500">{Math.round(att.size / 1024)} KB</span>
                      ) : null}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
            <Mail className="w-16 h-16 mb-4 opacity-10" />
            <h3 className="text-lg font-semibold text-gray-500">Select a message</h3>
          </div>
        )}
      </div>

      {/* Compose Modal */}
      {showCompose && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden animate-in fade-in zoom-in duration-200 flex flex-col h-[600px]">
            <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <h3 className="font-bold text-lg text-slate-800 flex items-center gap-2">
                <Send className="w-4 h-4"/> Compose Message
              </h3>
              <button onClick={() => setShowCompose(false)} className="text-gray-400 hover:text-gray-600">
                <span className="text-2xl">&times;</span>
              </button>
            </div>
            <form onSubmit={handleSend} className="flex-1 flex flex-col">
              <div className="p-6 space-y-4 flex-1 overflow-y-auto">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Related Case (Optional)</label>
                  <select
                    value={composeData.matterId}
                    onChange={e => setComposeData({...composeData, matterId: e.target.value})}
                    className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  >
                    <option value="">-- Select a case --</option>
                    {matters.map(m => (
                      <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Subject</label>
                  <input
                    required
                    type="text"
                    placeholder="Message subject..."
                    className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                    value={composeData.subject}
                    onChange={e => setComposeData({...composeData, subject: e.target.value})}
                  />
                </div>
                <div className="flex-1 h-64">
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Message</label>
                  <textarea
                    required
                    className="w-full h-full border border-gray-300 bg-white text-slate-900 rounded-lg p-4 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400 resize-none"
                    value={composeData.message}
                    onChange={e => setComposeData({...composeData, message: e.target.value})}
                    placeholder="Type your message here..."
                  ></textarea>
                </div>
                
                {/* File Attachments */}
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Attachments</label>
                  <div className="flex items-center gap-2">
                    <input
                      type="file"
                      ref={fileInputRef}
                      onChange={handleFileAttach}
                      multiple
                      className="hidden"
                    />
                    <button
                      type="button"
                      onClick={() => fileInputRef.current?.click()}
                      className="flex items-center gap-2 px-3 py-2 border border-gray-300 rounded-lg text-sm text-gray-700 hover:bg-gray-50"
                    >
                      <Paperclip className="w-4 h-4" />
                      Attach Files
                    </button>
                    {attachedFiles.length > 0 && (
                      <span className="text-xs text-gray-600">{attachedFiles.length} file(s) attached</span>
                    )}
                  </div>
                  {attachedFiles.length > 0 && (
                    <div className="mt-2 space-y-1">
                      {attachedFiles.map((file, index) => (
                        <div key={index} className="flex items-center justify-between p-2 bg-gray-50 rounded-lg text-sm">
                          <span className="text-gray-700 truncate flex-1">{file.name}</span>
                          <button
                            type="button"
                            onClick={() => handleRemoveFile(index)}
                            className="ml-2 text-red-600 hover:text-red-800"
                          >
                            <X className="w-4 h-4" />
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
              <div className="p-4 border-t border-gray-100 flex justify-end gap-3 bg-gray-50">
                <button
                  type="button"
                  onClick={() => setShowCompose(false)}
                  className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-6 py-2 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-lg flex items-center gap-2"
                >
                  <Send className="w-4 h-4" /> Send Message
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Gmail Compose Modal */}
      {showGmailCompose && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden animate-in fade-in zoom-in duration-200 flex flex-col h-[600px]">
            <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
              <h3 className="font-bold text-lg text-slate-800 flex items-center gap-2">
                <Mail className="w-4 h-4"/> Compose Email (Gmail)
              </h3>
              <button onClick={() => setShowGmailCompose(false)} className="text-gray-400 hover:text-gray-600">
                <span className="text-2xl">&times;</span>
              </button>
            </div>
            <form onSubmit={handleGmailSend} className="flex-1 flex flex-col">
              <div className="p-6 space-y-4 flex-1 overflow-y-auto">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">To</label>
                  <input
                    required
                    type="email"
                    placeholder="recipient@example.com"
                    className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                    value={gmailComposeData.to}
                    onChange={e => setGmailComposeData({...gmailComposeData, to: e.target.value})}
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Subject</label>
                  <input
                    required
                    type="text"
                    placeholder="Email subject..."
                    className="w-full border border-gray-300 bg-white text-slate-900 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                    value={gmailComposeData.subject}
                    onChange={e => setGmailComposeData({...gmailComposeData, subject: e.target.value})}
                  />
                </div>
                <div className="flex-1 h-64">
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Message</label>
                  <textarea
                    required
                    className="w-full h-full border border-gray-300 bg-white text-slate-900 rounded-lg p-4 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400 resize-none"
                    value={gmailComposeData.body}
                    onChange={e => setGmailComposeData({...gmailComposeData, body: e.target.value})}
                    placeholder="Type your email here..."
                  ></textarea>
                </div>
              </div>
              <div className="p-4 border-t border-gray-100 flex justify-end gap-3 bg-gray-50">
                <button
                  type="button"
                  onClick={() => setShowGmailCompose(false)}
                  className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="px-6 py-2 text-sm font-bold text-white bg-red-600 hover:bg-red-700 rounded-lg flex items-center gap-2"
                >
                  <Send className="w-4 h-4" /> Send Email
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default ClientMessages;

