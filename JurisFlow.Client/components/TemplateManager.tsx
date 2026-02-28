import React, { useState, useEffect } from 'react';
import { FileText, Plus, Edit, Trash2, X, Eye, Save } from './Icons';
import { toast } from './Toast';

// Template type based on Prisma model
interface DocumentTemplate {
    id: string;
    name: string;
    category: string;
    description?: string;
    content: string;
    variables?: string;
    isActive: boolean;
    createdAt: string;
    updatedAt: string;
}

// Variable definition for templates
interface VariableDefinition {
    name: string;
    label: string;
    type: 'text' | 'date' | 'number' | 'select';
    options?: string[]; // For select type
    required?: boolean;
}

// Template categories
const CATEGORIES = [
    { value: 'petition', label: 'Petition' },
    { value: 'contract', label: 'Contract' },
    { value: 'notice', label: 'Notice' },
    { value: 'power_of_attorney', label: 'Power of Attorney' },
    { value: 'agreement', label: 'Agreement' },
    { value: 'letter', label: 'Letter' },
    { value: 'other', label: 'Other' },
];

// Common variables that can be used in templates
const COMMON_VARIABLES: VariableDefinition[] = [
    { name: 'client_name', label: 'Client Name', type: 'text', required: true },
    { name: 'client_address', label: 'Client Address', type: 'text' },
    { name: 'client_tc', label: 'Government ID Number', type: 'text' },
    { name: 'opponent_name', label: 'Opposing Party Name', type: 'text' },
    { name: 'case_number', label: 'Case Number', type: 'text' },
    { name: 'court_name', label: 'Court Name', type: 'text' },
    { name: 'date', label: 'Date', type: 'date', required: true },
    { name: 'amount', label: 'Amount', type: 'number' },
    { name: 'attorney_name', label: 'Attorney Name', type: 'text' },
    { name: 'bar_number', label: 'Bar Number', type: 'text' },
];

const TemplateManager: React.FC = () => {
    const [templates, setTemplates] = useState<DocumentTemplate[]>([]);
    const [loading, setLoading] = useState(true);
    const [showEditor, setShowEditor] = useState(false);
    const [editingTemplate, setEditingTemplate] = useState<DocumentTemplate | null>(null);
    const [previewMode, setPreviewMode] = useState(false);

    // Form state
    const [formData, setFormData] = useState({
        name: '',
        category: 'petition',
        description: '',
        content: '',
        variables: '[]',
        isActive: true,
    });

    // Load templates
    useEffect(() => {
        loadTemplates();
    }, []);

    const loadTemplates = async () => {
        setLoading(true);
        try {
            const res = await fetch('/api/templates');
            if (res.ok) {
                const data = await res.json();
                setTemplates(data);
            }
        } catch (error) {
            console.error('Failed to load templates:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleNewTemplate = () => {
        setEditingTemplate(null);
        setFormData({
            name: '',
            category: 'petition',
            description: '',
            content: '',
            variables: JSON.stringify(COMMON_VARIABLES.slice(0, 3), null, 2),
            isActive: true,
        });
        setShowEditor(true);
    };

    const handleEditTemplate = (template: DocumentTemplate) => {
        setEditingTemplate(template);
        setFormData({
            name: template.name,
            category: template.category,
            description: template.description || '',
            content: template.content,
            variables: template.variables || '[]',
            isActive: template.isActive,
        });
        setShowEditor(true);
    };

    const handleSaveTemplate = async () => {
        if (!formData.name || !formData.content) {
            toast.error('Name and content are required');
            return;
        }

        try {
            const url = editingTemplate
                ? `/api/templates/${editingTemplate.id}`
                : '/api/templates';
            const method = editingTemplate ? 'PUT' : 'POST';

            const res = await fetch(url, {
                method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(formData),
            });

            if (res.ok) {
                toast.success(editingTemplate ? 'Template updated' : 'Template created');
                setShowEditor(false);
                loadTemplates();
            } else {
                toast.error('Failed to save template');
            }
        } catch (error) {
            toast.error('Error saving template');
        }
    };

    const handleDeleteTemplate = async (id: string) => {
        if (!confirm('Are you sure you want to delete this template?')) return;

        try {
            const res = await fetch(`/api/templates/${id}`, { method: 'DELETE' });
            if (res.ok) {
                toast.success('Template deleted');
                loadTemplates();
            }
        } catch (error) {
            toast.error('Error deleting template');
        }
    };

    const insertVariable = (varName: string) => {
        const textarea = document.getElementById('template-content') as HTMLTextAreaElement;
        if (textarea) {
            const start = textarea.selectionStart;
            const end = textarea.selectionEnd;
            const before = formData.content.substring(0, start);
            const after = formData.content.substring(end);
            const newContent = `${before}{{${varName}}}${after}`;
            setFormData({ ...formData, content: newContent });
            // Reset cursor position after insert
            setTimeout(() => {
                textarea.selectionStart = textarea.selectionEnd = start + varName.length + 4;
                textarea.focus();
            }, 0);
        }
    };

    const getCategoryLabel = (value: string) => {
        return CATEGORIES.find(c => c.value === value)?.label || value;
    };

    return (
        <div className="h-full flex flex-col bg-gray-50/50">
            {/* Header */}
            <div className="px-6 py-4 border-b border-gray-200 bg-white">
                <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <FileText className="w-6 h-6 text-slate-800" />
                        <div>
                            <h1 className="text-2xl font-bold text-slate-800">Document Templates</h1>
                            <p className="text-sm text-gray-500">Create and manage reusable legal document templates</p>
                        </div>
                    </div>
                    <button
                        onClick={handleNewTemplate}
                        className="flex items-center gap-2 px-4 py-2.5 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 transition-colors"
                    >
                        <Plus className="w-4 h-4" />
                        New Template
                    </button>
                </div>
            </div>

            {/* Template List */}
            <div className="flex-1 overflow-y-auto p-8">
                {loading ? (
                    <div className="text-center py-12 text-gray-500">Loading templates...</div>
                ) : templates.length === 0 ? (
                    <div className="text-center py-12">
                        <FileText className="w-12 h-12 text-gray-300 mx-auto mb-4" />
                        <h3 className="text-lg font-semibold text-gray-600 mb-2">No templates yet</h3>
                        <p className="text-gray-500 mb-4">Create your first template to get started</p>
                        <button
                            onClick={handleNewTemplate}
                            className="px-4 py-2 bg-primary-600 text-white rounded-lg text-sm font-bold hover:bg-primary-700"
                        >
                            Create Template
                        </button>
                    </div>
                ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                        {templates.map(template => (
                            <div
                                key={template.id}
                                className="bg-white rounded-xl border border-gray-200 p-5 hover:shadow-md transition-shadow"
                            >
                                <div className="flex items-start justify-between mb-3">
                                    <div className="flex-1">
                                        <h3 className="font-bold text-slate-800 mb-1">{template.name}</h3>
                                        <span className="inline-block px-2 py-0.5 text-xs font-medium bg-primary-50 text-primary-700 rounded">
                                            {getCategoryLabel(template.category)}
                                        </span>
                                    </div>
                                    <div className={`w-2 h-2 rounded-full ${template.isActive ? 'bg-emerald-500' : 'bg-gray-300'}`} />
                                </div>

                                {template.description && (
                                    <p className="text-sm text-gray-600 mb-3 line-clamp-2">{template.description}</p>
                                )}

                                <div className="text-xs text-gray-400 mb-4">
                                    Updated: {new Date(template.updatedAt).toLocaleDateString()}
                                </div>

                                <div className="flex items-center gap-2">
                                    <button
                                        onClick={() => handleEditTemplate(template)}
                                        className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-slate-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
                                    >
                                        <Edit className="w-4 h-4" />
                                        Edit
                                    </button>
                                    <button
                                        onClick={() => handleDeleteTemplate(template.id)}
                                        className="p-2 text-red-500 hover:bg-red-50 rounded-lg transition-colors"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>

            {/* Template Editor Modal */}
            {showEditor && (
                <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
                    <div className="bg-white w-full max-w-5xl h-[90vh] rounded-xl shadow-2xl flex flex-col overflow-hidden">
                        {/* Editor Header */}
                        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
                            <h2 className="text-lg font-bold text-slate-800">
                                {editingTemplate ? 'Edit Template' : 'Create New Template'}
                            </h2>
                            <div className="flex items-center gap-2">
                                <button
                                    onClick={() => setPreviewMode(!previewMode)}
                                    className={`px-3 py-1.5 text-sm font-medium rounded-lg transition-colors ${previewMode
                                            ? 'bg-primary-100 text-primary-700'
                                            : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                                        }`}
                                >
                                    <Eye className="w-4 h-4 inline mr-1" />
                                    Preview
                                </button>
                                <button
                                    onClick={() => setShowEditor(false)}
                                    className="p-2 hover:bg-gray-100 rounded-lg"
                                >
                                    <X className="w-5 h-5 text-gray-500" />
                                </button>
                            </div>
                        </div>

                        {/* Editor Content */}
                        <div className="flex-1 flex overflow-hidden">
                            {/* Left Panel - Form */}
                            <div className="w-1/3 border-r border-gray-200 p-4 overflow-y-auto">
                                <div className="space-y-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Template Name *</label>
                                        <input
                                            type="text"
                                            value={formData.name}
                                            onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                                            placeholder="e.g., Standard Petition"
                                        />
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Category</label>
                                        <select
                                            value={formData.category}
                                            onChange={(e) => setFormData({ ...formData, category: e.target.value })}
                                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                                        >
                                            {CATEGORIES.map(cat => (
                                                <option key={cat.value} value={cat.value}>{cat.label}</option>
                                            ))}
                                        </select>
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
                                        <textarea
                                            value={formData.description}
                                            onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                                            rows={2}
                                            placeholder="Brief description of this template"
                                        />
                                    </div>

                                    <div className="flex items-center gap-2">
                                        <input
                                            type="checkbox"
                                            id="isActive"
                                            checked={formData.isActive}
                                            onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                                            className="w-4 h-4"
                                        />
                                        <label htmlFor="isActive" className="text-sm text-gray-700">Active</label>
                                    </div>

                                    {/* Variable Insert Buttons */}
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-2">Insert Variable</label>
                                        <div className="flex flex-wrap gap-1.5">
                                            {COMMON_VARIABLES.map(v => (
                                                <button
                                                    key={v.name}
                                                    onClick={() => insertVariable(v.name)}
                                                    className="px-2 py-1 text-xs bg-gray-100 text-gray-700 rounded hover:bg-gray-200 transition-colors"
                                                    title={v.label}
                                                >
                                                    {`{{${v.name}}}`}
                                                </button>
                                            ))}
                                        </div>
                                    </div>
                                </div>
                            </div>

                            {/* Right Panel - Content Editor */}
                            <div className="flex-1 p-4 flex flex-col">
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    Template Content *
                                    <span className="text-gray-400 font-normal ml-2">Use {`{{variable_name}}`} for dynamic content</span>
                                </label>
                                {previewMode ? (
                                    <div className="flex-1 border border-gray-200 rounded-lg p-4 bg-gray-50 overflow-y-auto">
                                        <div className="prose prose-sm max-w-none whitespace-pre-wrap">
                                            {formData.content
                                                .replace(/\{\{client_name\}\}/g, '<span class="bg-yellow-100 px-1 rounded">Alex Johnson</span>')
                                                .replace(/\{\{date\}\}/g, '<span class="bg-yellow-100 px-1 rounded">' + new Date().toLocaleDateString('en-US') + '</span>')
                                                .replace(/\{\{([^}]+)\}\}/g, '<span class="bg-yellow-100 px-1 rounded">[$1]</span>')
                                            }
                                        </div>
                                    </div>
                                ) : (
                                    <textarea
                                        id="template-content"
                                        value={formData.content}
                                        onChange={(e) => setFormData({ ...formData, content: e.target.value })}
                                        className="flex-1 border border-gray-300 rounded-lg p-3 text-sm font-mono resize-none"
                                        placeholder={`Example:

{{court_name}} SUPERIOR COURT

PLAINTIFF : {{client_name}}
            {{client_address}}

DEFENDANT : {{opponent_name}}

RE        : ...

STATEMENT OF FACTS:
1. ...

DATE      : {{date}}

ATTORNEY FOR PLAINTIFF
{{attorney_name}}
{{bar_number}}`}
                                    />
                                )}
                            </div>
                        </div>

                        {/* Editor Footer */}
                        <div className="px-6 py-4 border-t border-gray-200 flex items-center justify-end gap-3">
                            <button
                                onClick={() => setShowEditor(false)}
                                className="px-4 py-2.5 text-sm font-medium text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
                            >
                                Cancel
                            </button>
                            <button
                                onClick={handleSaveTemplate}
                                className="flex items-center gap-2 px-4 py-2.5 text-sm font-bold text-white bg-slate-800 rounded-lg hover:bg-slate-900 transition-colors"
                            >
                                <Save className="w-4 h-4" />
                                Save Template
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default TemplateManager;
