'use client';

import { useState, useEffect } from 'react';
import { FileText, Send, CheckCircle, AlertCircle } from './Icons';
import { api } from '../services/api';

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
}

interface IntakeForm {
    id: string;
    name: string;
    description?: string;
    fieldsJson: string;
    styleJson?: string;
    practiceArea?: string;
}

interface PublicIntakeFormProps {
    slug: string;
}

const parseFieldOptions = (rawOptions?: string) =>
    (rawOptions || '')
        .split('\n')
        .map(option => option.trim())
        .filter(Boolean);

const getCheckboxDefaultValue = (value?: string) => (value || '').trim().toLowerCase() === 'true';

const getDefaultFieldValue = (field: IntakeFormField) => {
    if (!field.defaultValue) {
        return field.type === 'checkbox' ? false : '';
    }

    const normalizedDefaultValue = field.defaultValue.trim();

    if (field.type === 'checkbox') {
        return getCheckboxDefaultValue(normalizedDefaultValue);
    }

    if (field.type === 'select' || field.type === 'radio') {
        const options = parseFieldOptions(field.options);
        return options.includes(normalizedDefaultValue) ? normalizedDefaultValue : '';
    }

    return normalizedDefaultValue;
};

const getTrimmedStringValue = (value: unknown) => (typeof value === 'string' ? value.trim() : '');

export default function PublicIntakeForm({ slug }: PublicIntakeFormProps) {
    const [form, setForm] = useState<IntakeForm | null>(null);
    const [fields, setFields] = useState<IntakeFormField[]>([]);
    const [formData, setFormData] = useState<Record<string, any>>({});
    const [loading, setLoading] = useState(true);
    const [submitting, setSubmitting] = useState(false);
    const [submitted, setSubmitted] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [thankYouMessage, setThankYouMessage] = useState('');
    const hasUnsupportedFileField = fields.some(field => field.type === 'file');

    useEffect(() => {
        loadForm();
    }, [slug]);

    const loadForm = async () => {
        setLoading(true);
        try {
            const data = await api.intake.public.get(slug);
            const parsedFields = JSON.parse(data.fieldsJson || '[]') as IntakeFormField[];
            setForm(data);
            setFields(parsedFields);

            // Set default values
            const defaults: Record<string, any> = {};
            parsedFields.forEach((field: IntakeFormField) => {
                defaults[field.name] = getDefaultFieldValue(field);
            });
            setFormData(defaults);
        } catch (err) {
            setError('Form not found or is no longer available.');
        } finally {
            setLoading(false);
        }
    };

    const handleChange = (fieldName: string, value: any) => {
        setFormData(prev => ({ ...prev, [fieldName]: value }));
    };

    const extractErrorMessage = (err: unknown, fallback: string) => {
        if (!(err instanceof Error) || !err.message) {
            return fallback;
        }

        const detailMatch = err.message.match(/\((.*)\)$/);
        return detailMatch?.[1] || err.message || fallback;
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);

        if (hasUnsupportedFileField) {
            setError('This form includes a file upload field, but file uploads are not available yet. Please contact the firm directly or ask them to update the form.');
            return;
        }

        for (const field of fields) {
            const rawValue = formData[field.name];
            const hasValue = field.type === 'checkbox'
                ? rawValue === true
                : typeof rawValue === 'string'
                    ? rawValue.trim().length > 0
                    : rawValue !== undefined && rawValue !== null;

            if (field.required && !hasValue) {
                setError(`Please fill in the required field: ${field.label}`);
                return;
            }

            const trimmedValue = getTrimmedStringValue(rawValue);
            if (!trimmedValue || !field.validationPattern) {
                continue;
            }

            try {
                const pattern = new RegExp(field.validationPattern);
                if (!pattern.test(trimmedValue)) {
                    setError(field.validationMessage?.trim() || `Please enter a valid value for ${field.label}.`);
                    return;
                }
            } catch {
                setError('This form contains an invalid validation rule. Please contact the firm.');
                return;
            }
        }

        setSubmitting(true);

        try {
            const result = await api.intake.public.submit(slug, JSON.stringify(formData));
            setThankYouMessage(result.message || 'Thank you for your submission.');
            setSubmitted(true);

            if (result.redirectUrl) {
                setTimeout(() => {
                    window.location.href = result.redirectUrl;
                }, 3000);
            }
        } catch (err) {
            setError(extractErrorMessage(err, 'Failed to submit form. Please try again.'));
        } finally {
            setSubmitting(false);
        }
    };

    const renderField = (field: IntakeFormField) => {
        const baseInputClass = "w-full px-4 py-3 border border-slate-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 transition";

        switch (field.type) {
            case 'textarea':
                return (
                    <textarea
                        value={formData[field.name] || ''}
                        onChange={(e) => handleChange(field.name, e.target.value)}
                        placeholder={field.placeholder}
                        rows={4}
                        className={baseInputClass}
                        required={field.required}
                    />
                );

            case 'select':
                const selectOptions = parseFieldOptions(field.options);
                return (
                    <select
                        value={formData[field.name] || ''}
                        onChange={(e) => handleChange(field.name, e.target.value)}
                        className={baseInputClass}
                        required={field.required}
                    >
                        <option value="">{field.placeholder || 'Select an option...'}</option>
                        {selectOptions.map((opt, i) => (
                            <option key={i} value={opt}>{opt}</option>
                        ))}
                    </select>
                );

            case 'radio':
                const radioOptions = parseFieldOptions(field.options);
                return (
                    <div className="space-y-2">
                        {radioOptions.map((opt, i) => (
                            <label key={i} className="flex items-center gap-3 cursor-pointer">
                                <input
                                    type="radio"
                                    name={field.name}
                                    value={opt}
                                    checked={formData[field.name] === opt}
                                    onChange={(e) => handleChange(field.name, e.target.value)}
                                    className="w-4 h-4 text-blue-600"
                                    required={field.required}
                                />
                                <span>{opt}</span>
                            </label>
                        ))}
                    </div>
                );

            case 'checkbox':
                return (
                    <label className="flex items-center gap-3 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={Boolean(formData[field.name])}
                            onChange={(e) => handleChange(field.name, e.target.checked)}
                            className="w-5 h-5 rounded text-blue-600"
                        />
                        <span>{field.placeholder || 'Yes'}</span>
                    </label>
                );

            case 'date':
                return (
                    <input
                        type="date"
                        value={formData[field.name] || ''}
                        onChange={(e) => handleChange(field.name, e.target.value)}
                        className={baseInputClass}
                        required={field.required}
                    />
                );

            case 'file':
                return (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                        File uploads are coming soon. Please contact the firm directly to share documents for now.
                    </div>
                );

            default:
                return (
                    <input
                        type={field.type === 'email' ? 'email' : field.type === 'phone' ? 'tel' : 'text'}
                        value={formData[field.name] || ''}
                        onChange={(e) => handleChange(field.name, e.target.value)}
                        placeholder={field.placeholder}
                        className={baseInputClass}
                        required={field.required}
                    />
                );
        }
    };

    if (loading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-slate-50">
                <div className="w-8 h-8 border-2 border-blue-200 border-t-blue-600 rounded-full animate-spin" />
            </div>
        );
    }

    if (error && !form) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-slate-50">
                <div className="text-center">
                    <AlertCircle className="w-16 h-16 text-red-400 mx-auto mb-4" />
                    <h1 className="text-xl font-semibold text-slate-800 mb-2">Form Not Found</h1>
                    <p className="text-slate-500">{error}</p>
                </div>
            </div>
        );
    }

    if (submitted) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-green-50 to-emerald-100">
                <div className="text-center max-w-md px-6">
                    <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-6">
                        <CheckCircle className="w-10 h-10 text-green-600" />
                    </div>
                    <h1 className="text-2xl font-bold text-slate-800 mb-4">Thank You!</h1>
                    <p className="text-slate-600">{thankYouMessage}</p>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-gradient-to-br from-slate-50 to-blue-50 py-12 px-4">
            <div className="max-w-2xl mx-auto">
                {/* Header */}
                <div className="text-center mb-8">
                    <div className="w-16 h-16 bg-blue-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <FileText className="w-8 h-8 text-blue-600" />
                    </div>
                    <h1 className="text-3xl font-bold text-slate-800 mb-2">{form?.name}</h1>
                    {form?.description && (
                        <p className="text-slate-600">{form.description}</p>
                    )}
                    {form?.practiceArea && (
                        <span className="inline-block mt-2 px-3 py-1 bg-blue-100 text-blue-700 rounded-full text-sm">
                            {form.practiceArea}
                        </span>
                    )}
                </div>

                {/* Form */}
                <form onSubmit={handleSubmit} className="bg-white rounded-2xl shadow-xl p-8">
                    {hasUnsupportedFileField && (
                        <div className="mb-6 p-4 bg-amber-50 border border-amber-200 rounded-lg text-amber-800 flex items-center gap-2">
                            <AlertCircle className="w-5 h-5 flex-shrink-0" />
                            This form includes a file upload field. File uploads are coming soon, so submissions are temporarily disabled.
                        </div>
                    )}
                    {error && (
                        <div className="mb-6 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 flex items-center gap-2">
                            <AlertCircle className="w-5 h-5 flex-shrink-0" />
                            {error}
                        </div>
                    )}

                    <div className="space-y-6">
                        {fields.map(field => (
                            <div key={field.id}>
                                <label className="block text-sm font-medium text-slate-700 mb-2">
                                    {field.label}
                                    {field.required && <span className="text-red-500 ml-1">*</span>}
                                </label>
                                {renderField(field)}
                                {field.helpText && (
                                    <p className="mt-1 text-sm text-slate-500">{field.helpText}</p>
                                )}
                            </div>
                        ))}
                    </div>

                    <button
                        type="submit"
                        disabled={submitting || hasUnsupportedFileField}
                        className="w-full mt-8 py-4 bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-xl font-medium hover:from-blue-700 hover:to-indigo-700 disabled:opacity-50 transition flex items-center justify-center gap-2"
                    >
                        {submitting ? (
                            <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                        ) : (
                            <>
                                <Send className="w-5 h-5" />
                                {hasUnsupportedFileField ? 'File Uploads Coming Soon' : 'Submit Form'}
                            </>
                        )}
                    </button>

                    <p className="text-center text-sm text-slate-500 mt-4">
                        Your information is secure and will only be used to contact you about your inquiry.
                    </p>
                </form>
            </div>
        </div>
    );
}
