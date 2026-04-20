import React, { useEffect, useMemo, useState } from 'react';
import { Lead, LeadStatus, LeadSource, PracticeArea, Client } from '../types';
import { Users, ChevronRight, Search, CheckSquare, X, Scale, Edit, Filter, Plus } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { api } from '../services/api';
import { toast } from './Toast';

const CRM: React.FC = () => {
   const [view, setView] = React.useState<'pipeline' | 'clients'>('pipeline');
   const { t, formatCurrency } = useTranslation();
   const { clients, leads, crmReadModelsLoading, crmReadModelsReady, refreshCrmReadModels, addLead, updateLead, deleteLead, updateClient, addClient, setClientPortalPassword } = useData();
   const [highlightClientId, setHighlightClientId] = useState<string | null>(null);
   const [highlightLeadId, setHighlightLeadId] = useState<string | null>(null);
   const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
   const [clientSearch, setClientSearch] = useState('');
   const [editingClient, setEditingClient] = useState<Client | null>(null);
   const [clientForm, setClientForm] = useState<Partial<Client>>({});
   const [isSavingClient, setIsSavingClient] = useState(false);
   const [portalPassword, setPortalPassword] = useState('');
   const [statusHistory, setStatusHistory] = useState<any[]>([]);
   const [statusHistoryLoading, setStatusHistoryLoading] = useState(false);
   const [statusChangeNote, setStatusChangeNote] = useState('');
   const [showCreateClientModal, setShowCreateClientModal] = useState(false);
   const [isCreatingClient, setIsCreatingClient] = useState(false);
   const [createClientError, setCreateClientError] = useState('');
   const initialClientForm = {
      name: '',
      email: '',
      phone: '',
      company: '',
      clientNumber: '',
      type: 'Individual' as Client['type'],
      status: 'Active' as Client['status'],
      portalEnabled: false,
      password: ''
   };
   const [newClientForm, setNewClientForm] = useState(initialClientForm);

   const [showModal, setShowModal] = useState(false);
   const [showConflictModal, setShowConflictModal] = useState(false);
   const [newLeadName, setNewLeadName] = useState('');
   const [newLeadVal, setNewLeadVal] = useState<string | number>('');
   const [newLeadSource, setNewLeadSource] = useState<LeadSource>(LeadSource.Referral);
   const [newLeadPracticeArea, setNewLeadPracticeArea] = useState<PracticeArea>(PracticeArea.CivilLitigation);

   // Conflict Search State
   const [conflictQuery, setConflictQuery] = useState('');
   const [conflictResults, setConflictResults] = useState<any[] | null>(null);

   // Lead pipeline stages (simplified for clean UI)
   const pipelineStages: { key: LeadStatus; label: string; color: string }[] = [
      { key: LeadStatus.New, label: 'New', color: 'bg-blue-500' },
      { key: LeadStatus.Contacted, label: 'Contacted', color: 'bg-indigo-500' },
      { key: LeadStatus.Scheduled, label: 'Scheduled', color: 'bg-purple-500' },
      { key: LeadStatus.Consulted, label: 'Consulted', color: 'bg-violet-500' },
      { key: LeadStatus.Proposal, label: 'Proposal', color: 'bg-amber-500' },
      { key: LeadStatus.Retained, label: 'Retained', color: 'bg-green-500' },
      { key: LeadStatus.Lost, label: 'Lost', color: 'bg-gray-400' }
   ];

   // Deep-link from Command Palette
   useEffect(() => {
      const targetId = localStorage.getItem('cmd_target_client');
      if (!targetId) return;
      setView('clients');
      setHighlightClientId(targetId);
      localStorage.removeItem('cmd_target_client');
   }, []);

   useEffect(() => {
      const targetId = localStorage.getItem('cmd_target_lead');
      if (!targetId) return;
      setView('pipeline');
      setHighlightLeadId(targetId);
      localStorage.removeItem('cmd_target_lead');
   }, []);

   useEffect(() => {
      if (!highlightLeadId) return;
      const timer = setTimeout(() => setHighlightLeadId(null), 4000);
      return () => clearTimeout(timer);
   }, [highlightLeadId]);

   useEffect(() => {
      if (crmReadModelsReady || crmReadModelsLoading) return;
      void refreshCrmReadModels().catch(error => {
         console.error('Failed to hydrate CRM read models', error);
      });
   }, [crmReadModelsReady, crmReadModelsLoading]);

   const moveLead = (lead: Lead, direction: 'prev' | 'next') => {
      const idx = pipelineStages.findIndex(s => s.key === lead.status);
      if (idx === -1) return;
      const targetIdx = direction === 'prev' ? Math.max(0, idx - 1) : Math.min(pipelineStages.length - 1, idx + 1);
      const nextStatus = pipelineStages[targetIdx].key;
      void updateLead(lead.id, { status: nextStatus }).catch(error => {
         console.error('Failed to update lead status', error);
         toast.error('Failed to update lead status.');
      });
   };

   const handleAddDeal = async (e: React.FormEvent) => {
      e.preventDefault();
      if (!newLeadName) return;

      try {
         await addLead({
         name: newLeadName,
         source: newLeadSource,
         estimatedValue: parseFloat(String(newLeadVal)) || 0,
         status: LeadStatus.New,
         practiceArea: newLeadPracticeArea
         });
         setShowModal(false);
         setNewLeadName('');
         setNewLeadVal('');
         toast.success('Lead created successfully.');
      } catch (error) {
         console.error('Failed to create lead', error);
         toast.error('Failed to create lead.');
      }
   };

   const [isSearching, setIsSearching] = useState(false);
   const safeClients = useMemo(() => clients.filter((client): client is Client => !!client && typeof client.id === 'string'), [clients]);
   const safeLeads = useMemo(() => leads.filter((lead): lead is Lead => !!lead && typeof lead.id === 'string' && !lead.isArchived), [leads]);
   const isCrmSyncing = crmReadModelsLoading || !crmReadModelsReady;

   // Derived client stats
   const activeCount = useMemo(() => safeClients.filter(c => (c.status || 'Active') === 'Active').length, [safeClients]);
   const inactiveCount = useMemo(() => safeClients.filter(c => (c.status || 'Active') === 'Inactive').length, [safeClients]);
   const filteredClients = useMemo(() => {
      const query = clientSearch.trim().toLowerCase();
      return safeClients.filter(c => {
         const normalizedStatus = c.status || 'Active';
         if (statusFilter === 'active' && normalizedStatus !== 'Active') return false;
         if (statusFilter === 'inactive' && normalizedStatus !== 'Inactive') return false;
         if (!query) return true;
         const haystack = [
            c.name,
            c.email,
            c.phone,
            c.company,
            c.clientNumber
         ]
            .filter(Boolean)
            .join(' ')
            .toLowerCase();
         return haystack.includes(query);
      });
   }, [safeClients, statusFilter, clientSearch]);

   const performConflictCheck = async (e: React.FormEvent) => {
      e.preventDefault();
      const normalizedQuery = conflictQuery.trim();
      if (normalizedQuery.length < 3) {
         toast.error('Please enter at least 3 characters.');
         return;
      }

      setIsSearching(true);
      setConflictResults(null);

      try {
         const data = await api.crmConflictCheck(normalizedQuery);
         setConflictResults(data);
      } catch (err) {
         console.error(err);
         toast.error('Failed to perform conflict check.');
      } finally {
         setIsSearching(false);
      }
   };

   const generateTempPassword = () => {
      const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%';
      let out = '';
      for (let i = 0; i < 12; i += 1) {
         out += chars[Math.floor(Math.random() * chars.length)];
      }
      return out;
   };

   const resetNewClientForm = () => {
      setNewClientForm({ ...initialClientForm });
      setCreateClientError('');
   };

   const handleCreateClient = async (e: React.FormEvent) => {
      e.preventDefault();
      setCreateClientError('');

      const name = newClientForm.name.trim();
      const email = newClientForm.email.trim();
      const password = newClientForm.password.trim();
      const shouldEnablePortal = newClientForm.portalEnabled;

      if (!name) {
         setCreateClientError('Name is required.');
         return;
      }
      if (!email) {
         setCreateClientError('Email is required.');
         return;
      }
      if (shouldEnablePortal && !password) {
         setCreateClientError('Set a password to enable portal access.');
         return;
      }

      setIsCreatingClient(true);
      let clientCreated = false;
      try {
         const payload: any = {
            name,
            email,
            phone: newClientForm.phone || undefined,
            company: newClientForm.company || undefined,
            clientNumber: newClientForm.clientNumber || undefined,
            type: newClientForm.type,
            status: newClientForm.status
         };
         const createdClient = await addClient(payload);
         clientCreated = true;
         if (shouldEnablePortal) {
            await setClientPortalPassword(createdClient.id, password);
            toast.success('Client created and portal access enabled.');
         } else {
            toast.success('Client created successfully.');
         }
         setShowCreateClientModal(false);
         resetNewClientForm();
      } catch (error) {
         console.error('Failed to create client', error);
         toast.error(clientCreated ? 'Client created, but portal setup failed.' : 'Failed to create client.');
      } finally {
         setIsCreatingClient(false);
      }
   };

   const loadStatusHistory = async (clientId: string) => {
      setStatusHistoryLoading(true);
      try {
         const history = await api.getClientStatusHistory(clientId);
         setStatusHistory(history || []);
      } catch (error) {
         console.error('Failed to load client status history', error);
         setStatusHistory([]);
      } finally {
         setStatusHistoryLoading(false);
      }
   };

   const openEditClient = (client: Client) => {
      setEditingClient(client);
      setClientForm({
         name: client.name,
         email: client.email,
         phone: client.phone,
         company: client.company,
         type: client.type,
         status: client.status,
         portalEnabled: client.portalEnabled ?? false
      });
      setPortalPassword('');
      setStatusChangeNote('');
   };

   useEffect(() => {
      if (!editingClient) return;
      loadStatusHistory(editingClient.id);
   }, [editingClient]);

   const handleSaveClient = async () => {
      if (!editingClient) return;
      const portalEnabled = !!clientForm.portalEnabled;
      const requiresPortalPassword = portalEnabled && !editingClient.portalEnabled;
      const normalizedPortalPassword = portalPassword.trim();
      const shouldRotatePortalPassword = normalizedPortalPassword.length > 0;
      const nextStatus = clientForm.status || editingClient.status;
      const statusChanged = nextStatus !== editingClient.status;
      if (requiresPortalPassword && !normalizedPortalPassword) {
         toast.error('Set a password to enable portal access.');
         return;
      }
      setIsSavingClient(true);
      try {
         const payload: any = { ...clientForm };
         if (requiresPortalPassword) {
            delete payload.portalEnabled;
         }
         if (statusChanged && statusChangeNote.trim()) {
            payload.statusChangeNote = statusChangeNote.trim();
         }
         await updateClient(editingClient.id, payload);
         if (requiresPortalPassword || shouldRotatePortalPassword) {
            await setClientPortalPassword(editingClient.id, normalizedPortalPassword);
         }
         await loadStatusHistory(editingClient.id);
         if (requiresPortalPassword) {
            toast.success('Client updated and portal access enabled.');
         } else if (shouldRotatePortalPassword) {
            toast.success('Client updated and portal password changed.');
         } else {
            toast.success('Client updated successfully.');
         }
         setEditingClient(null);
         setPortalPassword('');
         setStatusChangeNote('');
      } catch {
         // errors are already logged in context
         toast.error('Failed to update client.');
      } finally {
         setIsSavingClient(false);
      }
   };

   return (
      <div className="h-full flex flex-col bg-gray-50/50">
         <div className="px-6 py-4 flex justify-between items-center bg-white border-b border-gray-200">
            <div>
               <h1 className="text-2xl font-bold text-slate-800">{t('crm_title')}</h1>
               <p className="text-sm text-gray-500 mt-1">{t('crm_subtitle')}</p>
               {isCrmSyncing && (
                  <p className="text-xs text-indigo-600 mt-2">CRM records are syncing in the background.</p>
               )}
            </div>
            <div className="flex gap-4">
               {/* Conflict Check Button */}
               <button
                  onClick={() => { setConflictResults(null); setConflictQuery(''); setShowConflictModal(true); }}
                  className="flex items-center gap-2 px-4 py-2 bg-red-50 text-red-600 border border-red-100 rounded-lg text-sm font-bold hover:bg-red-100 transition-colors"
               >
                  <Scale className="w-4 h-4" />
                  {t('conflict_check')}
               </button>

               <div className="flex gap-2 bg-gray-100 p-1 rounded-lg">
                  <button
                     onClick={() => setView('pipeline')}
                     className={`px-4 py-1.5 text-sm font-medium rounded-md transition-all ${view === 'pipeline' ? 'bg-white text-slate-800 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}>
                     {t('pipeline')}
                  </button>
                  <button
                     onClick={() => setView('clients')}
                     className={`px-4 py-1.5 text-sm font-medium rounded-md transition-all ${view === 'clients' ? 'bg-white text-slate-800 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}>
                     {t('clients')}
                  </button>
               </div>
            </div>
         </div>

         {view === 'pipeline' ? (
            <div className="flex-1 overflow-x-auto p-6">
               {isCrmSyncing && safeLeads.length === 0 && (
                  <div className="mb-4 rounded-xl border border-indigo-100 bg-indigo-50 px-4 py-3 text-sm text-indigo-700">
                     Loading lead pipeline...
                  </div>
               )}
               <div className="flex gap-4 min-w-[1000px] h-full">
                  {pipelineStages.map(stage => {
                     const stageLeads = safeLeads.filter(l => l.status === stage.key);
                     return (
                        <div key={stage.key} className="flex-1 flex flex-col min-w-[140px]">
                           <div className="flex justify-between items-center mb-3 px-2">
                              <div className="flex items-center gap-2">
                                 <div className={`w-2 h-2 rounded-full ${stage.color}`}></div>
                                 <h3 className="font-semibold text-gray-700 text-sm">{stage.label}</h3>
                              </div>
                              <span className="bg-gray-200 text-gray-600 px-2 py-0.5 rounded-full text-xs font-bold">{stageLeads.length}</span>
                           </div>
                           <div className="flex-1 bg-gray-100/50 rounded-xl p-2 space-y-2">
                              {stageLeads.map(lead => (
                                 <div
                                    key={lead.id}
                                    className={`bg-white p-3 rounded-lg shadow-sm border border-gray-200 hover:shadow-md transition-shadow ${highlightLeadId === lead.id ? 'ring-2 ring-indigo-400' : ''}`}
                                 >
                                    <div className="flex justify-between items-start mb-2">
                                       <span className="text-[10px] font-medium text-indigo-600 bg-indigo-50 px-1.5 py-0.5 rounded truncate max-w-[100px]">{lead.practiceArea}</span>
                                       <span className="text-[10px] text-gray-500 font-medium">{formatCurrency(lead.estimatedValue)}</span>
                                    </div>
                                    <h4 className="font-bold text-slate-800 text-sm truncate">{lead.name}</h4>
                                    <p className="text-[10px] text-gray-400 mt-1 truncate">{lead.source}</p>
                                    <div className="flex justify-between items-center pt-2 mt-2 border-t border-gray-100">
                                       <div className="flex gap-1">
                                          <button onClick={() => moveLead(lead, 'prev')} className="text-[10px] px-1.5 py-0.5 bg-gray-100 text-gray-600 rounded hover:bg-gray-200">&lt;</button>
                                          <button onClick={() => moveLead(lead, 'next')} className="text-[10px] px-1.5 py-0.5 bg-gray-100 text-gray-600 rounded hover:bg-gray-200">&gt;</button>
                                       </div>
                                       <button onClick={() => { void deleteLead(lead.id).catch(error => { console.error('Failed to archive lead', error); toast.error('Failed to archive lead.'); }); }} className="text-[10px] px-1.5 py-0.5 bg-red-50 text-red-600 rounded hover:bg-red-100">Archive</button>
                                    </div>
                                 </div>
                              ))}
                              <button onClick={() => setShowModal(true)} className="w-full py-2 text-xs font-medium text-gray-400 border border-dashed border-gray-300 rounded hover:bg-white transition-colors">
                                 + Add Lead
                              </button>
                           </div>
                        </div>
                     )
                  })}
               </div>
            </div>
         ) : (
            <div className="p-6 overflow-y-auto">
               {isCrmSyncing && safeClients.length === 0 ? (
                  <div className="text-center text-gray-400 mt-12">Loading clients...</div>
               ) : clients.length === 0 ? (
                  <div className="text-center text-gray-400 mt-12">No clients found. Add a Matter to create clients.</div>
               ) : (
                  <div className="space-y-4">
                     <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                        <div className="bg-gradient-to-br from-indigo-500 to-indigo-600 text-white rounded-xl p-4 shadow-md">
                           <p className="text-xs uppercase tracking-wide opacity-80">{t('crm_total_clients')}</p>
                           <div className="flex items-end gap-2 mt-2">
                              <span className="text-3xl font-bold">{clients.length}</span>
                              <span className="text-sm opacity-90">{t('crm_records_label')}</span>
                           </div>
                           <div className="text-[11px] mt-2 opacity-90">{t('crm_total_clients_desc')}</div>
                        </div>
                        <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
                           <p className="text-xs font-semibold text-gray-500 uppercase flex items-center gap-2"><Filter className="w-4 h-4" /> {t('crm_active_clients')}</p>
                           <div className="flex items-end gap-2 mt-2">
                              <span className="text-3xl font-bold text-slate-800">{activeCount}</span>
                              <span className="text-sm text-green-600 bg-green-50 px-2 py-0.5 rounded-full font-semibold">{t('status_active')}</span>
                           </div>
                           <p className="text-[11px] text-gray-500 mt-2">{t('crm_active_clients_desc')}</p>
                        </div>
                        <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
                           <p className="text-xs font-semibold text-gray-500 uppercase flex items-center gap-2"><Filter className="w-4 h-4" /> {t('crm_inactive_clients')}</p>
                           <div className="flex items-end gap-2 mt-2">
                              <span className="text-3xl font-bold text-slate-800">{inactiveCount}</span>
                              <span className="text-sm text-gray-600 bg-gray-100 px-2 py-0.5 rounded-full font-semibold">{t('crm_inactive_clients')}</span>
                           </div>
                           <p className="text-[11px] text-gray-500 mt-2">{t('crm_inactive_clients_desc')}</p>
                        </div>
                     </div>

                     <div className="bg-white rounded-xl shadow-card border border-gray-200 overflow-hidden">
                        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100 gap-4">
                           <div className="flex gap-2">
                              {[
                                 { key: 'all', label: `${t('crm_filter_all') || 'All Clients'} (${clients.length})` },
                                 { key: 'active', label: `${t('crm_filter_active') || 'Active'} (${activeCount})` },
                                 { key: 'inactive', label: `${t('crm_filter_inactive') || 'Inactive'} (${inactiveCount})` },
                              ].map(item => (
                                 <button
                                    key={item.key}
                                    onClick={() => setStatusFilter(item.key as any)}
                                    className={`px-3 py-1.5 text-sm rounded-lg border transition-all ${statusFilter === item.key
                                       ? 'bg-indigo-50 border-indigo-200 text-indigo-700 font-semibold'
                                       : 'text-gray-600 border-gray-200 hover:border-gray-300'}`}
                                 >
                                    {item.label}
                                 </button>
                              ))}
                           </div>
                           <div className="flex items-center gap-3 w-full max-w-xl justify-end">
                              <div className="relative w-full max-w-xs">
                                 <Search className="w-4 h-4 text-gray-400 absolute left-3 top-2.5" />
                                 <input
                                    value={clientSearch}
                                    onChange={e => setClientSearch(e.target.value)}
                                    placeholder="Search clients..."
                                    className="w-full pl-9 pr-3 py-2 border border-gray-200 rounded-lg text-sm bg-white text-slate-900 focus:ring-2 focus:ring-indigo-200 focus:border-indigo-300"
                                 />
                              </div>
                              <button
                                 onClick={() => { resetNewClientForm(); setShowCreateClientModal(true); }}
                                 className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-bold hover:bg-indigo-700 transition-colors"
                              >
                                 <Plus className="w-4 h-4" /> Add Client
                              </button>
                           </div>
                        </div>

                        <table className="w-full text-left">
                           <thead className="bg-gray-50 border-b border-gray-200">
                              <tr>
                                 <th className="px-6 py-4 text-xs font-bold text-gray-500 uppercase">ID</th>
                                 <th className="px-6 py-4 text-xs font-bold text-gray-500 uppercase">{t('col_name')}</th>
                                 <th className="px-6 py-4 text-xs font-bold text-gray-500 uppercase">{t('col_contact')}</th>
                                 <th className="px-6 py-4 text-xs font-bold text-gray-500 uppercase">{t('col_type')}</th>
                                 <th className="px-6 py-4 text-xs font-bold text-gray-500 uppercase">{t('status')}</th>
                                 <th className="px-6 py-4"></th>
                              </tr>
                           </thead>
                           <tbody className="divide-y divide-gray-100">
                              {filteredClients.map(client => {
                                 const clientStatus = client.status || 'Active';
                                 return (
                                 <tr
                                    key={client.id}
                                    className={`transition-colors ${highlightClientId === client.id ? 'bg-indigo-50' : 'hover:bg-gray-50'
                                       }`}
                                 >
                                    <td className="px-6 py-4">
                                       <span className="font-mono text-sm text-blue-600 bg-blue-50 px-2 py-1 rounded">
                                          {client.clientNumber || '-'}
                                       </span>
                                    </td>
                                    <td className="px-6 py-4">
                                       <div className="flex items-center gap-3">
                                          <div className="w-9 h-9 rounded-full bg-slate-100 flex items-center justify-center text-slate-600 font-bold text-xs">
                                             {client.name.substring(0, 2).toUpperCase()}
                                          </div>
                                          <div>
                                             <p className="text-sm font-semibold text-slate-900">{client.name}</p>
                                             {client.company && <p className="text-xs text-gray-500">{client.company}</p>}
                                          </div>
                                       </div>
                                    </td>
                                    <td className="px-6 py-4">
                                       <p className="text-sm text-gray-600">{client.email || '-'}</p>
                                       <p className="text-xs text-gray-400">{client.phone || '-'}</p>
                                    </td>
                                    <td className="px-6 py-4">
                                       <span className="text-sm text-gray-600">{client.type}</span>
                                    </td>
                                    <td className="px-6 py-4">
                                       <span className={`px-2 py-1 text-xs rounded font-semibold ${clientStatus === 'Active'
                                          ? 'bg-green-100 text-green-700'
                                          : 'bg-gray-100 text-gray-600'}`}>
                                          {clientStatus}
                                       </span>
                                    </td>
                                    <td className="px-6 py-4 text-right">
                                       <div className="flex items-center justify-end gap-2">
                                          <button
                                             onClick={() => openEditClient(client)}
                                             className="text-gray-500 hover:text-indigo-600 flex items-center gap-1 text-sm font-semibold"
                                          >
                                             <Edit className="w-4 h-4" /> {t('edit') || 'Edit'}
                                          </button>
                                          <button className="text-gray-400 hover:text-primary-600">
                                             <ChevronRight className="w-5 h-5" />
                                          </button>
                                       </div>
                                    </td>
                                 </tr>
                              )})}
                              {filteredClients.length === 0 && (
                                 <tr>
                                    <td colSpan={6} className="px-6 py-8 text-center text-gray-400 text-sm">
                                       {t('crm_no_clients')}
                                    </td>
                                 </tr>
                              )}
                           </tbody>
                        </table>
                     </div>
                  </div>
               )}
            </div>
         )}

         {/* Add Deal Modal */}
         {showModal && (
            <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
               <div className="bg-white rounded-xl shadow-2xl p-6 w-96">
                  <h3 className="font-bold text-lg mb-4 text-slate-800">Add Potential Lead</h3>
                  <form onSubmit={handleAddDeal} className="space-y-3">
                     <input required className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900" placeholder="Name" value={newLeadName} onChange={e => setNewLeadName(e.target.value)} />
                     <input
                        type="number"
                        className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                        placeholder="Est. Value ($)"
                        value={newLeadVal}
                        onChange={e => {
                           const val = e.target.value;
                           setNewLeadVal(val === '' ? '' : parseFloat(val))
                        }}
                     />
                     <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900" value={newLeadSource} onChange={e => setNewLeadSource(e.target.value as LeadSource)}>
                        {Object.values(LeadSource).map(src => (
                           <option key={src} value={src}>{src}</option>
                        ))}
                     </select>
                     <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900" value={newLeadPracticeArea} onChange={e => setNewLeadPracticeArea(e.target.value as PracticeArea)}>
                        {Object.values(PracticeArea).map(area => (
                           <option key={area} value={area}>{area}</option>
                        ))}
                     </select>
                     <div className="flex justify-end gap-2 mt-4">
                        <button type="button" onClick={() => setShowModal(false)} className="px-3 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg">Cancel</button>
                        <button type="submit" className="px-3 py-2 bg-slate-800 text-white font-bold rounded-lg text-sm">Add</button>
                     </div>
                  </form>
               </div>
            </div>
         )}

         {/* Conflict Check Modal */}
         {showConflictModal && (
            <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
               <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden animate-in fade-in zoom-in-95 duration-200">
                  <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                     <h3 className="font-bold text-lg text-slate-800 flex items-center gap-2"><Scale className="w-5 h-5" /> Conflict Check</h3>
                     <button onClick={() => setShowConflictModal(false)} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
                  </div>
                  <div className="p-6">
                     <p className="text-sm text-gray-500 mb-4">
                        Search across all database records (Clients, Leads, Opposing Parties) to ensure no conflict of interest exists before accepting a new case.
                     </p>
                     <form onSubmit={performConflictCheck} className="flex gap-2 mb-6">
                        <input
                           autoFocus
                           type="text"
                           className="flex-1 border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none focus:ring-2 focus:ring-primary-500"
                           placeholder="Enter name (e.g. John Doe, Corp Inc)..."
                           value={conflictQuery}
                           onChange={e => setConflictQuery(e.target.value)}
                        />
                        <button type="submit" disabled={isSearching} className="bg-slate-800 text-white px-4 py-2 rounded-lg text-sm font-bold disabled:opacity-50">
                           {isSearching ? 'Searching...' : 'Search'}
                        </button>
                     </form>

                     {isSearching && (
                        <div className="text-center py-8">
                           <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-slate-800 mx-auto"></div>
                           <p className="mt-2 text-sm text-gray-500">Checking database...</p>
                        </div>
                     )}

                     {!isSearching && conflictResults && (
                        <div className="bg-gray-50 rounded-lg border border-gray-200 overflow-hidden">
                           {conflictResults.length === 0 ? (
                              <div className="p-8 text-center">
                                 <CheckSquare className="w-8 h-8 text-green-500 mx-auto mb-2" />
                                 <p className="text-sm font-bold text-green-700">No conflicts found.</p>
                                 <p className="text-xs text-gray-500">No records match "{conflictQuery}".</p>
                              </div>
                           ) : (
                              <div>
                                 <div className="bg-red-50 p-3 border-b border-red-100 flex items-center gap-2">
                                    <div className="w-2 h-2 rounded-full bg-red-500"></div>
                                    <p className="text-xs font-bold text-red-700">{conflictResults.length} Potential Match(es) Found</p>
                                 </div>
                                 <div className="max-h-60 overflow-y-auto">
                                    {conflictResults.map((res, idx) => (
                                       <div key={idx} className="p-3 border-b border-gray-100 last:border-0 hover:bg-white transition-colors">
                                          <div className="flex justify-between">
                                             <span className="font-bold text-slate-800 text-sm">{res.name}</span>
                                             <span className="text-xs font-mono bg-gray-200 px-1.5 py-0.5 rounded text-gray-600">{res.type}</span>
                                          </div>
                                          <p className="text-xs text-gray-500 mt-1">Status: {res.status}</p>
                                       </div>
                                    ))}
                                 </div>
                              </div>
                           )}
                        </div>
                     )}
                  </div>
               </div>
            </div>
         )}

         {/* Create Client Modal */}
         {showCreateClientModal && (
            <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
               <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden">
                  <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                     <div>
                        <p className="text-xs text-gray-500 uppercase font-semibold">New Client</p>
                        <h3 className="font-bold text-lg text-slate-800">Client Profile</h3>
                     </div>
                     <button
                        onClick={() => { setShowCreateClientModal(false); resetNewClientForm(); }}
                        className="text-gray-400 hover:text-gray-600"
                     >
                        <X className="w-5 h-5" />
                     </button>
                  </div>
                  <form onSubmit={handleCreateClient} autoComplete="off" data-lpignore="true">
                     <div className="absolute opacity-0 pointer-events-none h-0 w-0 overflow-hidden" aria-hidden="true">
                        <input type="text" name="username" autoComplete="username" tabIndex={-1} />
                        <input type="password" name="current-password" autoComplete="current-password" tabIndex={-1} />
                     </div>
                     <div className="p-6">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Name</label>
                              <input
                                 name="client-full-name"
                                 autoComplete="off"
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.name}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, name: e.target.value }))}
                                 placeholder="Client name"
                                 required
                              />
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Email</label>
                              <input
                                 type="email"
                                 name="client-contact-email"
                                 autoComplete="off"
                                 data-lpignore="true"
                                 data-1p-ignore="true"
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.email}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, email: e.target.value }))}
                                 placeholder="name@example.com"
                                 required
                              />
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Phone</label>
                              <input
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.phone}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, phone: e.target.value }))}
                                 placeholder="(555) 555-5555"
                              />
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Company</label>
                              <input
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.company}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, company: e.target.value }))}
                                 placeholder="Company name"
                              />
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Client Number</label>
                              <input
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.clientNumber}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, clientNumber: e.target.value }))}
                                 placeholder="CLT-0001"
                              />
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Type</label>
                              <select
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.type}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, type: e.target.value as Client['type'] }))}
                              >
                                 <option value="Individual">Individual</option>
                                 <option value="Corporate">Corporate</option>
                              </select>
                           </div>
                           <div>
                              <label className="text-xs font-semibold text-gray-500">Status</label>
                              <select
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={newClientForm.status}
                                 onChange={e => setNewClientForm(prev => ({ ...prev, status: e.target.value as Client['status'] }))}
                              >
                                 <option value="Active">Active</option>
                                 <option value="Inactive">Inactive</option>
                              </select>
                           </div>
                        </div>

                        <div className="mt-6 border-t border-gray-100 pt-4">
                           <p className="text-xs font-semibold text-gray-500 uppercase">Portal Access</p>
                           <label className="flex items-center gap-2 text-sm text-slate-700 mt-3">
                              <input
                                 type="checkbox"
                                 className="rounded border-gray-300"
                                 checked={newClientForm.portalEnabled}
                                 onChange={e => setNewClientForm(prev => ({
                                    ...prev,
                                    portalEnabled: e.target.checked,
                                    password: e.target.checked ? prev.password : ''
                                 }))}
                              />
                              Enable client portal
                           </label>
                           {newClientForm.portalEnabled && (
                              <div className="mt-3">
                                 <label className="text-xs font-semibold text-gray-500">Temporary Password</label>
                                 <div className="mt-1 flex gap-2">
                                    <input
                                       type="password"
                                       name="client-portal-password"
                                       autoComplete="new-password"
                                       data-lpignore="true"
                                       data-1p-ignore="true"
                                       className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                       value={newClientForm.password}
                                       onChange={e => setNewClientForm(prev => ({ ...prev, password: e.target.value }))}
                                       placeholder="Set a temporary password"
                                    />
                                    <button
                                       type="button"
                                       onClick={() => setNewClientForm(prev => ({ ...prev, password: generateTempPassword() }))}
                                       className="px-3 py-2 bg-slate-100 text-slate-700 rounded-lg text-xs font-bold hover:bg-slate-200"
                                    >
                                       Generate
                                    </button>
                                 </div>
                                 <p className="text-xs text-gray-400 mt-2">
                                    This password is applied through the dedicated portal credential endpoint after the client record is created.
                                 </p>
                              </div>
                           )}
                        </div>

                        {createClientError && (
                           <div className="mt-4 px-3 py-2 bg-red-50 border border-red-100 text-red-600 text-sm rounded-lg">
                              {createClientError}
                           </div>
                        )}
                     </div>

                     <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
                        <button
                           type="button"
                           onClick={() => { setShowCreateClientModal(false); resetNewClientForm(); }}
                           className="px-3 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg"
                           disabled={isCreatingClient}
                        >
                           Cancel
                        </button>
                        <button
                           type="submit"
                           className="px-4 py-2 bg-indigo-600 text-white font-bold rounded-lg text-sm hover:bg-indigo-700 disabled:opacity-60"
                           disabled={isCreatingClient}
                        >
                           {isCreatingClient ? 'Creating...' : 'Create Client'}
                        </button>
                     </div>
                  </form>
               </div>
            </div>
         )}

         {/* Edit Client Modal */}
         {editingClient && (
            <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
               <div className="bg-white rounded-xl shadow-2xl w-full max-w-xl overflow-hidden">
                  <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50">
                     <div>
                        <p className="text-xs text-gray-500 uppercase font-semibold">{t('crm_edit_client')}</p>
                        <h3 className="font-bold text-lg text-slate-800">{editingClient.name}</h3>
                     </div>
                     <button onClick={() => { setEditingClient(null); setStatusChangeNote(''); }} className="text-gray-400 hover:text-gray-600">
                        <X className="w-5 h-5" />
                     </button>
                  </div>
                  <div className="p-6">
                     <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_name')}</label>
                           <input
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.name || ''}
                              onChange={e => setClientForm(prev => ({ ...prev, name: e.target.value }))}
                           />
                        </div>
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_email')}</label>
                           <input
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.email || ''}
                              onChange={e => setClientForm(prev => ({ ...prev, email: e.target.value }))}
                           />
                        </div>
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_phone')}</label>
                           <input
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.phone || ''}
                              onChange={e => setClientForm(prev => ({ ...prev, phone: e.target.value }))}
                           />
                        </div>
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_company')}</label>
                           <input
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.company || ''}
                              onChange={e => setClientForm(prev => ({ ...prev, company: e.target.value }))}
                           />
                        </div>
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_type')}</label>
                           <select
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.type || 'Individual'}
                              onChange={e => setClientForm(prev => ({ ...prev, type: e.target.value as Client['type'] }))}
                           >
                              <option value="Individual">Individual</option>
                              <option value="Corporate">Corporate</option>
                           </select>
                        </div>
                        <div>
                           <label className="text-xs font-semibold text-gray-500">{t('crm_label_status')}</label>
                           <select
                              className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                              value={clientForm.status || 'Active'}
                              onChange={e => setClientForm(prev => ({ ...prev, status: e.target.value as Client['status'] }))}
                           >
                              <option value="Active">Active</option>
                              <option value="Inactive">Inactive</option>
                           </select>
                        </div>
                        {editingClient && (clientForm.status || editingClient.status) !== editingClient.status && (
                           <div className="md:col-span-2">
                              <label className="text-xs font-semibold text-gray-500">Status Change Note (optional)</label>
                              <textarea
                                 className="mt-1 w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                 value={statusChangeNote}
                                 onChange={e => setStatusChangeNote(e.target.value)}
                                 placeholder={`Why is the status changing to ${clientForm.status || editingClient.status}?`}
                                 rows={2}
                              />
                           </div>
                        )}
                     </div>

                     <div className="mt-6 border-t border-gray-100 pt-4">
                        <div className="flex items-center justify-between">
                           <p className="text-xs font-semibold text-gray-500 uppercase">Status Timeline</p>
                           <button
                              onClick={() => editingClient && loadStatusHistory(editingClient.id)}
                              className="text-xs text-gray-500 hover:text-slate-700"
                              type="button"
                           >
                              Refresh
                           </button>
                        </div>
                        {statusHistoryLoading ? (
                           <p className="text-xs text-gray-400 mt-3">Loading status history...</p>
                        ) : statusHistory.length === 0 ? (
                           <p className="text-xs text-gray-400 mt-3">No status updates recorded yet.</p>
                        ) : (
                           <div className="mt-3 space-y-3">
                              {statusHistory.map(item => (
                                 <div key={item.id} className="flex items-start gap-3 bg-slate-50 border border-slate-200 rounded-lg p-3">
                                    <div className="w-2 h-2 rounded-full bg-slate-400 mt-1.5"></div>
                                    <div className="flex-1">
                                       <p className="text-sm font-semibold text-slate-800">
                                          {item.previousStatus} {'->'} {item.newStatus}
                                       </p>
                                       <p className="text-xs text-gray-500 mt-1">
                                          {item.changedByName || 'System'} - {new Date(item.createdAt).toLocaleString('en-US')}
                                       </p>
                                       {item.notes && (
                                          <p className="text-xs text-gray-500 mt-1">{item.notes}</p>
                                       )}
                                    </div>
                                 </div>
                              ))}
                           </div>
                        )}
                     </div>

                     <div className="mt-6 border-t border-gray-100 pt-4">
                        <p className="text-xs font-semibold text-gray-500 uppercase">Portal Access</p>
                        <label className="flex items-center gap-2 text-sm text-slate-700 mt-3">
                           <input
                              type="checkbox"
                              className="rounded border-gray-300"
                              checked={!!clientForm.portalEnabled}
                              onChange={e => {
                                 const enabled = e.target.checked;
                                 setClientForm(prev => ({ ...prev, portalEnabled: enabled }));
                                 if (!enabled) {
                                    setPortalPassword('');
                                 }
                              }}
                           />
                           Enable client portal
                        </label>
                        {clientForm.portalEnabled ? (
                           <div className="mt-3">
                              <label className="text-xs font-semibold text-gray-500">Set New Password</label>
                              <div className="mt-1 flex gap-2">
                                 <input
                                     type="password"
                                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900"
                                    value={portalPassword}
                                    onChange={e => setPortalPassword(e.target.value)}
                                    placeholder="Leave blank to keep the current password"
                                 />
                                 <button
                                    type="button"
                                    onClick={() => setPortalPassword(generateTempPassword())}
                                    className="px-3 py-2 bg-slate-100 text-slate-700 rounded-lg text-xs font-bold hover:bg-slate-200"
                                 >
                                    Generate
                                 </button>
                              </div>
                              <p className="text-xs text-gray-400 mt-2">
                                  Password changes are applied through the dedicated portal credential endpoint.
                              </p>
                           </div>
                        ) : (
                           <p className="text-xs text-gray-400 mt-2">
                              Portal access is currently disabled for this client.
                           </p>
                        )}
                     </div>

                     <div className="flex justify-end gap-2 mt-6">
                        <button
                           type="button"
                           onClick={() => { setEditingClient(null); setStatusChangeNote(''); }}
                           className="px-3 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg"
                           disabled={isSavingClient}
                        >
                           {t('cancel')}
                        </button>
                        <button
                           onClick={handleSaveClient}
                           className="px-4 py-2 bg-indigo-600 text-white font-bold rounded-lg text-sm hover:bg-indigo-700 disabled:opacity-60"
                           disabled={isSavingClient}
                        >
                           {isSavingClient ? t('saving') : t('save')}
                        </button>
                     </div>
                  </div>
               </div>
            </div>
         )}
      </div>
   );
};

export default CRM;
