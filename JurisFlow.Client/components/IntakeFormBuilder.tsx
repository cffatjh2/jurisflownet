'use client';

import { useEffect, useRef, useState, type ComponentType } from 'react';
import {
    Calendar,
    CheckCircle,
    CheckSquare,
    ChevronDown,
    Copy,
    Download,
    Edit,
    Eye,
    ExternalLink,
    File,
    FileText,
    Globe,
    GripVertical,
    LayoutGrid,
    Link2,
    Mail,
    Phone,
    Plus,
    RefreshCw,
    Save,
    Sparkles,
    Trash2
} from './Icons';
import { api } from '../services/api';
import { toast } from './Toast';
import {
    getCheckboxDefaultValue,
    getConditionalRuleValueOptions,
    getFieldDefaultValue,
    getVisibleFields,
    normalizeConditionalLogicFields,
    normalizeConditionalRuleValue,
    parseConditionalLogic,
    parseFieldOptions,
    serializeConditionalLogic,
    supportsConditionalSourceField
} from '../utils/intakeConditionalLogic';
import { downloadIntakeFormPdf } from '../utils/intakeFormPdf';
import { useConfirm } from './ConfirmDialog';

interface IntakeForm {
    id: string;
    name: string;
    description?: string;
    practiceArea?: string;
    fieldsJson: string;
    thankYouMessage?: string;
    redirectUrl?: string;
    notifyEmail?: string;
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
    helpText?: string;
    options?: string;
    defaultValue?: string;
    validationPattern?: string;
    validationMessage?: string;
    conditionalLogic?: string;
    order: number;
}

interface IntakeFormBuilderProps {
    formId?: string;
    onSave?: (form: IntakeForm) => void;
    onCancel?: () => void;
}

type PreviewViewport = 'desktop' | 'mobile';

type FieldTypeDefinition = {
    value: string;
    label: string;
    description: string;
    icon: ComponentType<{ className?: string }>;
    colorClass: string;
    disabled?: boolean;
};

type TemplatePresetDefinition = {
    id: string;
    name: string;
    description: string;
    practiceArea: string;
    formName: string;
    formDescription: string;
    thankYouMessage: string;
    icon: ComponentType<{ className?: string }>;
    accentClass: string;
    fields: Array<Partial<IntakeFormField> & Pick<IntakeFormField, 'name' | 'label' | 'type'>>;
};

const fieldTypes: FieldTypeDefinition[] = [
    {
        value: 'text',
        label: 'Text Input',
        description: 'Short answers for names, titles, and single-line responses.',
        icon: FileText,
        colorClass: 'bg-slate-100 text-slate-700'
    },
    {
        value: 'email',
        label: 'Email',
        description: 'Structured email capture with built-in format validation.',
        icon: Mail,
        colorClass: 'bg-blue-100 text-blue-700'
    },
    {
        value: 'phone',
        label: 'Phone',
        description: 'Phone number collection for callbacks and client screening.',
        icon: Phone,
        colorClass: 'bg-emerald-100 text-emerald-700'
    },
    {
        value: 'textarea',
        label: 'Text Area',
        description: 'Long-form detail capture for facts, summaries, and incident notes.',
        icon: FileText,
        colorClass: 'bg-indigo-100 text-indigo-700'
    },
    {
        value: 'select',
        label: 'Dropdown',
        description: 'Compact option picker for practice areas, urgency, and routing.',
        icon: ChevronDown,
        colorClass: 'bg-amber-100 text-amber-700'
    },
    {
        value: 'checkbox',
        label: 'Checkbox',
        description: 'Single yes/no acknowledgment or consent capture.',
        icon: CheckSquare,
        colorClass: 'bg-teal-100 text-teal-700'
    },
    {
        value: 'radio',
        label: 'Radio Buttons',
        description: 'Single-select choice list when all options should stay visible.',
        icon: CheckCircle,
        colorClass: 'bg-rose-100 text-rose-700'
    },
    {
        value: 'date',
        label: 'Date',
        description: 'Important dates like incident date, hearing date, or deadline.',
        icon: Calendar,
        colorClass: 'bg-cyan-100 text-cyan-700'
    },
    {
        value: 'file',
        label: 'File Upload',
        description: 'Coming soon. Keep visible in the builder, but not usable yet.',
        icon: File,
        colorClass: 'bg-amber-100 text-amber-700',
        disabled: true
    }
];

const practiceAreas = [
    'Personal Injury', 'Family Law', 'Criminal Defense', 'Immigration',
    'Business Law', 'Real Estate', 'Estate Planning', 'Bankruptcy',
    'Employment Law', 'General Practice'
];

const templatePresets: TemplatePresetDefinition[] = [
    {
        id: 'personal-injury',
        name: 'PI Rapid Screen',
        description: 'Fast triage for accident, treatment, and representation status.',
        practiceArea: 'Personal Injury',
        formName: 'Personal Injury Intake',
        formDescription: 'Help us understand the accident, the injuries involved, and how quickly your case needs attention.',
        thankYouMessage: 'Thank you for sharing your injury details. Our intake team will review your case and follow up shortly.',
        icon: Sparkles,
        accentClass: 'from-rose-50 via-orange-50 to-white text-rose-700',
        fields: [
            { name: 'full_name', label: 'Full Name', type: 'text', required: true, placeholder: 'Jane Doe' },
            { name: 'email', label: 'Email', type: 'email', required: true, placeholder: 'jane@example.com' },
            { name: 'phone', label: 'Phone Number', type: 'phone', required: true, placeholder: '(555) 555-5555' },
            { name: 'incident_date', label: 'Date of Incident', type: 'date', required: true },
            { name: 'injury_type', label: 'Injury Type', type: 'select', required: true, options: 'Car Accident\nSlip and Fall\nWork Injury\nMedical Malpractice\nOther' },
            { name: 'treatment_status', label: 'Medical Treatment Status', type: 'radio', required: true, options: 'Already received treatment\nTreatment scheduled\nNo treatment yet' },
            { name: 'has_attorney', label: 'Do you already have an attorney?', type: 'checkbox', placeholder: 'Yes, I am already represented' },
            {
                name: 'attorney_details',
                label: 'Current Attorney Details',
                type: 'textarea',
                helpText: 'Collect the current law firm only if the client is already represented.',
                conditionalLogic: JSON.stringify({ action: 'show', operator: 'equals', sourceField: 'has_attorney', value: 'true' })
            },
            { name: 'incident_summary', label: 'What happened?', type: 'textarea', required: true, placeholder: 'Briefly describe the accident and injuries.' }
        ]
    },
    {
        id: 'family-law',
        name: 'Family Law Intake',
        description: 'Structured screening for custody, divorce, and urgent hearing needs.',
        practiceArea: 'Family Law',
        formName: 'Family Law Consultation Request',
        formDescription: 'Share the core family-law issue, urgency, and family context before we schedule your consultation.',
        thankYouMessage: 'Your family-law request has been received. We will review the urgency and contact you with next steps.',
        icon: Globe,
        accentClass: 'from-blue-50 via-cyan-50 to-white text-blue-700',
        fields: [
            { name: 'full_name', label: 'Full Name', type: 'text', required: true, placeholder: 'Jane Doe' },
            { name: 'email', label: 'Email', type: 'email', required: true, placeholder: 'jane@example.com' },
            { name: 'phone', label: 'Phone Number', type: 'phone', required: true, placeholder: '(555) 555-5555' },
            { name: 'matter_type', label: 'Matter Type', type: 'select', required: true, options: 'Divorce\nCustody\nChild Support\nProtective Order\nPrenuptial Agreement' },
            { name: 'urgent_hearing', label: 'Do you have an urgent hearing or deadline?', type: 'checkbox', placeholder: 'Yes, there is an urgent court date or deadline' },
            {
                name: 'hearing_date',
                label: 'Hearing / Deadline Date',
                type: 'date',
                helpText: 'Shown only when the client indicates an urgent hearing or deadline.',
                conditionalLogic: JSON.stringify({ action: 'show', operator: 'equals', sourceField: 'urgent_hearing', value: 'true' })
            },
            { name: 'children_involved', label: 'Are children involved?', type: 'radio', required: true, options: 'Yes\nNo' },
            { name: 'opposing_party', label: 'Opposing Party Name', type: 'text', placeholder: 'Full name if known' },
            { name: 'case_summary', label: 'Case Summary', type: 'textarea', required: true, placeholder: 'Tell us what is happening and what outcome you need.' }
        ]
    },
    {
        id: 'general-consult',
        name: 'General Consult',
        description: 'Clean starting point for broad consultation requests and lead routing.',
        practiceArea: 'General Practice',
        formName: 'General Consultation Request',
        formDescription: 'Tell us what you need help with and how you would like our team to follow up.',
        thankYouMessage: 'Thank you for reaching out. We have your consultation request and will follow up soon.',
        icon: LayoutGrid,
        accentClass: 'from-emerald-50 via-teal-50 to-white text-emerald-700',
        fields: [
            { name: 'full_name', label: 'Full Name', type: 'text', required: true, placeholder: 'Jane Doe' },
            { name: 'email', label: 'Email', type: 'email', required: true, placeholder: 'jane@example.com' },
            { name: 'phone', label: 'Phone Number', type: 'phone', placeholder: '(555) 555-5555' },
            { name: 'practice_need', label: 'What do you need help with?', type: 'select', required: true, options: 'Personal Injury\nFamily Law\nCriminal Defense\nBusiness Law\nEstate Planning\nGeneral Question' },
            { name: 'preferred_contact', label: 'Preferred Contact Method', type: 'radio', required: true, options: 'Phone\nEmail' },
            { name: 'best_time', label: 'Best Time To Reach You', type: 'text', placeholder: 'Weekday afternoons, mornings, etc.' },
            { name: 'details', label: 'How can we help?', type: 'textarea', required: true, placeholder: 'Briefly describe the matter or question.' }
        ]
    }
];

const createFieldId = () => Math.random().toString(36).slice(2, 11);

const slugifyFieldName = (value: string) => {
    const normalized = value
        .toLowerCase()
        .trim()
        .replace(/[^a-z0-9]+/g, '_')
        .replace(/^_+|_+$/g, '');
    return normalized || 'field';
};

const reindexFields = (items: IntakeFormField[]) => items.map((field, index) => ({
    ...field,
    order: index
}));

const getFieldTypeDefinition = (type: string) =>
    fieldTypes.find(fieldType => fieldType.value === type) || fieldTypes[0];

const supportsChoiceOptions = (type: string) => ['select', 'radio'].includes(type);

const supportsPatternValidation = (type: string) => ['text', 'email', 'phone', 'textarea'].includes(type);

const isCheckboxDefaultEnabled = getCheckboxDefaultValue;

const getPreviewSelectValue = (field: IntakeFormField, options: string[]) => {
    const normalizedDefault = (field.defaultValue || '').trim();
    return options.includes(normalizedDefault) ? normalizedDefault : '';
};

const buildPreviewDefaults = (items: IntakeFormField[]) =>
    Object.fromEntries(items.map(field => [field.name, getFieldDefaultValue(field)]));

export default function IntakeFormBuilder({ formId, onSave, onCancel }: IntakeFormBuilderProps) {
    const { confirm } = useConfirm();
    const [form, setForm] = useState<Partial<IntakeForm>>({
        name: '',
        description: '',
        practiceArea: '',
        thankYouMessage: 'Thank you for your submission. We will contact you shortly.',
        redirectUrl: '',
        notifyEmail: '',
        isPublic: true,
        isActive: true
    });
    const [fields, setFields] = useState<IntakeFormField[]>([]);
    const [previewValues, setPreviewValues] = useState<Record<string, unknown>>({});
    const [previewViewport, setPreviewViewport] = useState<PreviewViewport>('desktop');
    const [loading, setLoading] = useState(!!formId);
    const [saving, setSaving] = useState(false);

    const [editingField, setEditingField] = useState<IntakeFormField | null>(null);
    const [showFieldModal, setShowFieldModal] = useState(false);
    const [isFieldPaletteOpen, setIsFieldPaletteOpen] = useState(false);
    const [draggedFieldId, setDraggedFieldId] = useState<string | null>(null);
    const [dragOverFieldId, setDragOverFieldId] = useState<string | null>(null);

    const fieldPaletteRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        if (formId) {
            loadForm();
        }
    }, [formId]);

    useEffect(() => {
        if (!isFieldPaletteOpen) {
            return;
        }

        const handlePointerDown = (event: MouseEvent) => {
            if (!fieldPaletteRef.current?.contains(event.target as Node)) {
                setIsFieldPaletteOpen(false);
            }
        };

        document.addEventListener('mousedown', handlePointerDown);
        return () => document.removeEventListener('mousedown', handlePointerDown);
    }, [isFieldPaletteOpen]);

    useEffect(() => {
        setPreviewValues(currentValues => {
            const nextValues: Record<string, unknown> = {};

            for (const field of fields) {
                if (Object.prototype.hasOwnProperty.call(currentValues, field.name)) {
                    nextValues[field.name] = currentValues[field.name];
                    continue;
                }

                nextValues[field.name] = getFieldDefaultValue(field);
            }

            return nextValues;
        });
    }, [fields]);

    const buildUniqueFieldName = (seed: string, fieldIdToIgnore?: string) => {
        const baseName = slugifyFieldName(seed);
        const usedNames = new Set(
            fields
                .filter(field => field.id !== fieldIdToIgnore)
                .map(field => field.name.toLowerCase())
        );

        let candidate = baseName;
        let suffix = 2;
        while (usedNames.has(candidate.toLowerCase())) {
            candidate = `${baseName}_${suffix}`;
            suffix += 1;
        }

        return candidate;
    };

    const normalizeBuilderFields = (items: IntakeFormField[]) =>
        normalizeConditionalLogicFields(reindexFields(items));

    const getAvailableConditionalSourceFields = (fieldId?: string) => {
        const targetIndex = fields.findIndex(field => field.id === fieldId);
        const availableFields = targetIndex >= 0 ? fields.slice(0, targetIndex) : fields;

        return availableFields.filter(field =>
            supportsConditionalSourceField(field.type) && getConditionalRuleValueOptions(field).length > 0
        );
    };

    const updateEditingFieldConditionalLogic = (nextConditionalLogic: string) => {
        setEditingField(currentField => currentField ? {
            ...currentField,
            conditionalLogic: nextConditionalLogic
        } : currentField);
    };

    const createDefaultConditionalRule = (sourceField?: IntakeFormField | null) => {
        if (!sourceField) {
            return null;
        }

        const valueOptions = getConditionalRuleValueOptions(sourceField);
        if (valueOptions.length === 0) {
            return null;
        }

        return {
            action: 'show' as const,
            operator: 'equals' as const,
            sourceField: sourceField.name,
            value: normalizeConditionalRuleValue(sourceField, valueOptions[0].value)
        };
    };

    const getConditionalSummary = (field: IntakeFormField) => {
        const rule = parseConditionalLogic(field.conditionalLogic);
        if (!rule) {
            return null;
        }

        const sourceField = fields.find(candidate => candidate.name.toLowerCase() === rule.sourceField.toLowerCase());
        if (!sourceField) {
            return 'Conditional visibility needs attention.';
        }

        const sourceLabel = sourceField.label || sourceField.name;
        const matchedOption = getConditionalRuleValueOptions(sourceField)
            .find(option => option.value === normalizeConditionalRuleValue(sourceField, rule.value));

        return `Show when ${sourceLabel} is ${matchedOption?.label || rule.value}.`;
    };

    const closeFieldModal = () => {
        setShowFieldModal(false);
        setEditingField(null);
    };

    const handleEditingFieldTypeChange = (type: string) => {
        setEditingField(currentField => {
            if (!currentField) {
                return currentField;
            }

            return {
                ...currentField,
                type,
                options: supportsChoiceOptions(type) ? currentField.options : '',
                validationPattern: supportsPatternValidation(type) ? currentField.validationPattern : '',
                validationMessage: supportsPatternValidation(type) ? currentField.validationMessage : '',
                defaultValue: type === 'checkbox'
                    ? (isCheckboxDefaultEnabled(currentField.defaultValue) ? 'true' : 'false')
                    : currentField.defaultValue
            };
        });
    };

    const loadForm = async () => {
        if (!formId) return;
        setLoading(true);
        try {
            const data = await api.intake.forms.get(formId);
            const parsedFields = JSON.parse(data.fieldsJson || '[]');
            const normalizedFields = Array.isArray(parsedFields)
                ? normalizeBuilderFields(
                    [...parsedFields].sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
                )
                : [];

            setForm(data);
            setFields(normalizedFields);
        } catch (error) {
            console.error('Failed to load form:', error);
            toast.error('Failed to load intake form.');
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        if (!form.name?.trim()) {
            toast.error('Form name is required.');
            return;
        }

        setSaving(true);
        try {
            const normalizedFields = normalizeBuilderFields(fields);
            const fieldsJson = JSON.stringify(normalizedFields);
            const updatePayload = {
                name: form.name.trim(),
                description: form.description ?? '',
                practiceArea: form.practiceArea ?? '',
                fieldsJson,
                thankYouMessage: form.thankYouMessage?.trim() || '',
                redirectUrl: form.redirectUrl?.trim() || '',
                notifyEmail: form.notifyEmail?.trim() || '',
                isPublic: Boolean(form.isPublic),
                isActive: Boolean(form.isActive)
            };

            if (formId) {
                const updated = await api.intake.forms.update(formId, updatePayload);
                onSave?.(updated);
            } else {
                const created = await api.intake.forms.create({
                    name: updatePayload.name,
                    description: form.description?.trim() || undefined,
                    practiceArea: form.practiceArea?.trim() || undefined,
                    fieldsJson,
                    thankYouMessage: form.thankYouMessage?.trim() || undefined,
                    redirectUrl: form.redirectUrl?.trim() || undefined,
                    notifyEmail: form.notifyEmail?.trim() || undefined,
                    isPublic: updatePayload.isPublic,
                    isActive: updatePayload.isActive
                });
                onSave?.(created);
            }
        } catch (error) {
            console.error('Failed to save form:', error);
            toast.error('Failed to save intake form.');
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

    const handleCopyEmbedCode = async () => {
        if (!form.slug) {
            toast.info('Save the form first to generate an embeddable snippet.');
            return;
        }

        const baseUrl = typeof window !== 'undefined' ? window.location.origin : '';
        const iframeTitle = form.name?.trim() || 'Intake Form';
        const embedCode = `<iframe src="${baseUrl}/intake/${form.slug}" title="${iframeTitle}" width="100%" height="960" style="border:0;border-radius:24px;overflow:hidden;" loading="lazy"></iframe>`;

        try {
            await navigator.clipboard.writeText(embedCode);
            toast.success('Embed code copied.');
        } catch (error) {
            console.error('Failed to copy embed code:', error);
            toast.error('Failed to copy embed code.');
        }
    };

    const handleDownloadPdf = async () => {
        try {
            await downloadIntakeFormPdf({
                ...form,
                fields,
                shareUrl
            });
            toast.success('Form PDF downloaded.');
        } catch (error) {
            console.error('Failed to download intake form PDF:', error);
            toast.error('Failed to download intake form PDF.');
        }
    };

    const resetPreviewToDefaults = () => {
        setPreviewValues(buildPreviewDefaults(fields));
        toast.success('Preview reset to default answers.');
    };

    const applyTemplatePreset = async (preset: TemplatePresetDefinition) => {
        const hasExistingContent = fields.length > 0 || Boolean(form.name?.trim()) || Boolean(form.description?.trim());
        if (hasExistingContent) {
            const confirmed = await confirm({
                title: 'Apply template preset?',
                message: `Apply "${preset.name}"? This will replace the current form layout.`,
                confirmText: 'Apply Preset',
                cancelText: 'Keep Current Form'
            });
            if (!confirmed) {
                return;
            }
        }

        const nextFields = normalizeBuilderFields(
            preset.fields.map((field, index) => ({
                id: createFieldId(),
                name: field.name,
                label: field.label,
                type: field.type,
                required: field.required ?? false,
                placeholder: field.placeholder || '',
                helpText: field.helpText || '',
                options: field.options || '',
                defaultValue: field.defaultValue || '',
                validationPattern: field.validationPattern || '',
                validationMessage: field.validationMessage || '',
                conditionalLogic: field.conditionalLogic || '',
                order: index
            }))
        );

        setForm(currentForm => ({
            ...currentForm,
            name: preset.formName,
            description: preset.formDescription,
            practiceArea: preset.practiceArea,
            thankYouMessage: preset.thankYouMessage
        }));
        setFields(nextFields);
        setPreviewValues(buildPreviewDefaults(nextFields));
        setIsFieldPaletteOpen(false);
        toast.success(`${preset.name} loaded.`);
    };

    const addField = (type: string) => {
        const fieldType = getFieldTypeDefinition(type);
        if (fieldType.disabled) {
            toast.info('File uploads are coming soon. Use a text field to collect document instructions for now.');
            return;
        }

        const newField: IntakeFormField = {
            id: createFieldId(),
            name: buildUniqueFieldName(type),
            label: fieldType.label,
            type,
            required: false,
            placeholder: '',
            helpText: '',
            conditionalLogic: '',
            order: fields.length
        };

        const nextFields = normalizeBuilderFields([...fields, newField]);
        setFields(nextFields);
        setEditingField(nextFields[nextFields.length - 1]);
        setShowFieldModal(true);
        setIsFieldPaletteOpen(false);
    };

    const updateField = (updatedField: IntakeFormField) => {
        const normalizedField: IntakeFormField = {
            ...updatedField,
            label: updatedField.label.trim() || 'Untitled Field',
            name: buildUniqueFieldName(updatedField.name || updatedField.label || updatedField.type, updatedField.id),
            placeholder: updatedField.placeholder || '',
            helpText: updatedField.helpText?.trim() || '',
            options: supportsChoiceOptions(updatedField.type) ? updatedField.options || '' : '',
            defaultValue: updatedField.defaultValue ?? '',
            validationPattern: supportsPatternValidation(updatedField.type) ? updatedField.validationPattern?.trim() || '' : '',
            validationMessage: supportsPatternValidation(updatedField.type) ? updatedField.validationMessage?.trim() || '' : '',
            conditionalLogic: updatedField.conditionalLogic || ''
        };

        setFields(currentFields => normalizeBuilderFields(
            currentFields.map(field => field.id === normalizedField.id ? normalizedField : field)
        ));
        closeFieldModal();
    };

    const removeField = (fieldId: string) => {
        setFields(currentFields => normalizeBuilderFields(currentFields.filter(field => field.id !== fieldId)));
    };

    const duplicateField = (fieldId: string) => {
        const index = fields.findIndex(field => field.id === fieldId);
        if (index === -1) {
            return;
        }

        const fieldToDuplicate = fields[index];
        const duplicatedField: IntakeFormField = {
            ...fieldToDuplicate,
            id: createFieldId(),
            name: buildUniqueFieldName(`${fieldToDuplicate.name}_copy`),
            label: `${fieldToDuplicate.label} Copy`
        };

        const nextFields = [...fields];
        nextFields.splice(index + 1, 0, duplicatedField);
        setFields(normalizeBuilderFields(nextFields));
        toast.success('Field duplicated.');
    };

    const toggleFieldRequired = (fieldId: string) => {
        setFields(currentFields => normalizeBuilderFields(
            currentFields.map(field => field.id === fieldId ? { ...field, required: !field.required } : field)
        ));
    };

    const handleDragStart = (fieldId: string) => {
        setDraggedFieldId(fieldId);
        setDragOverFieldId(fieldId);
    };

    const handleDrop = (targetFieldId: string) => {
        if (!draggedFieldId || draggedFieldId === targetFieldId) {
            setDragOverFieldId(null);
            setDraggedFieldId(null);
            return;
        }

        const sourceIndex = fields.findIndex(field => field.id === draggedFieldId);
        const targetIndex = fields.findIndex(field => field.id === targetFieldId);
        if (sourceIndex === -1 || targetIndex === -1) {
            setDragOverFieldId(null);
            setDraggedFieldId(null);
            return;
        }

        const nextFields = [...fields];
        const [movedField] = nextFields.splice(sourceIndex, 1);
        nextFields.splice(targetIndex, 0, movedField);

        setFields(normalizeBuilderFields(nextFields));
        setDragOverFieldId(null);
        setDraggedFieldId(null);
    };

    const handleDropAtEnd = () => {
        if (!draggedFieldId) {
            return;
        }

        const sourceIndex = fields.findIndex(field => field.id === draggedFieldId);
        if (sourceIndex === -1 || sourceIndex === fields.length - 1) {
            setDragOverFieldId(null);
            setDraggedFieldId(null);
            return;
        }

        const nextFields = [...fields];
        const [movedField] = nextFields.splice(sourceIndex, 1);
        nextFields.push(movedField);

        setFields(normalizeBuilderFields(nextFields));
        setDragOverFieldId(null);
        setDraggedFieldId(null);
    };

    const handlePreviewValueChange = (fieldName: string, value: unknown) => {
        setPreviewValues(currentValues => ({
            ...currentValues,
            [fieldName]: value
        }));
    };

    const renderPreviewField = (field: IntakeFormField) => {
        const baseInputClass = 'w-full rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700 shadow-sm';
        const options = parseFieldOptions(field.options);
        const rawPreviewValue = previewValues[field.name];
        const previewTextValue = typeof rawPreviewValue === 'string'
            ? rawPreviewValue
            : rawPreviewValue === undefined || rawPreviewValue === null
                ? ''
                : String(rawPreviewValue);

        switch (field.type) {
            case 'textarea':
                return (
                    <textarea
                        rows={4}
                        value={previewTextValue}
                        onChange={(event) => handlePreviewValueChange(field.name, event.target.value)}
                        placeholder={field.placeholder || 'Type your answer here'}
                        className={baseInputClass}
                    />
                );
            case 'select':
                return (
                    <select
                        value={typeof rawPreviewValue === 'string' ? rawPreviewValue : getPreviewSelectValue(field, options)}
                        onChange={(event) => handlePreviewValueChange(field.name, event.target.value)}
                        className={baseInputClass}
                    >
                        <option value="">{field.placeholder || 'Select an option'}</option>
                        {options.map(option => (
                            <option key={option} value={option}>{option}</option>
                        ))}
                    </select>
                );
            case 'radio':
                return (
                    <div className="space-y-2 rounded-xl border border-slate-200 bg-slate-50 p-3">
                        {(options.length > 0 ? options : ['Option 1', 'Option 2']).map(option => (
                            <label key={option} className="flex items-center gap-3 text-sm text-slate-700">
                                <input
                                    type="radio"
                                    name={`preview_${field.id}`}
                                    value={option}
                                    checked={rawPreviewValue === option}
                                    onChange={(event) => handlePreviewValueChange(field.name, event.target.value)}
                                    className="h-4 w-4 text-blue-600"
                                />
                                {option}
                            </label>
                        ))}
                    </div>
                );
            case 'checkbox':
                return (
                    <label className="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700">
                        <input
                            type="checkbox"
                            checked={Boolean(rawPreviewValue)}
                            onChange={(event) => handlePreviewValueChange(field.name, event.target.checked)}
                            className="h-4 w-4 rounded border-slate-300 text-blue-600"
                        />
                        {field.placeholder || 'Yes, I understand'}
                    </label>
                );
            case 'date':
                return (
                    <input
                        type="date"
                        value={previewTextValue}
                        onChange={(event) => handlePreviewValueChange(field.name, event.target.value)}
                        className={baseInputClass}
                    />
                );
            case 'file':
                return (
                    <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                        File uploads are coming soon.
                    </div>
                );
            default:
                return (
                    <input
                        type={field.type === 'email' ? 'email' : field.type === 'phone' ? 'tel' : 'text'}
                        value={previewTextValue}
                        onChange={(event) => handlePreviewValueChange(field.name, event.target.value)}
                        placeholder={field.placeholder || `Enter ${field.label.toLowerCase()}`}
                        className={baseInputClass}
                    />
                );
        }
    };

    if (loading) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="w-8 h-8 border-2 border-blue-200 border-t-blue-600 rounded-full animate-spin" />
            </div>
        );
    }

    const requiredFieldCount = fields.filter(field => field.required).length;
    const visiblePreviewFields = getVisibleFields(fields, previewValues);
    const hiddenPreviewFieldCount = Math.max(fields.length - visiblePreviewFields.length, 0);
    const shareUrl = form.slug
        ? `${typeof window !== 'undefined' ? window.location.origin : 'https://your-domain.com'}/intake/${form.slug}`
        : '';
    const embedCode = shareUrl
        ? `<iframe src="${shareUrl}" title="${form.name?.trim() || 'Intake Form'}" width="100%" height="960" style="border:0;border-radius:24px;overflow:hidden;" loading="lazy"></iframe>`
        : '';
    const availableConditionalSourceFields = getAvailableConditionalSourceFields(editingField?.id);
    const editingConditionalRule = editingField ? parseConditionalLogic(editingField.conditionalLogic) : null;
    const selectedConditionalSourceField = editingConditionalRule
        ? availableConditionalSourceFields.find(field => field.name.toLowerCase() === editingConditionalRule.sourceField.toLowerCase()) || null
        : null;
    const conditionalValueOptions = selectedConditionalSourceField
        ? getConditionalRuleValueOptions(selectedConditionalSourceField)
        : [];
    const previewFrameWidthClass = previewViewport === 'mobile' ? 'mx-auto max-w-[320px]' : '';
    const previewViewportLabel = previewViewport === 'mobile' ? 'Mobile' : 'Desktop';

    return (
        <div className="flex h-full min-h-0 flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div className="border-b border-slate-200 bg-gradient-to-r from-slate-50 via-blue-50 to-cyan-50 px-6 py-5">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex items-center gap-3">
                        <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-white text-blue-700 shadow-sm ring-1 ring-slate-200">
                            <FileText className="w-5 h-5" />
                        </div>
                        <div>
                            <h2 className="text-lg font-semibold text-slate-900">
                                {formId ? 'Edit Intake Form' : 'Create Intake Form'}
                            </h2>
                            <p className="text-sm text-slate-600">
                                Build the public intake experience with live layout feedback.
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-col gap-3 xl:items-end">
                        <div className="flex flex-wrap items-center gap-2 text-xs font-semibold">
                            <span className="rounded-full bg-white px-3 py-1.5 text-slate-600 ring-1 ring-slate-200">
                                {fields.length} fields
                            </span>
                            <span className="rounded-full bg-white px-3 py-1.5 text-slate-600 ring-1 ring-slate-200">
                                {requiredFieldCount} required
                            </span>
                            <span className={`rounded-full px-3 py-1.5 ring-1 ${form.isPublic ? 'bg-blue-50 text-blue-700 ring-blue-200' : 'bg-slate-100 text-slate-600 ring-slate-200'}`}>
                                {form.isPublic ? 'Public' : 'Private'}
                            </span>
                            <span className={`rounded-full px-3 py-1.5 ring-1 ${form.isActive ? 'bg-emerald-50 text-emerald-700 ring-emerald-200' : 'bg-slate-100 text-slate-600 ring-slate-200'}`}>
                                {form.isActive ? 'Active' : 'Draft'}
                            </span>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                            <button
                                type="button"
                                onClick={handleDownloadPdf}
                                className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                            >
                                <Download className="h-4 w-4" />
                                Download PDF
                            </button>
                            <a
                                href={shareUrl || undefined}
                                target="_blank"
                                rel="noreferrer"
                                className={`inline-flex items-center gap-2 rounded-xl px-3 py-2 text-sm font-medium transition ${
                                    form.slug
                                        ? 'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                                        : 'pointer-events-none border border-slate-200 bg-white text-slate-400'
                                }`}
                            >
                                <ExternalLink className="h-4 w-4" />
                                Open Live
                            </a>
                        </div>
                    </div>
                </div>
            </div>

            <div className="min-h-0 flex-1 overflow-y-auto bg-slate-50/80 p-6">
                <div className="grid gap-6 2xl:grid-cols-[minmax(0,1.55fr)_420px]">
                    <div className="space-y-6">
                        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
                            <div className="mb-5">
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Form Settings</p>
                                <h3 className="mt-2 text-lg font-semibold text-slate-900">Identity and publishing</h3>
                                <p className="mt-1 text-sm text-slate-600">
                                    Configure the public-facing title, context, and publish state before sharing the link.
                                </p>
                            </div>

                            <div className="grid gap-4 md:grid-cols-2">
                                <div className="md:col-span-2">
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Form Name *</label>
                                    <input
                                        type="text"
                                        value={form.name}
                                        onChange={(event) => setForm({ ...form, name: event.target.value })}
                                        placeholder="e.g., Personal Injury Intake"
                                        className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    />
                                </div>

                                <div className="md:col-span-2">
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Description</label>
                                    <textarea
                                        value={form.description || ''}
                                        onChange={(event) => setForm({ ...form, description: event.target.value })}
                                        placeholder="Give clients a quick explanation of what this form is for."
                                        rows={3}
                                        className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    />
                                </div>

                                <div>
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Practice Area</label>
                                    <select
                                        value={form.practiceArea || ''}
                                        onChange={(event) => setForm({ ...form, practiceArea: event.target.value })}
                                        className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    >
                                        <option value="">Select practice area...</option>
                                        {practiceAreas.map(area => (
                                            <option key={area} value={area}>{area}</option>
                                        ))}
                                    </select>
                                </div>

                                <div className="md:col-span-2 grid gap-4 lg:grid-cols-2 lg:items-stretch">
                                    <div className="flex h-full flex-col rounded-2xl border border-slate-200 bg-slate-50 p-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Visibility</p>
                                        <div className="mt-3 grid grid-cols-2 gap-2">
                                            <button
                                                type="button"
                                                onClick={() => setForm({ ...form, isPublic: true })}
                                                className={`inline-flex min-h-[48px] items-center justify-center rounded-xl px-4 py-2.5 text-sm font-semibold transition ${
                                                    form.isPublic
                                                        ? 'bg-blue-600 text-white shadow-sm'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50'
                                                }`}
                                            >
                                                Public
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => setForm({ ...form, isPublic: false })}
                                                className={`inline-flex min-h-[48px] items-center justify-center rounded-xl px-4 py-2.5 text-sm font-semibold transition ${
                                                    !form.isPublic
                                                        ? 'bg-slate-900 text-white shadow-sm'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50'
                                                }`}
                                            >
                                                Private
                                            </button>
                                        </div>
                                        <p className="mt-4 flex-1 text-sm leading-6 text-slate-600">
                                            {form.isPublic
                                                ? 'Public forms can accept client submissions from the share link.'
                                                : 'Private forms stay hidden until you are ready to expose them.'}
                                        </p>
                                    </div>

                                    <div className="flex h-full flex-col rounded-2xl border border-slate-200 bg-slate-50 p-4">
                                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Status</p>
                                        <div className="mt-3 grid grid-cols-2 gap-2">
                                            <button
                                                type="button"
                                                onClick={() => setForm({ ...form, isActive: true })}
                                                className={`inline-flex min-h-[48px] items-center justify-center rounded-xl px-4 py-2.5 text-sm font-semibold transition ${
                                                    form.isActive
                                                        ? 'bg-emerald-600 text-white shadow-sm'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50'
                                                }`}
                                            >
                                                Active
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => setForm({ ...form, isActive: false })}
                                                className={`inline-flex min-h-[48px] items-center justify-center rounded-xl px-4 py-2.5 text-sm font-semibold transition ${
                                                    !form.isActive
                                                        ? 'bg-slate-900 text-white shadow-sm'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-50'
                                                }`}
                                            >
                                                Draft
                                            </button>
                                        </div>
                                        <p className="mt-4 flex-1 text-sm leading-6 text-slate-600">
                                            {form.isActive
                                                ? 'Active forms can be reached by visitors once they are public.'
                                                : 'Draft forms remain unavailable to public traffic even if a link exists.'}
                                        </p>
                                    </div>
                                </div>

                                <div className="md:col-span-2 rounded-2xl border border-slate-200 bg-slate-50 p-4">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Submission Follow-up</p>
                                    <div className="mt-4 grid gap-4 lg:grid-cols-2">
                                        <div className="lg:col-span-2">
                                            <label className="mb-1.5 block text-sm font-medium text-slate-700">Thank You Message</label>
                                            <textarea
                                                value={form.thankYouMessage || ''}
                                                onChange={(event) => setForm({ ...form, thankYouMessage: event.target.value })}
                                                placeholder="Shown immediately after a successful submission."
                                                rows={3}
                                                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                            />
                                        </div>

                                        <div>
                                            <label className="mb-1.5 block text-sm font-medium text-slate-700">Redirect URL</label>
                                            <input
                                                type="url"
                                                value={form.redirectUrl || ''}
                                                onChange={(event) => setForm({ ...form, redirectUrl: event.target.value })}
                                                placeholder="https://yourfirm.com/thank-you"
                                                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                            />
                                        </div>

                                        <div>
                                            <label className="mb-1.5 block text-sm font-medium text-slate-700">Notify Email</label>
                                            <input
                                                type="email"
                                                value={form.notifyEmail || ''}
                                                onChange={(event) => setForm({ ...form, notifyEmail: event.target.value })}
                                                placeholder="intake@yourfirm.com"
                                                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                            />
                                        </div>
                                    </div>
                                </div>

                                <div className="md:col-span-2 overflow-hidden rounded-2xl border border-slate-200 bg-slate-950 text-white">
                                    <div className="border-b border-white/10 px-4 py-4">
                                        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                                            <div>
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-200">Share and Embed</p>
                                                <h4 className="mt-2 text-lg font-semibold text-white">Distribution tools for launch day</h4>
                                                <p className="mt-1 text-sm text-white/70">
                                                    Copy the hosted link, open the live form, or grab an iframe snippet for your website.
                                                </p>
                                            </div>
                                            <div className="grid w-full gap-2 sm:grid-cols-2 xl:w-auto">
                                                <button
                                                    type="button"
                                                    onClick={handleCopyLink}
                                                    disabled={!form.slug}
                                                    className="inline-flex items-center justify-center gap-2 rounded-xl bg-white/10 px-3 py-2 text-sm font-medium text-white transition hover:bg-white/15 disabled:cursor-not-allowed disabled:opacity-50"
                                                >
                                                    <Link2 className="w-4 h-4" />
                                                    Copy Link
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={handleCopyEmbedCode}
                                                    disabled={!form.slug}
                                                    className="inline-flex items-center justify-center gap-2 rounded-xl bg-white/10 px-3 py-2 text-sm font-medium text-white transition hover:bg-white/15 disabled:cursor-not-allowed disabled:opacity-50"
                                                >
                                                    <LayoutGrid className="w-4 h-4" />
                                                    Copy Embed
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={handleDownloadPdf}
                                                    className="inline-flex items-center justify-center gap-2 rounded-xl bg-white/10 px-3 py-2 text-sm font-medium text-white transition hover:bg-white/15"
                                                >
                                                    <Download className="w-4 h-4" />
                                                    Download PDF
                                                </button>
                                                <a
                                                    href={shareUrl || undefined}
                                                    target="_blank"
                                                    rel="noreferrer"
                                                    className={`inline-flex items-center justify-center gap-2 rounded-xl px-3 py-2 text-sm font-medium transition ${
                                                        form.slug
                                                            ? 'bg-white text-slate-900 hover:bg-slate-100'
                                                            : 'pointer-events-none bg-white/10 text-white/50'
                                                    }`}
                                                >
                                                    <ExternalLink className="w-4 h-4" />
                                                    Open Live
                                                </a>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="grid gap-4 px-4 py-4 lg:grid-cols-2">
                                        <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
                                            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-blue-200">
                                                <Globe className="w-4 h-4" />
                                                Public Link
                                            </div>
                                            <code className="mt-3 block min-h-[48px] break-all rounded-xl bg-black/20 px-3 py-3 text-sm text-white/85">
                                                {shareUrl || 'Save the form once to generate a hosted share link.'}
                                            </code>
                                            <p className="mt-3 text-xs leading-5 text-white/60">
                                                {form.isPublic && form.isActive
                                                    ? 'This form is ready for public traffic.'
                                                    : 'The link exists after save, but submissions only open when the form is both Public and Active.'}
                                            </p>
                                        </div>

                                        <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
                                            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-blue-200">
                                                <LayoutGrid className="w-4 h-4" />
                                                Embed Snippet
                                            </div>
                                            <textarea
                                                readOnly
                                                rows={4}
                                                value={embedCode || '<iframe ... />'}
                                                className="mt-3 w-full rounded-xl border border-white/10 bg-black/20 px-3 py-3 text-sm text-white/85 outline-none"
                                            />
                                            <p className="mt-3 text-xs leading-5 text-white/60">
                                                Use this iframe on your marketing site, campaign page, or client portal shell.
                                            </p>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </section>

                        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
                            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Template Presets</p>
                                    <h3 className="mt-2 text-lg font-semibold text-slate-900">Start from a polished workflow</h3>
                                    <p className="mt-1 text-sm text-slate-600">
                                        Load a ready-made intake structure, then tailor copy, routing, and logic instead of starting from zero.
                                    </p>
                                </div>
                                <p className="rounded-full bg-slate-100 px-3 py-1.5 text-xs font-semibold text-slate-600">
                                    {templatePresets.length} launch-ready presets
                                </p>
                            </div>

                            <div className="mt-5 grid gap-4 lg:grid-cols-2">
                                {templatePresets.map(preset => {
                                    const Icon = preset.icon;

                                    return (
                                        <button
                                            key={preset.id}
                                            type="button"
                                            onClick={() => void applyTemplatePreset(preset)}
                                            className="group flex h-full flex-col rounded-3xl border border-slate-200 bg-white p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md"
                                        >
                                            <div className={`rounded-2xl bg-gradient-to-br ${preset.accentClass} p-4`}>
                                                <div className="flex items-start justify-between gap-3">
                                                    <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/80 text-current shadow-sm">
                                                        <Icon className="h-6 w-6" />
                                                    </div>
                                                    <span className="rounded-full bg-white/80 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-700">
                                                        {preset.fields.length} fields
                                                    </span>
                                                </div>
                                                <p className="mt-4 text-base font-semibold text-slate-900">{preset.name}</p>
                                                <p className="mt-2 text-sm leading-6 text-slate-700">{preset.description}</p>
                                            </div>
                                            <div className="mt-4 flex flex-1 flex-col justify-between gap-4">
                                                <div className="min-w-0">
                                                    <p className="text-sm font-medium text-slate-900">{preset.formName}</p>
                                                    <p className="mt-1 text-xs text-slate-500">{preset.practiceArea}</p>
                                                </div>
                                                <div className="flex items-center justify-start">
                                                    <span className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-3 py-2 text-xs font-semibold text-white transition group-hover:bg-slate-800">
                                                        <Sparkles className="h-4 w-4" />
                                                        Apply
                                                    </span>
                                                </div>
                                            </div>
                                        </button>
                                    );
                                })}
                            </div>
                        </section>

                        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
                            <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Field Canvas</p>
                                    <h3 className="mt-2 text-lg font-semibold text-slate-900">Structure and sequencing</h3>
                                    <p className="mt-1 text-sm text-slate-600">
                                        Drag cards to reorder them, toggle required status inline, and open details only when needed.
                                    </p>
                                </div>

                                <div className="relative" ref={fieldPaletteRef}>
                                    <button
                                        type="button"
                                        onClick={() => setIsFieldPaletteOpen(currentValue => !currentValue)}
                                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-slate-800"
                                    >
                                        <Plus className="w-4 h-4" />
                                        Add Field
                                        <ChevronDown className={`w-4 h-4 transition ${isFieldPaletteOpen ? 'rotate-180' : ''}`} />
                                    </button>

                                    {isFieldPaletteOpen && (
                                        <div className="absolute right-0 top-full z-20 mt-3 w-[23rem] rounded-2xl border border-slate-200 bg-white p-3 shadow-2xl">
                                            <div className="mb-3 px-2">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Field Library</p>
                                                <p className="mt-1 text-sm text-slate-600">All supported field types are available here.</p>
                                            </div>
                                            <div className="grid gap-2 sm:grid-cols-2">
                                                {fieldTypes.map(fieldType => {
                                                    const Icon = fieldType.icon;

                                                    return (
                                                        <button
                                                            key={fieldType.value}
                                                            type="button"
                                                            onClick={() => addField(fieldType.value)}
                                                            disabled={fieldType.disabled}
                                                            className={`rounded-2xl border px-4 py-3 text-left transition ${
                                                                fieldType.disabled
                                                                    ? 'cursor-not-allowed border-slate-200 bg-slate-50 text-slate-400'
                                                                    : 'border-slate-200 bg-white hover:border-blue-200 hover:bg-blue-50/40'
                                                            }`}
                                                        >
                                                            <div className="flex items-start gap-3">
                                                                <div className={`flex h-10 w-10 items-center justify-center rounded-xl ${fieldType.colorClass}`}>
                                                                    <Icon className="w-5 h-5" />
                                                                </div>
                                                                <div className="min-w-0">
                                                                    <div className="flex items-center gap-2">
                                                                        <p className="text-sm font-semibold text-slate-900">{fieldType.label}</p>
                                                                        {fieldType.disabled && (
                                                                            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.16em] text-amber-700">
                                                                                Soon
                                                                            </span>
                                                                        )}
                                                                    </div>
                                                                    <p className="mt-1 text-xs leading-5 text-slate-500">{fieldType.description}</p>
                                                                </div>
                                                            </div>
                                                        </button>
                                                    );
                                                })}
                                            </div>
                                        </div>
                                    )}
                                </div>
                            </div>

                            <div className="mt-6 space-y-3">
                                {fields.length === 0 ? (
                                    <div className="overflow-hidden rounded-3xl border border-slate-200 bg-gradient-to-br from-slate-50 via-white to-blue-50">
                                        <div className="grid gap-6 px-6 py-10 xl:grid-cols-[minmax(0,1fr)_280px] xl:items-center">
                                            <div>
                                                <div className="inline-flex items-center gap-2 rounded-full bg-slate-900 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-white">
                                                    <Sparkles className="h-4 w-4" />
                                                    Blank Canvas
                                                </div>
                                                <h4 className="mt-4 text-2xl font-semibold text-slate-900">Launch the form with structure, not guesswork</h4>
                                                <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600">
                                                    Start from a preset for a premium first draft, or open the field library and build the intake journey question by question.
                                                </p>
                                                <div className="mt-5 flex flex-wrap gap-3">
                                                    <button
                                                        type="button"
                                                        onClick={() => void applyTemplatePreset(templatePresets[0])}
                                                        className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-800"
                                                    >
                                                        <Sparkles className="h-4 w-4" />
                                                        Use Recommended Preset
                                                    </button>
                                                    <button
                                                        type="button"
                                                        onClick={() => setIsFieldPaletteOpen(true)}
                                                        className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:bg-slate-50"
                                                    >
                                                        <Plus className="h-4 w-4" />
                                                        Add First Field
                                                    </button>
                                                </div>
                                            </div>

                                            <div className="rounded-3xl border border-slate-200 bg-white/80 p-5 shadow-sm">
                                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">Starter Checklist</p>
                                                <div className="mt-4 space-y-3 text-sm text-slate-600">
                                                    <div className="flex items-center gap-3">
                                                        <span className="flex h-8 w-8 items-center justify-center rounded-2xl bg-blue-50 text-blue-700">1</span>
                                                        Choose a preset or add your first field
                                                    </div>
                                                    <div className="flex items-center gap-3">
                                                        <span className="flex h-8 w-8 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-700">2</span>
                                                        Refine copy, required state, and logic
                                                    </div>
                                                    <div className="flex items-center gap-3">
                                                        <span className="flex h-8 w-8 items-center justify-center rounded-2xl bg-violet-50 text-violet-700">3</span>
                                                        Save to unlock share link and embed code
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                ) : (
                                    <>
                                        {fields.map(field => {
                                            const fieldType = getFieldTypeDefinition(field.type);
                                            const Icon = fieldType.icon;
                                            const optionCount = parseFieldOptions(field.options).length;
                                            const isDragging = draggedFieldId === field.id;
                                            const isDropTarget = dragOverFieldId === field.id && draggedFieldId !== field.id;

                                            return (
                                                <div
                                                    key={field.id}
                                                    draggable
                                                    onDragStart={() => handleDragStart(field.id)}
                                                    onDragEnd={() => {
                                                        setDraggedFieldId(null);
                                                        setDragOverFieldId(null);
                                                    }}
                                                    onDragOver={(event) => {
                                                        event.preventDefault();
                                                        if (draggedFieldId !== field.id) {
                                                            setDragOverFieldId(field.id);
                                                        }
                                                    }}
                                                    onDrop={() => handleDrop(field.id)}
                                                    className={`rounded-2xl border bg-white p-4 shadow-sm transition ${
                                                        isDragging ? 'border-blue-300 bg-blue-50/50 opacity-60' : 'border-slate-200'
                                                    } ${isDropTarget ? 'ring-2 ring-blue-100 border-blue-300' : ''}`}
                                                >
                                                    <div className="flex items-start gap-4">
                                                        <div className="flex items-center gap-3">
                                                            <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-slate-200 bg-slate-50 text-slate-400">
                                                                <GripVertical className="w-4 h-4" />
                                                            </div>
                                                            <div className={`flex h-11 w-11 items-center justify-center rounded-2xl ${fieldType.colorClass}`}>
                                                                <Icon className="w-5 h-5" />
                                                            </div>
                                                        </div>
                                                        <div className="min-w-0 flex-1">
                                                            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                                                                <div className="min-w-0">
                                                                    <div className="flex flex-wrap items-center gap-2">
                                                                        <p className="truncate text-sm font-semibold text-slate-900">
                                                                            {field.label || 'Untitled field'}
                                                                        </p>
                                                                        <span className="rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-600">
                                                                            {fieldType.label}
                                                                        </span>
                                                                        {field.required && (
                                                                            <span className="rounded-full bg-rose-100 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-rose-700">
                                                                                Required
                                                                            </span>
                                                                        )}
                                                                        {field.conditionalLogic && (
                                                                            <span className="rounded-full bg-violet-100 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-violet-700">
                                                                                Conditional
                                                                            </span>
                                                                        )}
                                                                    </div>
                                                                    <p className="mt-1 truncate text-xs text-slate-500">
                                                                        {field.name}
                                                                        {field.placeholder ? ` | ${field.placeholder}` : ''}
                                                                        {optionCount > 0 ? ` | ${optionCount} options` : ''}
                                                                    </p>
                                                                    <div className="mt-2 flex flex-wrap gap-2">
                                                                        {field.helpText && (
                                                                            <span className="rounded-full bg-slate-100 px-2.5 py-1 text-[11px] font-medium text-slate-600">
                                                                                Help text
                                                                            </span>
                                                                        )}
                                                                        {field.defaultValue && (
                                                                            <span className="rounded-full bg-blue-50 px-2.5 py-1 text-[11px] font-medium text-blue-700">
                                                                                Default value
                                                                            </span>
                                                                        )}
                                                                        {field.validationPattern && (
                                                                            <span className="rounded-full bg-amber-50 px-2.5 py-1 text-[11px] font-medium text-amber-700">
                                                                                Regex rule
                                                                            </span>
                                                                        )}
                                                                    </div>
                                                                    {getConditionalSummary(field) && (
                                                                        <p className="mt-2 text-xs font-medium text-violet-700">
                                                                            {getConditionalSummary(field)}
                                                                        </p>
                                                                    )}
                                                                </div>
                                                                <div className="flex flex-wrap items-center gap-2">
                                                                    <button
                                                                        type="button"
                                                                        onClick={() => toggleFieldRequired(field.id)}
                                                                        className={`rounded-xl px-3 py-2 text-xs font-semibold transition ${
                                                                            field.required
                                                                                ? 'bg-rose-600 text-white hover:bg-rose-700'
                                                                                : 'bg-slate-100 text-slate-700 hover:bg-slate-200'
                                                                        }`}
                                                                    >
                                                                        {field.required ? 'Required' : 'Optional'}
                                                                    </button>
                                                                    <button
                                                                        type="button"
                                                                        onClick={() => duplicateField(field.id)}
                                                                        className="rounded-xl border border-slate-200 p-2 text-slate-500 transition hover:bg-slate-50 hover:text-slate-700"
                                                                        aria-label="Duplicate field"
                                                                    >
                                                                        <Copy className="w-4 h-4" />
                                                                    </button>
                                                                    <button
                                                                        type="button"
                                                                        onClick={() => {
                                                                            setEditingField(field);
                                                                            setShowFieldModal(true);
                                                                        }}
                                                                        className="rounded-xl border border-slate-200 p-2 text-slate-500 transition hover:bg-slate-50 hover:text-slate-700"
                                                                        aria-label="Edit field"
                                                                    >
                                                                        <Edit className="w-4 h-4" />
                                                                    </button>
                                                                    <button
                                                                        type="button"
                                                                        onClick={() => removeField(field.id)}
                                                                        className="rounded-xl border border-red-100 p-2 text-red-500 transition hover:bg-red-50 hover:text-red-600"
                                                                        aria-label="Delete field"
                                                                    >
                                                                        <Trash2 className="w-4 h-4" />
                                                                    </button>
                                                                </div>
                                                            </div>
                                                        </div>
                                                    </div>
                                                </div>
                                            );
                                        })}
                                        {draggedFieldId && fields.length > 1 && (
                                            <div
                                                onDragOver={(event) => event.preventDefault()}
                                                onDrop={handleDropAtEnd}
                                                className="rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-4 py-3 text-sm font-medium text-slate-500"
                                            >
                                                Drop here to move the field to the end of the form.
                                            </div>
                                        )}
                                    </>
                                )}
                            </div>
                        </section>
                    </div>
                    <aside className="self-start 2xl:sticky 2xl:top-6">
                        <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
                            <div className="flex items-center justify-between border-b border-slate-200 bg-slate-950 px-5 py-4 text-white">
                                <div>
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-blue-200">Live Preview</p>
                                    <h3 className="mt-1 text-sm font-semibold">Public intake experience</h3>
                                </div>
                                <div className="flex items-center gap-2">
                                    <div className="inline-flex rounded-full bg-white/10 p-1 text-xs font-medium text-white/90">
                                        <button
                                            type="button"
                                            onClick={() => setPreviewViewport('desktop')}
                                            className={`inline-flex items-center gap-2 rounded-full px-3 py-1.5 transition ${previewViewport === 'desktop' ? 'bg-white text-slate-900' : 'text-white/80 hover:text-white'}`}
                                        >
                                            <LayoutGrid className="h-3.5 w-3.5" />
                                            Desktop
                                        </button>
                                        <button
                                            type="button"
                                            onClick={() => setPreviewViewport('mobile')}
                                            className={`inline-flex items-center gap-2 rounded-full px-3 py-1.5 transition ${previewViewport === 'mobile' ? 'bg-white text-slate-900' : 'text-white/80 hover:text-white'}`}
                                        >
                                            <Phone className="h-3.5 w-3.5" />
                                            Mobile
                                        </button>
                                    </div>
                                    <button
                                        type="button"
                                        onClick={resetPreviewToDefaults}
                                        className="inline-flex items-center gap-2 rounded-full bg-white/10 px-3 py-1.5 text-xs font-medium text-white/90 transition hover:bg-white/15"
                                    >
                                        <RefreshCw className="h-3.5 w-3.5" />
                                        Reset
                                    </button>
                                </div>
                            </div>
                            <div className="bg-slate-100 p-4">
                                <div className={`transition-all duration-300 ${previewFrameWidthClass}`}>
                                    <div className="mb-3 flex items-center justify-between px-1 text-xs font-medium text-slate-500">
                                        <span>{previewViewportLabel} canvas</span>
                                        <span>{visiblePreviewFields.length} visible field{visiblePreviewFields.length === 1 ? '' : 's'}</span>
                                    </div>
                                <div className={`overflow-hidden border border-slate-300 bg-white shadow-xl transition-all duration-300 ${previewViewport === 'mobile' ? 'rounded-[32px]' : 'rounded-[28px]'}`}>
                                    {previewViewport === 'mobile' && (
                                        <div className="flex items-center justify-center border-b border-slate-200 bg-slate-950 py-3">
                                            <span className="h-1.5 w-16 rounded-full bg-white/60" />
                                        </div>
                                    )}
                                    <div className="border-b border-slate-100 px-6 py-6">
                                        <div className="mb-4 flex items-center gap-3">
                                            <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-blue-50 text-blue-700">
                                                <FileText className="w-6 h-6" />
                                            </div>
                                            {form.practiceArea && (
                                                <span className="rounded-full bg-blue-50 px-3 py-1 text-xs font-semibold text-blue-700">
                                                    {form.practiceArea}
                                                </span>
                                            )}
                                        </div>
                                        <h4 className="text-2xl font-semibold text-slate-900">
                                            {form.name?.trim() || 'Untitled Intake Form'}
                                        </h4>
                                        <p className="mt-2 text-sm leading-6 text-slate-600">
                                            {form.description?.trim() || 'Clients will see the layout, order, and field labels exactly as arranged here.'}
                                        </p>
                                    </div>
                                    <div className="space-y-5 px-6 py-6">
                                        {fields.length === 0 ? (
                                            <div className="rounded-3xl border border-dashed border-slate-200 bg-gradient-to-br from-slate-50 to-blue-50 px-5 py-10 text-center">
                                                <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-white text-blue-700 shadow-sm ring-1 ring-slate-200">
                                                    <Sparkles className="h-6 w-6" />
                                                </div>
                                                <h4 className="mt-4 text-base font-semibold text-slate-900">Preview wakes up as soon as the form has structure</h4>
                                                <p className="mt-2 text-sm leading-6 text-slate-500">
                                                    Load a preset or add a few fields to see the public experience, responsive frame, and conditional logic in motion.
                                                </p>
                                            </div>
                                        ) : visiblePreviewFields.length === 0 ? (
                                            <div className="rounded-2xl border border-dashed border-violet-200 bg-violet-50 px-4 py-8 text-center text-sm text-violet-700">
                                                Current preview answers hide every conditional field. Adjust the driver inputs to reveal dependent questions.
                                            </div>
                                        ) : (
                                            visiblePreviewFields.map(field => (
                                                <div key={field.id}>
                                                    <label className="mb-2 block text-sm font-medium text-slate-800">
                                                        {field.label || 'Untitled field'}
                                                        {field.required && <span className="ml-1 text-rose-500">*</span>}
                                                    </label>
                                                    {renderPreviewField(field)}
                                                    {field.helpText && (
                                                        <p className="mt-2 text-xs leading-5 text-slate-500">{field.helpText}</p>
                                                    )}
                                                    {field.validationPattern && (
                                                        <p className="mt-2 text-xs font-medium text-amber-700">
                                                            Pattern validation is enabled{field.validationMessage ? `: ${field.validationMessage}` : '.'}
                                                        </p>
                                                    )}
                                                </div>
                                            ))
                                        )}
                                    </div>
                                    <div className="border-t border-slate-100 px-6 py-5">
                                        <button
                                            type="button"
                                            disabled
                                            className="w-full rounded-xl bg-slate-900 px-4 py-3 text-sm font-semibold text-white"
                                        >
                                            Submit Form
                                        </button>
                                        <div className="mt-4 space-y-3 rounded-2xl bg-slate-50 p-4 text-xs text-slate-600">
                                            <div>
                                                <p className="font-semibold uppercase tracking-[0.16em] text-slate-500">Thank You</p>
                                                <p className="mt-1 leading-5">
                                                    {form.thankYouMessage?.trim() || 'Default thank-you copy will be shown after submission.'}
                                                </p>
                                            </div>
                                            {form.redirectUrl?.trim() && (
                                                <div>
                                                    <p className="font-semibold uppercase tracking-[0.16em] text-slate-500">Redirect</p>
                                                    <p className="mt-1 truncate">{form.redirectUrl}</p>
                                                </div>
                                            )}
                                            {form.notifyEmail?.trim() && (
                                                <div>
                                                    <p className="font-semibold uppercase tracking-[0.16em] text-slate-500">Notifications</p>
                                                    <p className="mt-1 truncate">{form.notifyEmail}</p>
                                                </div>
                                            )}
                                        </div>
                                        <p className="mt-3 text-left text-xs leading-5 text-slate-500">
                                            Preview mirrors field order, labels, required state, and conditional visibility as you edit.
                                        </p>
                                        {hiddenPreviewFieldCount > 0 && (
                                            <p className="mt-2 text-left text-xs font-medium text-violet-700">
                                                {hiddenPreviewFieldCount} field{hiddenPreviewFieldCount > 1 ? 's are' : ' is'} currently hidden by logic.
                                            </p>
                                        )}
                                    </div>
                                </div>
                                </div>
                                <div className="mt-4 grid grid-cols-2 gap-3">
                                    <div className="rounded-2xl border border-slate-200 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Visible</p>
                                        <p className="mt-2 text-2xl font-semibold text-slate-900">{visiblePreviewFields.length}</p>
                                    </div>
                                    <div className="rounded-2xl border border-slate-200 bg-white px-4 py-3 shadow-sm">
                                        <p className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Hidden</p>
                                        <p className="mt-2 text-2xl font-semibold text-slate-900">{hiddenPreviewFieldCount}</p>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </aside>
                </div>
            </div>
            <div className="sticky bottom-0 z-10 border-t border-slate-200 bg-white/95 px-6 py-4 backdrop-blur">
                <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex flex-wrap items-center gap-3">
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Builder State</p>
                            <p className="mt-1 text-sm font-semibold text-slate-900">
                                {form.name?.trim() || 'Untitled form'}
                            </p>
                        </div>
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">Launch Tools</p>
                            <p className="mt-1 text-sm font-semibold text-slate-900">
                                {form.slug ? 'Share + embed ready' : 'Save to unlock share + embed'}
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-wrap items-center justify-end gap-2">
                        {onCancel && (
                            <button
                                type="button"
                                onClick={onCancel}
                                className="rounded-xl px-4 py-2.5 text-sm font-medium text-slate-600 transition hover:bg-slate-100"
                            >
                                Cancel
                            </button>
                        )}
                        <button
                            type="button"
                            onClick={handleCopyLink}
                            disabled={!form.slug}
                            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <Link2 className="h-4 w-4" />
                            Copy Link
                        </button>
                        <button
                            type="button"
                            onClick={handleCopyEmbedCode}
                            disabled={!form.slug}
                            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <LayoutGrid className="h-4 w-4" />
                            Copy Embed
                        </button>
                        <button
                            type="button"
                            onClick={handleDownloadPdf}
                            className="inline-flex items-center gap-2 rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                        >
                            <Download className="h-4 w-4" />
                            Download PDF
                        </button>
                        <button
                            type="button"
                            onClick={handleSave}
                            disabled={saving || !form.name?.trim()}
                            className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <Save className="h-4 w-4" />
                            {saving ? 'Saving...' : formId ? 'Update Form' : 'Create Form'}
                        </button>
                    </div>
                </div>
            </div>
            {showFieldModal && editingField && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
                    <div className="w-full max-w-2xl max-h-[90vh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl">
                        <h3 className="text-lg font-semibold text-slate-900">Edit Field</h3>
                        <p className="mt-1 text-sm text-slate-500">
                            Adjust the field label, API key, and input behavior. Required status stays on the card itself.
                        </p>

                        <div className="mt-5 space-y-4">
                            <div>
                                <label className="mb-1.5 block text-sm font-medium text-slate-700">Label</label>
                                <input
                                    type="text"
                                    value={editingField.label}
                                    onChange={(event) => setEditingField({ ...editingField, label: event.target.value })}
                                    className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                />
                            </div>

                            <div>
                                <label className="mb-1.5 block text-sm font-medium text-slate-700">Field Name</label>
                                <input
                                    type="text"
                                    value={editingField.name}
                                    onChange={(event) => setEditingField({ ...editingField, name: event.target.value })}
                                    className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                />
                            </div>

                            <div>
                                <label className="mb-1.5 block text-sm font-medium text-slate-700">Type</label>
                                <select
                                    value={editingField.type}
                                    onChange={(event) => handleEditingFieldTypeChange(event.target.value)}
                                    className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                >
                                    {fieldTypes.map(type => (
                                        <option key={type.value} value={type.value} disabled={type.disabled}>
                                            {type.disabled ? `${type.label} (Coming soon)` : type.label}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            {editingField.type === 'file' && (
                                <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                                    File uploads are coming soon. Replace this field with a supported type before publishing the form.
                                </div>
                            )}

                            <div>
                                <label className="mb-1.5 block text-sm font-medium text-slate-700">Placeholder</label>
                                <input
                                    type="text"
                                    value={editingField.placeholder || ''}
                                    onChange={(event) => setEditingField({ ...editingField, placeholder: event.target.value })}
                                    className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                />
                            </div>

                            <div>
                                <label className="mb-1.5 block text-sm font-medium text-slate-700">Help Text</label>
                                <textarea
                                    value={editingField.helpText || ''}
                                    onChange={(event) => setEditingField({ ...editingField, helpText: event.target.value })}
                                    rows={2}
                                    placeholder="Short guidance shown below the field."
                                    className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                />
                            </div>

                            {editingField.type === 'checkbox' ? (
                                <div>
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Default State</label>
                                    <select
                                        value={isCheckboxDefaultEnabled(editingField.defaultValue) ? 'true' : 'false'}
                                        onChange={(event) => setEditingField({ ...editingField, defaultValue: event.target.value })}
                                        className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    >
                                        <option value="false">Unchecked</option>
                                        <option value="true">Checked</option>
                                    </select>
                                </div>
                            ) : editingField.type !== 'file' && (
                                <div>
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Default Value</label>
                                    <input
                                        type="text"
                                        value={editingField.defaultValue || ''}
                                        onChange={(event) => setEditingField({ ...editingField, defaultValue: event.target.value })}
                                        placeholder={supportsChoiceOptions(editingField.type) ? 'Must match one of the available options' : 'Optional pre-filled value'}
                                        className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    />
                                </div>
                            )}

                            {supportsChoiceOptions(editingField.type) && (
                                <div>
                                    <label className="mb-1.5 block text-sm font-medium text-slate-700">Options (one per line)</label>
                                    <textarea
                                        value={editingField.options || ''}
                                        onChange={(event) => setEditingField({ ...editingField, options: event.target.value })}
                                        rows={4}
                                        className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                    />
                                </div>
                            )}

                            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                    <div>
                                        <p className="text-sm font-semibold text-slate-900">Conditional Display</p>
                                        <p className="mt-1 text-xs leading-5 text-slate-500">
                                            Show this field only when an earlier select, radio, or checkbox field matches a value.
                                        </p>
                                    </div>
                                    {availableConditionalSourceFields.length > 0 && (
                                        <div className="grid grid-cols-2 gap-2">
                                            <button
                                                type="button"
                                                onClick={() => updateEditingFieldConditionalLogic('')}
                                                className={`rounded-xl px-3 py-2 text-xs font-semibold transition ${
                                                    !editingConditionalRule
                                                        ? 'bg-slate-900 text-white'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-100'
                                                }`}
                                            >
                                                Always Show
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    const nextRule = createDefaultConditionalRule(availableConditionalSourceFields[0]);
                                                    updateEditingFieldConditionalLogic(serializeConditionalLogic(nextRule));
                                                }}
                                                className={`rounded-xl px-3 py-2 text-xs font-semibold transition ${
                                                    editingConditionalRule
                                                        ? 'bg-violet-600 text-white'
                                                        : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-100'
                                                }`}
                                            >
                                                Show When
                                            </button>
                                        </div>
                                    )}
                                </div>

                                {availableConditionalSourceFields.length === 0 ? (
                                    <p className="mt-4 text-xs leading-5 text-slate-500">
                                        Add and configure a choice field above this one to unlock conditional display rules.
                                    </p>
                                ) : editingConditionalRule ? (
                                    <div className="mt-4 grid gap-4 sm:grid-cols-2">
                                        <div>
                                            <label className="mb-1.5 block text-sm font-medium text-slate-700">Source Field</label>
                                            <select
                                                value={selectedConditionalSourceField?.name || ''}
                                                onChange={(event) => {
                                                    const nextSourceField = availableConditionalSourceFields.find(field => field.name === event.target.value) || null;
                                                    const nextRule = createDefaultConditionalRule(nextSourceField);
                                                    updateEditingFieldConditionalLogic(serializeConditionalLogic(nextRule));
                                                }}
                                                className="w-full rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                            >
                                                {availableConditionalSourceFields.map(field => (
                                                    <option key={field.id} value={field.name}>
                                                        {field.label || field.name}
                                                    </option>
                                                ))}
                                            </select>
                                        </div>

                                        <div>
                                            <label className="mb-1.5 block text-sm font-medium text-slate-700">Matches Value</label>
                                            <select
                                                value={editingConditionalRule.value}
                                                onChange={(event) => {
                                                    if (!selectedConditionalSourceField) {
                                                        return;
                                                    }

                                                    updateEditingFieldConditionalLogic(serializeConditionalLogic({
                                                        action: 'show',
                                                        operator: 'equals',
                                                        sourceField: selectedConditionalSourceField.name,
                                                        value: normalizeConditionalRuleValue(selectedConditionalSourceField, event.target.value)
                                                    }));
                                                }}
                                                className="w-full rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                            >
                                                {conditionalValueOptions.map(option => (
                                                    <option key={option.value} value={option.value}>
                                                        {option.label}
                                                    </option>
                                                ))}
                                            </select>
                                        </div>
                                    </div>
                                ) : null}
                            </div>

                            {supportsPatternValidation(editingField.type) && (
                                <>
                                    <div>
                                        <label className="mb-1.5 block text-sm font-medium text-slate-700">Validation Pattern (Regex)</label>
                                        <input
                                            type="text"
                                            value={editingField.validationPattern || ''}
                                            onChange={(event) => setEditingField({ ...editingField, validationPattern: event.target.value })}
                                            placeholder="e.g. ^[0-9]{5}$"
                                            className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                        />
                                    </div>

                                    <div>
                                        <label className="mb-1.5 block text-sm font-medium text-slate-700">Validation Message</label>
                                        <input
                                            type="text"
                                            value={editingField.validationMessage || ''}
                                            onChange={(event) => setEditingField({ ...editingField, validationMessage: event.target.value })}
                                            placeholder="Shown when the regex rule fails."
                                            className="w-full rounded-xl border border-slate-200 px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-blue-300 focus:ring-4 focus:ring-blue-50"
                                        />
                                        <p className="mt-1 text-xs text-slate-500">
                                            Pattern validation runs only when the field has a value.
                                        </p>
                                    </div>
                                </>
                            )}
                        </div>

                        <div className="mt-6 flex gap-2">
                            <button
                                type="button"
                                onClick={() => updateField(editingField)}
                                className="flex-1 rounded-xl bg-slate-900 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-800"
                            >
                                Save Field
                            </button>
                            <button
                                type="button"
                                onClick={closeFieldModal}
                                className="rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-600 transition hover:bg-slate-50"
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
