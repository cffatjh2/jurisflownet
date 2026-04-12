'use client';

import { useState, useEffect } from 'react';
import { FileText, Plus, Trash2, Edit, Copy, ChevronUp, ChevronDown } from './Icons';
import { api } from '../services/api';
import { toast } from './Toast';

interface IntakeForm {
    id: string;
    name: string;
    description?: string;
    practiceArea?: string;
    fieldsJson: string;
    slug: string;
    isActive: boolean;
    isPublic: boolean;
    submissionCount: number;
    createdAt: string;
}

interface IntakeFormField {
    id: string;
    name: string;
    label: string;
    type: string;
    required: boolean;
    placeholder?: string;
    options?: string;
    order: number;
}

interface IntakeFormBuilderProps {
    formId?: string;
    onSave?: (form: IntakeForm) => void;
    onCancel?: () => void;
}

export default function IntakeFormBuilder({ formId, onSave, onCancel }: IntakeFormBuilderProps) {
    const [form, setForm] = useState<Partial<IntakeForm>>({
        name: '',
        description: '',
        practiceArea: '',
        isPublic: true,
        isActive: true
    });
    const [fields, setFields] = useState<IntakeFormField[]>([]);
    const [loading, setLoading] = useState(!!formId);
    const [saving, setSaving] = useState(false);

    const [editingField, setEditingField] = useState<IntakeFormField | null>(null);
    const [showFieldModal, setShowFieldModal] = useState(false);

    const fieldTypes = [
        { value: 'text', label: 'Text Input' },
        { value: 'email', label: 'Email' },
        { value: 'phone', label: 'Phone' },
        { value: 'textarea', label: 'Text Area' },
        { value: 'select', label: 'Dropdown' },
        { value: 'checkbox', label: 'Checkbox' },
        { value: 'radio', label: 'Radio Buttons' },
        { value: 'date', label: 'Date' },
        { value: 'file', label: 'File Upload', disabled: true }
    ];

    const practiceAreas = [
        'Personal Injury', 'Family Law', 'Criminal Defense', 'Immigration',
        'Business Law', 'Real Estate', 'Estate Planning', 'Bankruptcy',
        'Employment Law', 'General Practice'
    ];

    useEffect(() => {
        if (formId) {
            loadForm();
        }
    }, [formId]);

    const loadForm = async () => {
        if (!formId) return;
        setLoading(true);
        try {
            const data = await api.intake.forms.get(formId);
            setForm(data);
            setFields(JSON.parse(data.fieldsJson || '[]'));
        } catch (error) {
            console.error('Failed to load form:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        if (!form.name) return;

        setSaving(true);
        try {
            const fieldsJson = JSON.stringify(fields);

            if (formId) {
                const updated = await api.intake.forms.update(formId, {
                    ...form,
                    fieldsJson
                });
                onSave?.(updated);
            } else {
                const created = await api.intake.forms.create({
                    name: form.name,
                    description: form.description,
                    practiceArea: form.practiceArea,
                    fieldsJson,
                    isPublic: form.isPublic,
                    isActive: form.isActive
                });
                onSave?.(created);
            }
        } catch (error) {
            console.error('Failed to save form:', error);
        } finally {
            setSaving(false);
        }
    };

    const handleCopyLink = async () => {
        if (!form.slug) return;

        const baseUrl = typeof window !== 'undefined' ? window.location.origin : '';
        const url = `${baseUrl}/intake/${form.slug}`;
        try {
            await navigator.clipboard.writeText(url);
            toast.success('Public intake link copied.');
        } catch (error) {
            console.error('Failed to copy intake link:', error);
            toast.error('Failed to copy intake link.');
        }
    };

    const handleFieldTypeSelect = (type: string) => {
        const selectedType = fieldTypes.find(fieldType => fieldType.value === type);
        if (selectedType?.disabled) {
            toast.info('File uploads are coming soon. Use a text field to collect document instructions for now.');
            return;
        }

        addField(type);
    };

    const addField = (type: string) => {
        const newField: IntakeFormField = {
            id: Math.random().toString(36).substr(2, 9),
            name: `field_${fields.length + 1}`,
            label: `New ${type} field`,
            type,
            required: false,
            order: fields.length
        };
        setFields([...fields, newField]);
        setEditingField(newField);
        setShowFieldModal(true);
    };

    const updateField = (updatedField: IntakeFormField) => {
        setFields(fields.map(f => f.id === updatedField.id ? updatedField : f));
        setShowFieldModal(false);
        setEditingField(null);
    };

    const removeField = (fieldId: string) => {
        setFields(fields.filter(f => f.id !== fieldId));
    };

    const moveField = (index: number, direction: 'up' | 'down') => {
        if (direction === 'up' && index === 0) return;
        if (direction === 'down' && index === fields.length - 1) return;

        const newFields = [...fields];
        const swapIndex = direction === 'up' ? index - 1 : index + 1;
        [newFields[index], newFields[swapIndex]] = [newFields[swapIndex], newFields[index]];
        setFields(newFields);
    };

    if (loading) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="w-8 h-8 border-2 border-blue-200 border-t-blue-600 rounded-full animate-spin" />
            </div>
        );
    }

    return (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
            {/* Header */}
            <div className="px-6 py-4 border-b bg-gradient-to-r from-purple-50 to-pink-50">
                <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-full bg-purple-100 flex items-center justify-center">
                        <FileText className="w-5 h-5 text-purple-600" />
                    </div>
                    <div>
                        <h2 className="text-lg font-semibold text-slate-800">
                            {formId ? 'Edit Intake Form' : 'Create Intake Form'}
                        </h2>
                        <p className="text-sm text-slate-500">Design your client intake form</p>
                    </div>
                </div>
            </div>

            <div className="grid grid-cols-2 divide-x">
                {/* Form Settings */}
                <div className="p-6 space-y-4">
                    <h3 className="font-medium text-slate-800">Form Settings</h3>

                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">Form Name *</label>
                        <input
                            type="text"
                            value={form.name}
                            onChange={(e) => setForm({ ...form, name: e.target.value })}
                            placeholder="e.g., Personal Injury Intake"
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">Description</label>
                        <textarea
                            value={form.description || ''}
                            onChange={(e) => setForm({ ...form, description: e.target.value })}
                            placeholder="Brief description of the form..."
                            rows={2}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">Practice Area</label>
                        <select
                            value={form.practiceArea || ''}
                            onChange={(e) => setForm({ ...form, practiceArea: e.target.value })}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                        >
                            <option value="">Select practice area...</option>
                            {practiceAreas.map(area => (
                                <option key={area} value={area}>{area}</option>
                            ))}
                        </select>
                    </div>

                    <div className="flex items-center gap-4">
                        <label className="flex items-center gap-2 cursor-pointer">
                            <input
                                type="checkbox"
                                checked={form.isPublic}
                                onChange={(e) => setForm({ ...form, isPublic: e.target.checked })}
                                className="rounded"
                            />
                            <span className="text-sm">Public Form</span>
                        </label>
                        <label className="flex items-center gap-2 cursor-pointer">
                            <input
                                type="checkbox"
                                checked={form.isActive}
                                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                                className="rounded"
                            />
                            <span className="text-sm">Active</span>
                        </label>
                    </div>

                    {form.slug && (
                        <div className="p-3 bg-slate-50 rounded-lg">
                            <p className="text-xs text-slate-500 mb-1">Public URL</p>
                            <div className="flex items-center gap-2">
                                <code className="text-sm text-blue-600 flex-1 truncate">
                                    /intake/{form.slug}
                                </code>
                                <button
                                    onClick={handleCopyLink}
                                    className="p-1 hover:bg-slate-200 rounded"
                                >
                                    <Copy className="w-4 h-4" />
                                </button>
                            </div>
                        </div>
                    )}
                </div>

                {/* Field Builder */}
                <div className="p-6">
                    <div className="flex items-center justify-between mb-4">
                        <h3 className="font-medium text-slate-800">Form Fields</h3>
                        <div className="flex gap-1">
                            {fieldTypes.slice(0, 4).map(type => (
                                <button
                                    key={type.value}
                                    onClick={() => handleFieldTypeSelect(type.value)}
                                    className="px-2 py-1 text-xs border border-slate-200 rounded hover:bg-slate-50"
                                >
                                    + {type.label}
                                </button>
                            ))}
                        </div>
                    </div>

                    <div className="space-y-2 max-h-80 overflow-auto">
                        {fields.length === 0 ? (
                            <div className="text-center py-12 text-slate-400 border-2 border-dashed rounded-lg">
                                <Plus className="w-8 h-8 mx-auto mb-2 opacity-50" />
                                <p>Add fields to your form</p>
                                <p className="text-sm">Click the buttons above</p>
                            </div>
                        ) : (
                            fields.map((field, index) => (
                                <div
                                    key={field.id}
                                    className="flex items-center gap-2 p-3 bg-slate-50 rounded-lg group"
                                >
                                    <div className="flex flex-col">
                                        <button
                                            onClick={() => moveField(index, 'up')}
                                            className="text-slate-400 hover:text-slate-600"
                                            aria-label="Move field up"
                                        >
                                            <ChevronUp className="w-4 h-4" />
                                        </button>
                                        <button
                                            onClick={() => moveField(index, 'down')}
                                            className="text-slate-400 hover:text-slate-600"
                                            aria-label="Move field down"
                                        >
                                            <ChevronDown className="w-4 h-4" />
                                        </button>
                                    </div>
                                    <div className="flex-1">
                                        <p className="font-medium text-sm">{field.label}</p>
                                        <p className="text-xs text-slate-500">
                                            {field.type}{field.required ? ' - Required' : ''}
                                        </p>
                                    </div>
                                    <button
                                        onClick={() => {
                                            setEditingField(field);
                                            setShowFieldModal(true);
                                        }}
                                        className="p-1 opacity-0 group-hover:opacity-100 hover:bg-slate-200 rounded"
                                    >
                                        <Edit className="w-4 h-4" />
                                    </button>
                                    <button
                                        onClick={() => removeField(field.id)}
                                        className="p-1 opacity-0 group-hover:opacity-100 hover:bg-red-100 text-red-500 rounded"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </button>
                                </div>
                            ))
                        )}
                    </div>
                </div>
            </div>

            {/* Footer */}
            <div className="flex items-center justify-end gap-3 px-6 py-4 border-t bg-slate-50">
                {onCancel && (
                    <button
                        onClick={onCancel}
                        className="px-4 py-2 text-slate-600 hover:bg-slate-100 rounded-lg"
                    >
                        Cancel
                    </button>
                )}
                <button
                    onClick={handleSave}
                    disabled={saving || !form.name}
                    className="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50 transition"
                >
                    {saving ? 'Saving...' : formId ? 'Update Form' : 'Create Form'}
                </button>
            </div>

            {/* Field Edit Modal */}
            {showFieldModal && editingField && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl p-6 w-full max-w-md">
                        <h3 className="text-lg font-semibold mb-4">Edit Field</h3>
                        <div className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-slate-700 mb-1">Label</label>
                                <input
                                    type="text"
                                    value={editingField.label}
                                    onChange={(e) => setEditingField({ ...editingField, label: e.target.value })}
                                    className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-slate-700 mb-1">Field Name</label>
                                <input
                                    type="text"
                                    value={editingField.name}
                                    onChange={(e) => setEditingField({ ...editingField, name: e.target.value })}
                                    className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-slate-700 mb-1">Type</label>
                                <select
                                    value={editingField.type}
                                    onChange={(e) => setEditingField({ ...editingField, type: e.target.value })}
                                    className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                                >
                                    {fieldTypes.map(type => (
                                        <option key={type.value} value={type.value} disabled={type.disabled}>
                                            {type.disabled ? `${type.label} (Coming soon)` : type.label}
                                        </option>
                                    ))}
                                </select>
                            </div>
                            {editingField.type === 'file' && (
                                <div className="p-3 rounded-lg border border-amber-200 bg-amber-50 text-sm text-amber-800">
                                    File uploads are coming soon. Replace this field with a supported type before publishing the form.
                                </div>
                            )}
                            <div>
                                <label className="block text-sm font-medium text-slate-700 mb-1">Placeholder</label>
                                <input
                                    type="text"
                                    value={editingField.placeholder || ''}
                                    onChange={(e) => setEditingField({ ...editingField, placeholder: e.target.value })}
                                    className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                                />
                            </div>
                            {['select', 'radio'].includes(editingField.type) && (
                                <div>
                                    <label className="block text-sm font-medium text-slate-700 mb-1">Options (one per line)</label>
                                    <textarea
                                        value={editingField.options || ''}
                                        onChange={(e) => setEditingField({ ...editingField, options: e.target.value })}
                                        rows={3}
                                        className="w-full px-4 py-2 border border-slate-200 rounded-lg"
                                    />
                                </div>
                            )}
                            <label className="flex items-center gap-2 cursor-pointer">
                                <input
                                    type="checkbox"
                                    checked={editingField.required}
                                    onChange={(e) => setEditingField({ ...editingField, required: e.target.checked })}
                                    className="rounded"
                                />
                                <span className="text-sm">Required field</span>
                            </label>
                        </div>
                        <div className="flex gap-2 mt-6">
                            <button
                                onClick={() => updateField(editingField)}
                                className="flex-1 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700"
                            >
                                Save Field
                            </button>
                            <button
                                onClick={() => {
                                    setShowFieldModal(false);
                                    setEditingField(null);
                                }}
                                className="px-4 py-2 border border-slate-200 rounded-lg hover:bg-slate-50"
                            >
                                Cancel
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
