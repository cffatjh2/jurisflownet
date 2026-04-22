import React, { useState, useEffect, useRef } from 'react';
import { Users, Plus, Edit, Trash2, Mail, Phone, Calendar, Clock, CheckSquare, RefreshCw, Search, LayoutGrid, List, Send, X } from './Icons';
import { Can } from './common/Can';
import { ConfirmDialog } from './common/ConfirmDialog';
import { CredentialModal } from './common/CredentialModal';

import { toast } from './Toast';
import { Employee, EmployeeRole, EmployeeStatus, BarLicenseStatus, USState } from '../types';
import { api } from '../services/api';
import { useTranslation } from '../contexts/LanguageContext';
import { useAuth } from '../contexts/AuthContext';
import EntityOfficeFilter from './common/EntityOfficeFilter';
import { passwordRequirementsText, validatePassword } from '../services/passwordPolicy';

const getAvatarPath = (emp: Employee) => emp.avatar || null;

const Employees: React.FC = () => {
    const { t } = useTranslation();
    const { user } = useAuth();
    const [employees, setEmployees] = useState<Employee[]>([]);
    const [loading, setLoading] = useState(true);
    const [showForm, setShowForm] = useState(false);
    const [editingEmployee, setEditingEmployee] = useState<Employee | null>(null);
    const [selectedRole, setSelectedRole] = useState<string>('all');
    const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');
    const [searchTerm, setSearchTerm] = useState('');
    const [selfEmployeeId, setSelfEmployeeId] = useState<string | null>(null);
    const [entityFilter, setEntityFilter] = useState('');
    const [officeFilter, setOfficeFilter] = useState('');

    // Delete Confirmation State
    const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
    const [employeeToDelete, setEmployeeToDelete] = useState<string | null>(null);

    // Credential Modal State
    const [credentialModalOpen, setCredentialModalOpen] = useState(false);
    const [createdCredential, setCreatedCredential] = useState<{ email: string; pass?: string; role: string } | null>(null);

    // Message Modal State
    const [messageModalOpen, setMessageModalOpen] = useState(false);
    const [messageTarget, setMessageTarget] = useState<Employee | null>(null);
    const [messageBody, setMessageBody] = useState('');
    const [sendingMessage, setSendingMessage] = useState(false);

    // Avatar upload state
    const [avatarFile, setAvatarFile] = useState<File | null>(null);
    const [avatarPreview, setAvatarPreview] = useState<string | null>(null);
    const [avatarUrls, setAvatarUrls] = useState<Record<string, string>>({});
    const avatarUrlsRef = useRef<Map<string, string>>(new Map());
    const [avatarPreviewIsTemp, setAvatarPreviewIsTemp] = useState(false);

    const [formData, setFormData] = useState({
        firstName: '',
        lastName: '',
        email: '',
        phone: '',
        mobile: '',
        role: EmployeeRole.LEGAL_ASSISTANT,
        hireDate: new Date().toISOString().split('T')[0],
        hourlyRate: '',
        salary: '',
        notes: '',
        address: '',
        emergencyContact: '',
        emergencyPhone: '',
        password: '',
        // Bar License Fields
        barNumber: '',
        barState: 'NY',
        barAdmissionDate: '',
        barStatus: 'Active',
        entityId: '',
        officeId: ''
    });

    const roleLabels: Record<EmployeeRole, string> = {
        [EmployeeRole.PARTNER]: t('role_partner') || 'Partner',
        [EmployeeRole.ASSOCIATE]: t('role_associate') || 'Associate',
        [EmployeeRole.OF_COUNSEL]: t('role_of_counsel') || 'Of Counsel',
        [EmployeeRole.PARALEGAL]: t('role_paralegal') || 'Paralegal',
        [EmployeeRole.LEGAL_SECRETARY]: t('role_legal_secretary') || 'Legal Secretary',
        [EmployeeRole.LEGAL_ASSISTANT]: t('role_legal_assistant') || 'Legal Assistant',
        [EmployeeRole.OFFICE_MANAGER]: t('role_office_manager') || 'Office Manager',
        [EmployeeRole.RECEPTIONIST]: t('role_receptionist') || 'Receptionist',
        [EmployeeRole.ACCOUNTANT]: t('role_accountant') || 'Accountant'
    };

    const roleIcons: Record<EmployeeRole, string> = {
        [EmployeeRole.PARTNER]: 'P',
        [EmployeeRole.ASSOCIATE]: 'A',
        [EmployeeRole.OF_COUNSEL]: 'OC',
        [EmployeeRole.PARALEGAL]: 'PL',
        [EmployeeRole.LEGAL_SECRETARY]: 'LS',
        [EmployeeRole.LEGAL_ASSISTANT]: 'LA',
        [EmployeeRole.OFFICE_MANAGER]: 'OM',
        [EmployeeRole.RECEPTIONIST]: 'R',
        [EmployeeRole.ACCOUNTANT]: 'AC'
    };

    const roleByIndex: EmployeeRole[] = [
        EmployeeRole.PARTNER,
        EmployeeRole.ASSOCIATE,
        EmployeeRole.OF_COUNSEL,
        EmployeeRole.PARALEGAL,
        EmployeeRole.LEGAL_SECRETARY,
        EmployeeRole.LEGAL_ASSISTANT,
        EmployeeRole.OFFICE_MANAGER,
        EmployeeRole.RECEPTIONIST,
        EmployeeRole.ACCOUNTANT
    ];

    const statusByIndex: EmployeeStatus[] = [
        EmployeeStatus.ACTIVE,
        EmployeeStatus.ON_LEAVE,
        EmployeeStatus.TERMINATED
    ];

    const barStatusByIndex: BarLicenseStatus[] = [
        BarLicenseStatus.Active,
        BarLicenseStatus.Inactive,
        BarLicenseStatus.Suspended,
        BarLicenseStatus.Pending
    ];

    const statusLabels: Record<EmployeeStatus, string> = {
        [EmployeeStatus.ACTIVE]: 'Active',
        [EmployeeStatus.ON_LEAVE]: 'On Leave',
        [EmployeeStatus.TERMINATED]: 'Terminated'
    };

    const statusColors: Record<EmployeeStatus, string> = {
        [EmployeeStatus.ACTIVE]: 'bg-green-100 text-green-800',
        [EmployeeStatus.ON_LEAVE]: 'bg-yellow-100 text-yellow-800',
        [EmployeeStatus.TERMINATED]: 'bg-red-100 text-red-800'
    };

    useEffect(() => {
        fetchEmployees();
    }, []);

    useEffect(() => {
        let cancelled = false;
        const needed = new Set<string>();
        employees.forEach((emp) => {
            const path = getAvatarPath(emp);
            if (path) needed.add(path);
        });

        const cache = avatarUrlsRef.current;
        for (const [path, url] of cache) {
            if (!needed.has(path)) {
                URL.revokeObjectURL(url);
                cache.delete(path);
            }
        }

        setAvatarUrls(Object.fromEntries(cache));

        const missing = Array.from(needed).filter((path) => !cache.has(path));
        if (missing.length === 0) {
            return () => {
                cancelled = true;
            };
        }

        (async () => {
            const downloads = await Promise.all(
                missing.map(async (path) => {
                    try {
                        const file = await api.downloadFile(path);
                        if (!file?.blob) return null;
                        const url = URL.createObjectURL(file.blob);
                        return { path, url };
                    } catch (error) {
                        console.error('Failed to load avatar', error);
                        return null;
                    }
                })
            );

            if (cancelled) {
                downloads.forEach((item) => {
                    if (item?.url) {
                        URL.revokeObjectURL(item.url);
                    }
                });
                return;
            }

            downloads.forEach((item) => {
                if (item) {
                    cache.set(item.path, item.url);
                }
            });
            setAvatarUrls(Object.fromEntries(cache));
        })();

        return () => {
            cancelled = true;
        };
    }, [employees]);

    useEffect(() => {
        return () => {
            if (avatarPreviewIsTemp && avatarPreview) {
                URL.revokeObjectURL(avatarPreview);
            }
        };
    }, [avatarPreview, avatarPreviewIsTemp]);

    useEffect(() => {
        return () => {
            for (const url of avatarUrlsRef.current.values()) {
                URL.revokeObjectURL(url);
            }
        };
    }, []);

    useEffect(() => {
        if (!editingEmployee || avatarFile) return;
        const avatarPath = getAvatarPath(editingEmployee);
        if (!avatarPath) return;
        const cached = avatarUrls[avatarPath];
        if (cached && avatarPreview !== cached) {
            setAvatarPreview(cached);
            setAvatarPreviewIsTemp(false);
        }
    }, [avatarFile, avatarPreview, avatarUrls, editingEmployee]);

    const normalizeEmployee = (emp: Employee) => {
        const normalized = { ...emp } as Employee;
        const roleValue = (emp as any).role;
        const statusValue = (emp as any).status;
        const barStatusValue = (emp as any).barStatus;

        if (typeof roleValue === 'number') {
            normalized.role = roleByIndex[roleValue] || EmployeeRole.LEGAL_ASSISTANT;
        }
        if (typeof statusValue === 'number') {
            normalized.status = statusByIndex[statusValue] || EmployeeStatus.ACTIVE;
        }
        if (typeof barStatusValue === 'number') {
            normalized.barStatus = barStatusByIndex[barStatusValue] || BarLicenseStatus.Active;
        }

        return normalized;
    };

    const fetchEmployees = async () => {
        try {
            const data = await api.getEmployees();
            const normalized = Array.isArray(data) ? data.map(normalizeEmployee) : [];
            setEmployees(normalized);
            if (user && normalized.length > 0) {
                const found = normalized.find((e: Employee) =>
                    e.email?.toLowerCase() === user.email?.toLowerCase() ||
                    e.userId === user.id
                );
                if (found) setSelfEmployeeId(found.id);
            }
        } catch (error) {
            console.error('Error fetching employees:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            if (!editingEmployee) {
                if (!formData.password) {
                    toast.error('Initial password is required.');
                    return;
                }
                const passwordResult = validatePassword(formData.password, {
                    email: formData.email,
                    name: `${formData.firstName} ${formData.lastName}`
                });
                if (!passwordResult.isValid) {
                    toast.error(passwordResult.message);
                    return;
                }
            }

            const employeeData: any = {
                ...formData,
                hourlyRate: formData.hourlyRate ? parseFloat(formData.hourlyRate) : undefined,
                salary: formData.salary ? parseFloat(formData.salary) : undefined,
            };
            if (editingEmployee) {
                delete employeeData.password;
            }

            // Add Bar License if role is attorney
            if ([EmployeeRole.PARTNER, EmployeeRole.ASSOCIATE, EmployeeRole.OF_COUNSEL].includes(formData.role as EmployeeRole)) {
                if (formData.barNumber) {
                    employeeData.barLicense = {
                        jurisdiction: formData.barState,
                        barNumber: formData.barNumber,
                        admissionDate: formData.barAdmissionDate,
                        status: formData.barStatus
                    };
                }
            }

            let createdOrUpdatedId: string | null = null;
            if (editingEmployee) {
                await api.updateEmployee(editingEmployee.id, employeeData);
                createdOrUpdatedId = editingEmployee.id;
                toast.success('Staff member updated successfully.');
            } else {
                const res = await api.createEmployee(employeeData);
                createdOrUpdatedId = res?.id || null;
                if (res && res.tempPassword) {
                    setCreatedCredential({
                        email: res.email,
                        pass: res.tempPassword,
                        role: roleLabels[res.role as EmployeeRole]
                    });
                    setCredentialModalOpen(true);
                } else {
                    toast.success('Staff member added successfully.');
                }
            }

            // Upload avatar if selected and we have an ID
            if (avatarFile && createdOrUpdatedId) {
                try {
                    await api.uploadEmployeeAvatar(createdOrUpdatedId, avatarFile);
                    toast.success('Profile photo updated.');
                } catch (err) {
                    console.error('Avatar upload failed', err);
                    toast.error('Could not upload profile photo.');
                }
            }

            setShowForm(false);
            setEditingEmployee(null);
            resetForm();
            handleAvatarChange(null);
            await fetchEmployees();
        } catch (error) {
            console.error('Error saving employee:', error);
            toast.error('Error saving staff member');
        }
    };

    const handleDeleteClick = (id: string) => {
        setEmployeeToDelete(id);
        setDeleteConfirmOpen(true);
    };

    const handleConfirmDelete = async () => {
        if (!employeeToDelete) return;

        try {
            await api.deleteEmployee(employeeToDelete);
            toast.success('Staff member deleted successfully.');
            fetchEmployees();
        } catch (error) {
            console.error('Error deleting employee:', error);
            toast.error('Failed to delete staff member.');
        } finally {
            setDeleteConfirmOpen(false);
            setEmployeeToDelete(null);
        }
    };

    const handleResetPassword = async (id: string) => {
        try {
            const result = await api.resetEmployeePassword(id);
            if (result?.tempPassword) {
                const emp = employees.find(e => e.id === id);
                if (emp) {
                    setCreatedCredential({
                        email: emp.email,
                        pass: result.tempPassword,
                        role: roleLabels[emp.role]
                    });
                    setCredentialModalOpen(true);
                }
            }
        } catch (error) {
            console.error('Error resetting password:', error);
        }
    };

    const openMessageModal = (emp: Employee) => {
        setMessageTarget(emp);
        setMessageModalOpen(true);
    };

    const handleSendMessage = async () => {
        if (!messageTarget || !messageBody.trim()) return;
        setSendingMessage(true);
        try {
            await api.staffMessages.send({
                recipientId: messageTarget.id,
                body: messageBody.trim()
            });
            toast.success('Message sent to staff member.');
            setMessageBody('');
            setMessageModalOpen(false);
        } catch (error) {
            console.error('Error sending message:', error);
            toast.error('Failed to send message.');
        } finally {
            setSendingMessage(false);
        }
    };

    const resetForm = () => {
        setFormData({
            firstName: '',
            lastName: '',
            email: '',
            phone: '',
            mobile: '',
            role: EmployeeRole.LEGAL_ASSISTANT,
            hireDate: new Date().toISOString().split('T')[0],
            hourlyRate: '',
            salary: '',
            notes: '',
            address: '',
            emergencyContact: '',
            emergencyPhone: '',
            password: '',
            barNumber: '',
            barState: 'NY',
            barAdmissionDate: '',
            barStatus: 'Active',
            entityId: '',
            officeId: ''
        });
    };

    const openEditForm = (emp: Employee) => {
        setEditingEmployee(emp);
        setFormData({
            firstName: emp.firstName,
            lastName: emp.lastName,
            email: emp.email,
            phone: emp.phone || '',
            mobile: emp.mobile || '',
            role: emp.role,
            hireDate: emp.hireDate?.split('T')[0] || '',
            hourlyRate: emp.hourlyRate?.toString() || '',
            salary: emp.salary?.toString() || '',
            notes: emp.notes || '',
            address: emp.address || '',
            emergencyContact: emp.emergencyContact || '',
            emergencyPhone: emp.emergencyPhone || '',
            password: '',
            barNumber: emp.barNumber || '',
            barState: emp.barJurisdiction || 'NY',
            barAdmissionDate: emp.barAdmissionDate ? emp.barAdmissionDate.split('T')[0] : '',
            barStatus: (emp.barStatus as string) || 'Active',
            entityId: emp.entityId || '',
            officeId: emp.officeId || ''
        });
        setShowForm(true);
        const avatarPath = getAvatarPath(emp);
        setAvatarPreview(avatarPath ? avatarUrls[avatarPath] || null : null);
        setAvatarPreviewIsTemp(false);
        setAvatarFile(null);
    };

    const filteredEmployees = employees.filter(e => {
        const roleMatch = selectedRole === 'all' || e.role === selectedRole;
        const search = searchTerm.trim().toLowerCase();
        const searchMatch = !search || `${e.firstName} ${e.lastName} ${e.email}`.toLowerCase().includes(search) || roleLabels[e.role].toLowerCase().includes(search);
        const entityMatch = !entityFilter || e.entityId === entityFilter;
        const officeMatch = !officeFilter || e.officeId === officeFilter;
        return roleMatch && searchMatch && entityMatch && officeMatch;
    });

    const roleCounts = employees.reduce((acc, e) => {
        acc[e.role] = (acc[e.role] || 0) + 1;
        return acc;
    }, {} as Record<string, number>);

    const stats = {
        total: employees.length,
        active: employees.filter(e => e.status === EmployeeStatus.ACTIVE).length,
        onLeave: employees.filter(e => e.status === EmployeeStatus.ON_LEAVE).length
    };

    if (loading) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
            </div>
        );
    }

    const handleAvatarChange = (file: File | null) => {
        if (!file) {
            setAvatarFile(null);
            setAvatarPreview(null);
            setAvatarPreviewIsTemp(false);
            return;
        }
        setAvatarFile(file);
        setAvatarPreview(URL.createObjectURL(file));
        setAvatarPreviewIsTemp(true);
    };

    return (
        <div className="p-6 h-full overflow-auto">
            <div className="max-w-6xl mx-auto">
                {/* Header */}
                <div className="bg-gradient-to-r from-blue-50 via-white to-purple-50 border border-gray-200 rounded-2xl p-5 mb-6 shadow-sm">
                    <div className="flex flex-wrap items-center justify-between gap-4">
                        <div>
                            <h2 className="text-2xl font-bold text-gray-900">{t('employees_title') || 'Staff Management'}</h2>
                            <p className="text-gray-600 mt-1">{t('employees_subtitle') || 'Manage attorneys, paralegals, and support staff.'}</p>
                        </div>
                        <div className="flex items-center gap-3 flex-wrap">
                            <div className="relative">
                                <Search className="w-4 h-4 text-gray-400 absolute left-3 top-2.5" />
                                <input
                                    value={searchTerm}
                                    onChange={(e) => setSearchTerm(e.target.value)}
                                    placeholder="Search name, role, email"
                                    className="pl-9 pr-3 py-2 bg-white border border-gray-200 rounded-lg text-sm shadow-inner focus:ring-2 focus:ring-blue-200 focus:border-blue-400 min-w-[220px]"
                                />
                            </div>
                            <EntityOfficeFilter
                                entityId={entityFilter}
                                officeId={officeFilter}
                                onEntityChange={setEntityFilter}
                                onOfficeChange={setOfficeFilter}
                                allowAll
                            />
                            <div className="flex items-center bg-white border border-gray-200 rounded-lg overflow-hidden shadow-sm">
                                <button
                                    className={`px-3 py-2 flex items-center gap-1 text-sm ${viewMode === 'grid' ? 'bg-blue-600 text-white' : 'text-gray-600 hover:bg-gray-50'}`}
                                    onClick={() => setViewMode('grid')}
                                    type="button"
                                >
                                    <LayoutGrid className="w-4 h-4" />
                                    Grid
                                </button>
                                <button
                                    className={`px-3 py-2 flex items-center gap-1 text-sm border-l border-gray-200 ${viewMode === 'list' ? 'bg-blue-600 text-white' : 'text-gray-600 hover:bg-gray-50'}`}
                                    onClick={() => setViewMode('list')}
                                    type="button"
                                >
                                    <List className="w-4 h-4" />
                                    List
                                </button>
                            </div>
                            <Can perform="user.manage">
                                <button
                                    onClick={() => { setShowForm(true); setEditingEmployee(null); resetForm(); handleAvatarChange(null); }}
                                    className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors shadow"
                                >
                                    <Plus className="w-5 h-5" />
                                    {t('add_employee') || '+ Add Staff'}
                                </button>
                            </Can>
                        </div>
                    </div>
                    <div className="mt-4 grid grid-cols-1 sm:grid-cols-3 gap-3">
                        <div className="bg-white border border-blue-100 rounded-xl px-4 py-3 shadow-inner">
                            <p className="text-xs uppercase text-gray-500">Total Team</p>
                            <div className="flex items-end justify-between mt-1">
                                <span className="text-2xl font-semibold text-gray-900">{stats.total}</span>
                                <span className="text-xs text-blue-600">All roles</span>
                            </div>
                        </div>
                        <div className="bg-white border border-green-100 rounded-xl px-4 py-3 shadow-inner">
                            <p className="text-xs uppercase text-gray-500">Active</p>
                            <div className="flex items-end justify-between mt-1">
                                <span className="text-2xl font-semibold text-green-700">{stats.active}</span>
                                <span className="text-xs text-green-600">Available</span>
                            </div>
                        </div>
                        <div className="bg-white border border-amber-100 rounded-xl px-4 py-3 shadow-inner">
                            <p className="text-xs uppercase text-gray-500">On Leave</p>
                            <div className="flex items-end justify-between mt-1">
                                <span className="text-2xl font-semibold text-amber-700">{stats.onLeave}</span>
                                <span className="text-xs text-amber-600">Off duty</span>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Role Summary */}
                <div className="grid grid-cols-4 gap-4 mb-6">
                    {Object.entries(roleLabels).map(([role, label]) => (
                        <button
                            key={role}
                            onClick={() => setSelectedRole(selectedRole === role ? 'all' : role)}
                            className={`p-4 rounded-xl border transition-all ${selectedRole === role
                                ? 'bg-blue-50 border-blue-300'
                                : 'bg-white border-gray-200 hover:border-gray-300'
                                }`}
                        >
                            <div className="flex items-center gap-3">
                                <span className="text-2xl">{roleIcons[role as EmployeeRole]}</span>
                                <div className="text-left">
                                    <p className="font-medium text-gray-900">{label}</p>
                                    <p className="text-sm text-gray-500">{roleCounts[role] || 0} {t('staff_count') || 'members'}</p>
                                </div>
                            </div>
                        </button>
                    ))}
                </div>

                {/* Form Modal */}
                {showForm && (
                    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                        <div className="bg-white rounded-xl shadow-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                            <div className="p-6 border-b border-gray-200">
                                <h3 className="text-lg font-semibold">
                                    {editingEmployee ? 'Edit Staff Member' : 'Add New Staff Member'}
                                </h3>
                            </div>
                            <form onSubmit={handleSubmit} className="p-6 space-y-4">
                                <div className="flex items-center gap-4">
                                    <div className="w-14 h-14 rounded-full bg-gradient-to-br from-blue-500 to-indigo-600 text-white font-semibold flex items-center justify-center overflow-hidden">
                                        {avatarPreview ? (
                                            <img src={avatarPreview} alt="avatar preview" className="w-full h-full object-cover" />
                                        ) : (
                                            <span>{formData.firstName?.[0]}{formData.lastName?.[0]}</span>
                                        )}
                                    </div>
                                    <div className="space-y-2">
                                        <label className="block text-sm font-medium text-gray-700">Profile Photo</label>
                                        <input
                                            type="file"
                                            accept="image/*"
                                            onChange={(e) => handleAvatarChange(e.target.files?.[0] || null)}
                                            className="text-sm"
                                        />
                                        <p className="text-xs text-gray-500">JPG/PNG, max ~2MB</p>
                                    </div>
                                </div>
                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('first_name') || 'First Name'} *</label>
                                        <input
                                            type="text"
                                            value={formData.firstName}
                                            onChange={(e) => setFormData({ ...formData, firstName: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            required
                                        />
                                    </div>
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('last_name') || 'Last Name'} *</label>
                                        <input
                                            type="text"
                                            value={formData.lastName}
                                            onChange={(e) => setFormData({ ...formData, lastName: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            required
                                        />
                                    </div>
                                </div>

                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Email *</label>
                                        <input
                                            type="email"
                                            value={formData.email}
                                            onChange={(e) => setFormData({ ...formData, email: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            required
                                            disabled={!!editingEmployee}
                                        />
                                    </div>
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Role *</label>
                                        <select
                                            value={formData.role}
                                            onChange={(e) => setFormData({ ...formData, role: e.target.value as EmployeeRole })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        >
                                            {Object.entries(roleLabels).map(([val, lbl]) => (
                                                <option key={val} value={val}>{lbl}</option>
                                            ))}
                                        </select>
                                    </div>
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-2">Entity & Office</label>
                                    <EntityOfficeFilter
                                        entityId={formData.entityId}
                                        officeId={formData.officeId}
                                        onEntityChange={(value) => setFormData({ ...formData, entityId: value, officeId: '' })}
                                        onOfficeChange={(value) => setFormData({ ...formData, officeId: value })}
                                    />
                                </div>

                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                                        <input
                                            type="tel"
                                            value={formData.phone}
                                            onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        />
                                    </div>
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Mobile Phone</label>
                                        <input
                                            type="tel"
                                            value={formData.mobile}
                                            onChange={(e) => setFormData({ ...formData, mobile: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        />
                                    </div>
                                </div>

                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('hire_date') || 'Hire Date'}</label>
                                        <input
                                            type="date"
                                            value={formData.hireDate}
                                            onChange={(e) => setFormData({ ...formData, hireDate: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        />
                                    </div>
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('salary') || 'Salary / Wage'}</label>
                                        <input
                                            type="number"
                                            value={formData.salary}
                                            onChange={(e) => setFormData({ ...formData, salary: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            placeholder="$"
                                        />
                                    </div>
                                </div>

                                {!editingEmployee && (
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Initial Password *</label>
                                        <input
                                            type="password"
                                            value={formData.password}
                                            onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            placeholder={passwordRequirementsText}
                                        />
                                        <p className="mt-1 text-xs text-gray-500">{passwordRequirementsText}</p>
                                    </div>
                                )}

                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('emergency_contact') || 'Emergency Contact'}</label>
                                    <div className="grid grid-cols-2 gap-2">
                                        <input
                                            type="text"
                                            value={formData.emergencyContact}
                                            onChange={(e) => setFormData({ ...formData, emergencyContact: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            placeholder="Name"
                                        />
                                        <input
                                            type="tel"
                                            value={formData.emergencyPhone}
                                            onChange={(e) => setFormData({ ...formData, emergencyPhone: e.target.value })}
                                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                            placeholder="Phone"
                                        />
                                    </div>
                                </div>

                                {(formData.role === EmployeeRole.PARTNER || formData.role === EmployeeRole.ASSOCIATE || formData.role === EmployeeRole.OF_COUNSEL) && (
                                    <div className="bg-slate-50 p-4 rounded-lg border border-slate-200">
                                        <h4 className="font-medium text-slate-800 mb-3 flex items-center gap-2">
                                            Bar Admission
                                        </h4>
                                        <div className="grid grid-cols-2 gap-4">
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Bar Number</label>
                                                <input
                                                    type="text"
                                                    value={formData.barNumber}
                                                    onChange={(e) => setFormData({ ...formData, barNumber: e.target.value })}
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                                />
                                            </div>
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                                                <select
                                                    value={formData.barState}
                                                    onChange={(e) => setFormData({ ...formData, barState: e.target.value })}
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                                >
                                                    <option value="NY">New York</option>
                                                    <option value="CA">California</option>
                                                    <option value="TX">Texas</option>
                                                    <option value="FL">Florida</option>
                                                    <option value="NJ">New Jersey</option>
                                                    <option value="MA">Massachusetts</option>
                                                    <option value="DC">District of Columbia</option>
                                                </select>
                                            </div>
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Admission Date</label>
                                                <input
                                                    type="date"
                                                    value={formData.barAdmissionDate}
                                                    onChange={(e) => setFormData({ ...formData, barAdmissionDate: e.target.value })}
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                                />
                                            </div>
                                            <div>
                                                <label className="block text-sm font-medium text-gray-700 mb-1">Status</label>
                                                <select
                                                    value={formData.barStatus}
                                                    onChange={(e) => setFormData({ ...formData, barStatus: e.target.value })}
                                                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                                >
                                                    <option value="Active">Active</option>
                                                    <option value="Inactive">Inactive</option>
                                                    <option value="Suspended">Suspended</option>
                                                    <option value="Pending">Pending</option>
                                                </select>
                                            </div>
                                        </div>
                                    </div>
                                )}

                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">Notes</label>
                                    <textarea
                                        value={formData.notes}
                                        onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                        className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                        rows={2}
                                    />
                                </div>

                                <div className="flex justify-end gap-3 pt-4 border-t">
                                    <button
                                        type="button"
                                        onClick={() => { setShowForm(false); setEditingEmployee(null); handleAvatarChange(null); }}
                                        className="px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200"
                                    >
                                        Cancel
                                    </button>
                                    <button
                                        type="submit"
                                        className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                                    >
                                        Save
                                    </button>
                                </div>
                            </form>
                        </div>
                    </div>
                )}

                {/* Employee List */}
                {filteredEmployees.length === 0 ? (
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
                        <Users className="w-16 h-16 text-gray-300 mx-auto mb-4" />
                        <h3 className="text-lg font-medium text-gray-900 mb-2">{t('no_employees') || 'No staff members found'}</h3>
                        <p className="text-gray-500 mb-4">Start by adding staff to your firm.</p>
                        <button
                            onClick={() => { setShowForm(true); setEditingEmployee(null); resetForm(); handleAvatarChange(null); }}
                            className="inline-flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                        >
                            <Plus className="w-5 h-5" />
                            {t('add_first_employee') || 'Add First Staff Member'}
                        </button>
                    </div>
                ) : (
                    <div className={viewMode === 'grid' ? 'grid gap-4 md:grid-cols-2' : 'grid gap-3'}>
                        {filteredEmployees.map((emp) => {
                            const avatarPath = getAvatarPath(emp);
                            const avatarUrl = avatarPath ? avatarUrls[avatarPath] : null;
                            return (
                                <div key={emp.id} className={`bg-white rounded-xl shadow-sm border border-gray-200 p-5 hover:shadow-md transition-shadow ${viewMode === 'list' ? 'flex items-center justify-between gap-4' : ''}`}>
                                    <div className="flex items-center justify-between">
                                        <div className="flex items-center gap-4">
                                            <div className="w-12 h-12 rounded-full bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center text-white font-bold text-lg">
                                                {avatarUrl ? (
                                                    <img src={avatarUrl} alt={emp.firstName} className="w-full h-full object-cover rounded-full" />
                                                ) : (
                                                    <>{emp.firstName[0]}{emp.lastName[0]}</>
                                                )}
                                            </div>
                                            <div>
                                                <div className="flex items-center gap-3">
                                                    <h3 className="font-semibold text-gray-900">{emp.firstName} {emp.lastName}</h3>
                                                    <span className={`px-2 py-0.5 text-xs font-medium rounded-full ${statusColors[emp.status]}`}>
                                                        {statusLabels[emp.status]}
                                                    </span>
                                                </div>
                                                <div className="flex items-center gap-4 mt-1 text-sm text-gray-500">
                                                    <span className="flex items-center gap-1">
                                                        {roleIcons[emp.role]} {roleLabels[emp.role]}
                                                    </span>
                                                    <span className="flex items-center gap-1">
                                                        <Mail className="w-4 h-4" /> {emp.email}
                                                    </span>
                                                    {emp.barNumber && (
                                                        <span className="flex items-center gap-1 text-xs bg-slate-100 px-2 py-0.5 rounded text-slate-600">
                                                            Bar: {emp.barNumber} ({emp.barJurisdiction})
                                                        </span>
                                                    )}
                                                    {emp.phone && (
                                                        <span className="flex items-center gap-1">
                                                            <Phone className="w-4 h-4" /> {emp.phone}
                                                        </span>
                                                    )}
                                                </div>
                                            </div>
                                        </div>
                                        <div className="flex items-center gap-2">
                                            <button
                                                onClick={() => openMessageModal(emp)}
                                                className="px-3 py-2 text-sm font-medium text-blue-700 bg-blue-50 border border-blue-100 rounded-lg hover:bg-blue-100 transition-colors flex items-center gap-1"
                                            >
                                                <Send className="w-4 h-4" /> Message
                                            </button>
                                            <Can perform="user.manage">
                                                <button
                                                    onClick={() => handleResetPassword(emp.id)}
                                                    className="p-2 text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
                                                    title={t('reset_password') || 'Reset Password'}
                                                >
                                                    <RefreshCw className="w-5 h-5" />
                                                </button>
                                                <button
                                                    onClick={() => openEditForm(emp)}
                                                    className="p-2 text-gray-600 hover:bg-gray-100 rounded-lg transition-colors"
                                                >
                                                    <Edit className="w-5 h-5" />
                                                </button>
                                                <button
                                                    onClick={() => handleDeleteClick(emp.id)}
                                                    className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                                >
                                                    <Trash2 className="w-5 h-5" />
                                                </button>
                                            </Can>
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>

            {messageTarget && messageModalOpen && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg">
                        <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
                            <div>
                                <p className="text-xs uppercase text-gray-500">Direct message</p>
                                <h3 className="text-lg font-semibold text-gray-900">{messageTarget.firstName} {messageTarget.lastName}</h3>
                                <p className="text-sm text-gray-500">{messageTarget.email}</p>
                            </div>
                            <button
                                onClick={() => setMessageModalOpen(false)}
                                className="p-2 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-lg"
                            >
                                <X className="w-5 h-5" />
                            </button>
                        </div>
                        <div className="p-5 space-y-3">
                            <textarea
                                value={messageBody}
                                onChange={(e) => setMessageBody(e.target.value)}
                                placeholder="Write a quick note..."
                                className="w-full border border-gray-200 rounded-lg p-3 text-sm focus:ring-2 focus:ring-blue-200 focus:border-blue-400 min-h-[140px]"
                            />
                            <div className="flex items-center justify-end gap-2">
                                <button
                                    onClick={() => setMessageModalOpen(false)}
                                    className="px-4 py-2 text-sm font-medium text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200"
                                >
                                    Cancel
                                </button>
                                <button
                                    onClick={handleSendMessage}
                                    disabled={sendingMessage || !messageBody.trim()}
                                    className="px-4 py-2 text-sm font-semibold text-white bg-blue-600 rounded-lg hover:bg-blue-700 disabled:opacity-60 flex items-center gap-2"
                                >
                                    <Send className="w-4 h-4" /> {sendingMessage ? 'Sending...' : 'Send'}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            <ConfirmDialog
                isOpen={deleteConfirmOpen}
                title="Delete Staff"
                message="Are you sure you want to delete this staff member? This action cannot be undone."
                confirmLabel="Yes, Delete"
                cancelLabel="Cancel"
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeleteConfirmOpen(false)}
            />

            {createdCredential && (
                <CredentialModal
                    isOpen={credentialModalOpen}
                    onClose={() => setCredentialModalOpen(false)}
                    email={createdCredential.email}
                    tempPassword={createdCredential.pass}
                    role={createdCredential.role}
                />
            )}
        </div>
    );
};

export default Employees;
