import React, { useState, useEffect } from 'react';
import { Users, Plus, Edit, Trash2, X, Save, Shield, User as UserIcon, AlertCircle, RefreshCw } from './Icons';
import { api } from '../services/api';
import { passwordRequirementsText, validatePassword } from '../services/passwordPolicy';
import { useAuth } from '../contexts/AuthContext';
import { useConfirm } from './ConfirmDialog';
import { AdminAuditLogs } from './AdminAuditLogs';

interface User {
  id: string;
  email: string;
  name: string;
  role: 'Admin' | 'Partner' | 'Associate';
  phone?: string;
  mobile?: string;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  country?: string;
  barNumber?: string;
  bio?: string;
}

interface Client {
  id: string;
  name: string;
  email: string;
  phone?: string;
  mobile?: string;
  company?: string;
  type: string;
  status: string;
  portalEnabled: boolean;
  passwordHash?: string;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  country?: string;
  taxId?: string;
  notes?: string;
}

interface RetentionPolicy {
  id: string;
  entityName: string;
  retentionDays: number;
  isActive: boolean;
  lastAppliedAt?: string;
}

const AdminPanel: React.FC = () => {
  const { user } = useAuth();
  const { confirm } = useConfirm();
  const [activeSection, setActiveSection] = useState<'users' | 'clients' | 'audit' | 'retention'>('users');
  const [users, setUsers] = useState<User[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [retentionPolicies, setRetentionPolicies] = useState<RetentionPolicy[]>([]);
  const [retentionLoading, setRetentionLoading] = useState(false);
  const [loading, setLoading] = useState(false);
  const [accessDenied, setAccessDenied] = useState(false);
  const [showUserModal, setShowUserModal] = useState(false);
  const [showClientModal, setShowClientModal] = useState(false);
  const [editingUser, setEditingUser] = useState<User | null>(null);
  const [editingClient, setEditingClient] = useState<Client | null>(null);
  const [userForm, setUserForm] = useState({
    email: '',
    name: '',
    password: '',
    role: 'Associate' as 'Admin' | 'Partner' | 'Associate',
    phone: '',
    mobile: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: '',
    barNumber: '',
    bio: ''
  });
  const [clientForm, setClientForm] = useState({
    name: '',
    email: '',
    phone: '',
    mobile: '',
    company: '',
    type: 'Individual',
    status: 'Active',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: '',
    taxId: '',
    notes: '',
    password: '',
    portalEnabled: false
  });

  useEffect(() => {
    // Only Admin users can access the admin panel
    if (user?.role !== 'Admin') {
      setAccessDenied(true);
      return;
    }
    setAccessDenied(false); // Reset access denied state if user is admin
    loadUsers();
    loadClients();
  }, [user]);

  useEffect(() => {
    if (activeSection === 'retention') {
      loadRetention();
    }
  }, [activeSection]);

  const loadUsers = async () => {
    try {
      setLoading(true);
      const data = await api.admin.getUsers();
      if (data === null) {
        // 401 Unauthorized - yetki yok
        setAccessDenied(true);
        return;
      }
      setUsers(data || []);
    } catch (error: any) {
      console.error('Error loading users:', error);
      if (error.message?.includes('403') || error.message?.includes('Admin access required')) {
        setAccessDenied(true);
      } else {
        const { toast } = await import('./Toast');
        toast.error('Failed to load users');
      }
    } finally {
      setLoading(false);
    }
  };

  const loadClients = async () => {
    try {
      setLoading(true);
      const data = await api.admin.getClients();
      if (data === null) {
        // 401 Unauthorized - yetki yok
        setAccessDenied(true);
        return;
      }
      setClients(data || []);
    } catch (error: any) {
      console.error('Error loading clients:', error);
      if (error.message?.includes('403') || error.message?.includes('Admin access required')) {
        setAccessDenied(true);
      } else {
        const { toast } = await import('./Toast');
        toast.error('Failed to load clients');
      }
    } finally {
      setLoading(false);
    }
  };

  const loadRetention = async () => {
    try {
      setRetentionLoading(true);
      const data = await api.admin.getRetentionPolicies();
      setRetentionPolicies(Array.isArray(data) ? data : []);
    } catch (error: any) {
      console.error('Error loading retention policies:', error);
      const { toast } = await import('./Toast');
      toast.error('Failed to load retention policies');
    } finally {
      setRetentionLoading(false);
    }
  };

  const handleSaveRetention = async () => {
    try {
      setRetentionLoading(true);
      const payload = retentionPolicies.map(p => ({
        entityName: p.entityName,
        retentionDays: p.retentionDays,
        isActive: p.isActive
      }));
      await api.admin.updateRetentionPolicies(payload);
      await loadRetention();
      const { toast } = await import('./Toast');
      toast.success('Retention policies updated');
    } catch (error: any) {
      console.error('Retention save failed:', error);
      const { toast } = await import('./Toast');
      toast.error('Failed to update retention policies');
    } finally {
      setRetentionLoading(false);
    }
  };

  const handleRunRetention = async () => {
    try {
      setRetentionLoading(true);
      await api.admin.runRetention();
      await loadRetention();
      const { toast } = await import('./Toast');
      toast.success('Retention cleanup executed');
    } catch (error: any) {
      console.error('Retention run failed:', error);
      const { toast } = await import('./Toast');
      toast.error('Retention cleanup failed');
    } finally {
      setRetentionLoading(false);
    }
  };

  const handleCreateUser = () => {
    setEditingUser(null);
    setUserForm({
      email: '',
      name: '',
      password: '',
      role: 'Associate',
      phone: '',
      mobile: '',
      address: '',
      city: '',
      state: '',
      zipCode: '',
      country: '',
      barNumber: '',
      bio: ''
    });
    setShowUserModal(true);
  };

  const handleEditUser = (user: User) => {
    setEditingUser(user);
    setUserForm({
      email: user.email,
      name: user.name,
      password: '',
      role: user.role,
      phone: user.phone || '',
      mobile: user.mobile || '',
      address: user.address || '',
      city: user.city || '',
      state: user.state || '',
      zipCode: user.zipCode || '',
      country: user.country || '',
      barNumber: user.barNumber || '',
      bio: user.bio || ''
    });
    setShowUserModal(true);
  };

  const handleSaveUser = async () => {
    try {
      const { toast } = await import('./Toast');

      if (!userForm.email || !userForm.name) {
        toast.error('Email and name are required');
        return;
      }
      if (!editingUser && !userForm.password) {
        toast.error('Password is required for new user');
        return;
      }
      if (userForm.password) {
        const result = validatePassword(userForm.password, { email: userForm.email, name: userForm.name });
        if (!result.isValid) {
          toast.error(result.message);
          return;
        }
      }

      if (editingUser) {
        await api.admin.updateUser(editingUser.id, userForm);
      } else {
        await api.admin.createUser(userForm);
      }
      await loadUsers();
      setShowUserModal(false);
      toast.success('User saved');
    } catch (error: any) {
      const { toast } = await import('./Toast');
      toast.error(error.message || 'Failed to save user');
    }
  };

  const handleDeleteUser = async (id: string) => {
    const ok = await confirm({
      title: 'Delete User',
      message: 'Are you sure you want to delete this user?',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      variant: 'danger'
    });
    if (!ok) return;
    try {
      const { toast } = await import('./Toast');
      await api.admin.deleteUser(id);
      await loadUsers();
      toast.success('User deleted');
    } catch (error: any) {
      const { toast } = await import('./Toast');
      toast.error(error.message || 'Failed to delete user');
    }
  };

  const handleEditClient = (client: Client) => {
    setEditingClient(client);
    setClientForm({
      name: client.name,
      email: client.email,
      phone: client.phone || '',
      mobile: client.mobile || '',
      company: client.company || '',
      type: client.type,
      status: client.status,
      address: client.address || '',
      city: client.city || '',
      state: client.state || '',
      zipCode: client.zipCode || '',
      country: client.country || '',
      taxId: client.taxId || '',
      notes: client.notes || '',
      password: '',
      portalEnabled: client.portalEnabled
    });
    setShowClientModal(true);
  };

  const handleSaveClient = async () => {
    const { toast } = await import('./Toast');

    try {
      if (!clientForm.name || !clientForm.email) {
        toast.error('Name and email are required');
        return;
      }
      const enablingPortal = clientForm.portalEnabled && !editingClient?.portalEnabled;
      if (enablingPortal && !clientForm.password) {
        toast.error('Portal password is required when enabling access');
        return;
      }
      if (clientForm.password) {
        const result = validatePassword(clientForm.password, { email: clientForm.email, name: clientForm.name });
        if (!result.isValid) {
          toast.error(result.message);
          return;
        }
      }

      if (editingClient) {
        await api.admin.updateClient(editingClient.id, clientForm);
      } else {
        // Create client via regular API
        await api.createClient(clientForm);
      }
      await loadClients();
      setShowClientModal(false);
      toast.success('Client saved');
    } catch (error: any) {
      toast.error(error.message || 'Failed to save client');
    }
  };

  const handleDeleteClient = async (id: string) => {
    const ok = await confirm({
      title: 'Delete Client',
      message: 'Are you sure you want to delete this client?',
      confirmText: 'Delete',
      cancelText: 'Cancel',
      variant: 'danger'
    });
    if (!ok) return;
    try {
      const { toast } = await import('./Toast');
      await api.admin.deleteClient(id);
      await loadClients();
      toast.success('Client deleted');
    } catch (error: any) {
      const { toast } = await import('./Toast');
      toast.error(error.message || 'Failed to delete client');
    }
  };

  // Access denied message
  if (accessDenied || user?.role !== 'Admin') {
    return (
      <div className="h-full flex items-center justify-center bg-gray-50/50">
        <div className="text-center p-8 bg-white rounded-lg border border-gray-200 max-w-md">
          <AlertCircle className="w-16 h-16 text-red-500 mx-auto mb-4" />
          <h2 className="text-2xl font-bold text-slate-800 mb-2">Access Denied</h2>
          <p className="text-gray-600 mb-4">
            You need Admin privileges to access this page.
          </p>
          <p className="text-sm text-gray-500">
            Only authorized admin accounts can use the admin panel.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      {/* Header */}
      <div className="px-6 py-4 border-b border-gray-200 bg-white">
        <div className="flex items-center gap-3 mb-2">
          <Shield className="w-6 h-6 text-slate-800" />
          <h1 className="text-2xl font-bold text-slate-800">Admin Panel</h1>
        </div>
        <p className="text-sm text-gray-500">User and client management - Admin access only</p>
      </div>

      {/* Tabs */}
      <div className="px-8 py-4 border-b border-gray-200 bg-white">
        <div className="flex gap-4">
          <button
            onClick={() => setActiveSection('users')}
            className={`px-4 py-2 rounded-lg font-medium transition-colors ${activeSection === 'users'
              ? 'bg-slate-800 text-white'
              : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
              }`}
          >
            <Users className="w-4 h-4 inline mr-2" />
            Attorneys ({users.length})
          </button>
          <button
            onClick={() => setActiveSection('clients')}
            className={`px-4 py-2 rounded-lg font-medium transition-colors ${activeSection === 'clients'
              ? 'bg-slate-800 text-white'
              : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
              }`}
          >
            <UserIcon className="w-4 h-4 inline mr-2" />
            Clients ({clients.length})
          </button>
          <button
            onClick={() => setActiveSection('audit')}
            className={`px-4 py-2 rounded-lg font-medium transition-colors ${activeSection === 'audit'
              ? 'bg-slate-800 text-white'
              : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
              }`}
          >
            <Shield className="w-4 h-4 inline mr-2" />
            Logs
          </button>
          <button
            onClick={() => setActiveSection('retention')}
            className={`px-4 py-2 rounded-lg font-medium transition-colors ${activeSection === 'retention'
              ? 'bg-slate-800 text-white'
              : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
              }`}
          >
            <Save className="w-4 h-4 inline mr-2" />
            Retention
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-8">
        {activeSection === 'users' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-xl font-bold text-slate-800">Attorney Management</h2>
              <button
                onClick={handleCreateUser}
                className="flex items-center gap-2 px-4 py-2 bg-slate-800 text-white rounded-lg hover:bg-slate-900"
              >
                <Plus className="w-4 h-4" />
                Add New Attorney
              </button>
            </div>

            {loading ? (
              <div className="text-center py-8 text-gray-500">Loading...</div>
            ) : (
              <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                <table className="w-full">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Full Name</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Email</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Role</th>
                      <th className="px-4 py-3 text-right text-sm font-semibold text-gray-700">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-200">
                    {users.map((user) => (
                      <tr key={user.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3 text-sm text-gray-900">{user.name}</td>
                        <td className="px-4 py-3 text-sm text-gray-600">{user.email}</td>
                        <td className="px-4 py-3">
                          <span className={`px-2 py-1 rounded text-xs font-medium ${user.role === 'Admin' ? 'bg-red-100 text-red-700' :
                            user.role === 'Partner' ? 'bg-blue-100 text-blue-700' :
                              'bg-gray-100 text-gray-700'
                            }`}>
                            {user.role}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex justify-end gap-2">
                            <button
                              onClick={() => handleEditUser(user)}
                              className="p-1.5 text-blue-600 hover:bg-blue-50 rounded"
                            >
                              <Edit className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => handleDeleteUser(user.id)}
                              className="p-1.5 text-red-600 hover:bg-red-50 rounded"
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {activeSection === 'clients' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-xl font-bold text-slate-800">Client Management</h2>
              <button
                onClick={() => {
                  setEditingClient(null);
                  setClientForm({
                    name: '',
                    email: '',
                    phone: '',
                    mobile: '',
                    company: '',
                    type: 'Individual',
                    status: 'Active',
                    address: '',
                    city: '',
                    state: '',
                    zipCode: '',
                    country: '',
                    taxId: '',
                    notes: '',
                    password: '',
                    portalEnabled: false
                  });
                  setShowClientModal(true);
                }}
                className="flex items-center gap-2 px-4 py-2 bg-slate-800 text-white rounded-lg hover:bg-slate-900"
              >
                <Plus className="w-4 h-4" />
                Add New Client
              </button>
            </div>

            {loading ? (
              <div className="text-center py-8 text-gray-500">Loading...</div>
            ) : (
              <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                <table className="w-full">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Full Name</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Email</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Status</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Portal</th>
                      <th className="px-4 py-3 text-right text-sm font-semibold text-gray-700">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-200">
                    {clients.map((client) => (
                      <tr key={client.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3 text-sm text-gray-900">{client.name}</td>
                        <td className="px-4 py-3 text-sm text-gray-600">{client.email}</td>
                        <td className="px-4 py-3">
                          <span className={`px-2 py-1 rounded text-xs font-medium ${client.status === 'Active' ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'
                            }`}>
                            {client.status}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <span className={`px-2 py-1 rounded text-xs font-medium ${client.portalEnabled ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-700'
                            }`}>
                            {client.portalEnabled ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex justify-end gap-2">
                            <button
                              onClick={() => handleEditClient(client)}
                              className="p-1.5 text-blue-600 hover:bg-blue-50 rounded"
                            >
                              <Edit className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => handleDeleteClient(client.id)}
                              className="p-1.5 text-red-600 hover:bg-red-50 rounded"
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {activeSection === 'audit' && (
          <div>
            <AdminAuditLogs />
          </div>
        )}

        {activeSection === 'retention' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <div>
                <h2 className="text-xl font-bold text-slate-800">Retention Policies</h2>
                <p className="text-sm text-gray-500">Control data retention windows for compliance.</p>
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={handleRunRetention}
                  className="flex items-center gap-2 px-4 py-2 bg-gray-100 text-gray-700 rounded-lg hover:bg-gray-200"
                  disabled={retentionLoading}
                >
                  <RefreshCw className={`w-4 h-4 ${retentionLoading ? 'animate-spin' : ''}`} />
                  Run Cleanup
                </button>
                <button
                  onClick={handleSaveRetention}
                  className="flex items-center gap-2 px-4 py-2 bg-slate-800 text-white rounded-lg hover:bg-slate-900"
                  disabled={retentionLoading}
                >
                  <Save className="w-4 h-4" />
                  Save Changes
                </button>
              </div>
            </div>

            {retentionLoading ? (
              <div className="text-center py-8 text-gray-500">Loading...</div>
            ) : (
              <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                <table className="w-full">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Entity</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Retention (days)</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Active</th>
                      <th className="px-4 py-3 text-left text-sm font-semibold text-gray-700">Last Applied</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-200">
                    {retentionPolicies.map(policy => (
                      <tr key={policy.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3 text-sm text-gray-900">{policy.entityName}</td>
                        <td className="px-4 py-3">
                          <input
                            type="number"
                            min={1}
                            value={policy.retentionDays}
                            onChange={(e) => {
                              const value = parseInt(e.target.value || '0', 10);
                              setRetentionPolicies(prev => prev.map(p => p.id === policy.id ? { ...p, retentionDays: value } : p));
                            }}
                            className="w-32 border border-gray-300 rounded-lg p-2 text-sm"
                          />
                        </td>
                        <td className="px-4 py-3">
                          <button
                            onClick={() => {
                              setRetentionPolicies(prev => prev.map(p => p.id === policy.id ? { ...p, isActive: !p.isActive } : p));
                            }}
                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${policy.isActive ? 'bg-green-500' : 'bg-gray-300'}`}
                          >
                            <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${policy.isActive ? 'translate-x-6' : 'translate-x-1'}`} />
                          </button>
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-500">
                          {policy.lastAppliedAt ? new Date(policy.lastAppliedAt).toLocaleString('en-US') : 'Never'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>

      {/* User Modal */}
      {showUserModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-full max-w-2xl max-h-[90vh] overflow-y-auto">
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-xl font-bold text-slate-800">
                {editingUser ? 'Edit Attorney' : 'Add New Attorney'}
              </h3>
              <button onClick={() => setShowUserModal(false)} className="text-gray-400 hover:text-gray-600">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Full Name *</label>
                  <input
                    type="text"
                    required
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.name}
                    onChange={e => setUserForm({ ...userForm, name: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Email *</label>
                  <input
                    type="email"
                    required
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.email}
                    onChange={e => setUserForm({ ...userForm, email: e.target.value })}
                  />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Password {!editingUser && '*'}
                  </label>
                  <input
                    type="password"
                    required={!editingUser}
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.password}
                    onChange={e => setUserForm({ ...userForm, password: e.target.value })}
                    placeholder={editingUser ? 'Enter new password to change' : passwordRequirementsText}
                  />
                  <p className="mt-1 text-xs text-gray-500">{passwordRequirementsText}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Role *</label>
                  <select
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.role}
                    onChange={e => setUserForm({ ...userForm, role: e.target.value as any })}
                  >
                    <option value="Associate">Associate</option>
                    <option value="Partner">Partner</option>
                    <option value="Admin">Admin</option>
                  </select>
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                  <input
                    type="tel"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.phone}
                    onChange={e => setUserForm({ ...userForm, phone: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Mobile Phone</label>
                  <input
                    type="tel"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.mobile}
                    onChange={e => setUserForm({ ...userForm, mobile: e.target.value })}
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={userForm.address}
                  onChange={e => setUserForm({ ...userForm, address: e.target.value })}
                />
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.city}
                    onChange={e => setUserForm({ ...userForm, city: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.state}
                    onChange={e => setUserForm({ ...userForm, state: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={userForm.zipCode}
                    onChange={e => setUserForm({ ...userForm, zipCode: e.target.value })}
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Country</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={userForm.country}
                  onChange={e => setUserForm({ ...userForm, country: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Bar Number</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={userForm.barNumber}
                  onChange={e => setUserForm({ ...userForm, barNumber: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Biography</label>
                <textarea
                  rows={3}
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={userForm.bio}
                  onChange={e => setUserForm({ ...userForm, bio: e.target.value })}
                />
              </div>
            </div>
            <div className="flex justify-end gap-3 mt-6">
              <button
                onClick={() => setShowUserModal(false)}
                className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveUser}
                className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-medium hover:bg-slate-900 flex items-center gap-2"
              >
                <Save className="w-4 h-4" />
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Client Modal */}
      {showClientModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-full max-w-2xl max-h-[90vh] overflow-y-auto">
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-xl font-bold text-slate-800">
                {editingClient ? 'Edit Client' : 'Add New Client'}
              </h3>
              <button onClick={() => setShowClientModal(false)} className="text-gray-400 hover:text-gray-600">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Full Name *</label>
                  <input
                    type="text"
                    required
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.name}
                    onChange={e => setClientForm({ ...clientForm, name: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Email *</label>
                  <input
                    type="email"
                    required
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.email}
                    onChange={e => setClientForm({ ...clientForm, email: e.target.value })}
                  />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
                  <select
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.type}
                    onChange={e => setClientForm({ ...clientForm, type: e.target.value })}
                  >
                    <option value="Individual">Individual</option>
                    <option value="Corporate">Corporate</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
                  <select
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.status}
                    onChange={e => setClientForm({ ...clientForm, status: e.target.value })}
                  >
                    <option value="Active">Active</option>
                    <option value="Inactive">Inactive</option>
                  </select>
                </div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                  <input
                    type="tel"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.phone}
                    onChange={e => setClientForm({ ...clientForm, phone: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Mobile Phone</label>
                  <input
                    type="tel"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.mobile}
                    onChange={e => setClientForm({ ...clientForm, mobile: e.target.value })}
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Company</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={clientForm.company}
                  onChange={e => setClientForm({ ...clientForm, company: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={clientForm.address}
                  onChange={e => setClientForm({ ...clientForm, address: e.target.value })}
                />
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.city}
                    onChange={e => setClientForm({ ...clientForm, city: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.state}
                    onChange={e => setClientForm({ ...clientForm, state: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                    value={clientForm.zipCode}
                    onChange={e => setClientForm({ ...clientForm, zipCode: e.target.value })}
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Country</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={clientForm.country}
                  onChange={e => setClientForm({ ...clientForm, country: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Tax ID</label>
                <input
                  type="text"
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={clientForm.taxId}
                  onChange={e => setClientForm({ ...clientForm, taxId: e.target.value })}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Notes</label>
                <textarea
                  rows={3}
                  className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                  value={clientForm.notes}
                  onChange={e => setClientForm({ ...clientForm, notes: e.target.value })}
                />
              </div>
              <div className="border-t pt-4 space-y-4">
                <div className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    id="portalEnabled"
                    className="w-4 h-4"
                    checked={clientForm.portalEnabled}
                    onChange={e => setClientForm({ ...clientForm, portalEnabled: e.target.checked })}
                  />
                  <label htmlFor="portalEnabled" className="text-sm font-medium text-gray-700">
                    Client Portal Access Enabled
                  </label>
                </div>
                {clientForm.portalEnabled && (
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Portal Password {editingClient ? '(Enter new password to change)' : '*'}
                    </label>
                    <input
                      type="password"
                      required={!editingClient && clientForm.portalEnabled}
                      className="w-full border border-gray-300 rounded-lg p-2 text-sm"
                      value={clientForm.password}
                      onChange={e => setClientForm({ ...clientForm, password: e.target.value })}
                      placeholder={passwordRequirementsText}
                    />
                    <p className="mt-1 text-xs text-gray-500">{passwordRequirementsText}</p>
                  </div>
                )}
              </div>
            </div>
            <div className="flex justify-end gap-3 mt-6">
              <button
                onClick={() => setShowClientModal(false)}
                className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveClient}
                className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-medium hover:bg-slate-900 flex items-center gap-2"
              >
                <Save className="w-4 h-4" />
                Save
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default AdminPanel;

