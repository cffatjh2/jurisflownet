'use client';

import { useState, useEffect } from 'react';
import { Calendar, Clock, AlertTriangle, ChevronRight, Plus, X, CheckCircle, Filter } from './Icons';
import { api } from '../services/api';

interface CourtRule {
    id: string;
    name: string;
    ruleType: string;
    jurisdiction: string;
    citation?: string;
    triggerEvent: string;
    daysCount: number;
    dayType: string;
    direction: string;
    serviceDaysAdd: number;
    description?: string;
}

interface CalculatedDeadline {
    triggerDate: string;
    dueDate: string;
    ruleName: string;
    ruleCitation?: string;
    daysCount: number;
    serviceDaysAdded: number;
    description: string;
}

interface Holiday {
    id: string;
    date: string;
    name: string;
    jurisdiction: string;
}

interface DeadlineCalculatorProps {
    matterId?: string;
    onDeadlineCreate?: (deadline: any) => void;
}

export default function DeadlineCalculator({ matterId, onDeadlineCreate }: DeadlineCalculatorProps) {
    const [rules, setRules] = useState<CourtRule[]>([]);
    const [jurisdictions, setJurisdictions] = useState<string[]>([]);
    const [loading, setLoading] = useState(true);

    const [selectedJurisdiction, setSelectedJurisdiction] = useState<string>('');
    const [selectedRule, setSelectedRule] = useState<string>('');
    const [triggerDate, setTriggerDate] = useState<string>(new Date().toISOString().split('T')[0]);
    const [serviceMethod, setServiceMethod] = useState<string>('Personal');

    const [holidays, setHolidays] = useState<Holiday[]>([]);

    const [calculatedDeadline, setCalculatedDeadline] = useState<CalculatedDeadline | null>(null);
    const [calculating, setCalculating] = useState(false);

    const [showCreateForm, setShowCreateForm] = useState(false);
    const [deadlineTitle, setDeadlineTitle] = useState('');
    const [creating, setCreating] = useState(false);
    const [reminderDays, setReminderDays] = useState(3);

    useEffect(() => {
        loadInitialData();
    }, []);

    useEffect(() => {
        if (selectedJurisdiction) {
            loadRulesForJurisdiction(selectedJurisdiction);
        }
    }, [selectedJurisdiction]);

    const loadInitialData = async () => {
        setLoading(true);
        try {
            const jurisdictionsData = await api.courtRules.jurisdictions();
            setJurisdictions(jurisdictionsData);

            // If no jurisdictions, seed default rules
            if (jurisdictionsData.length === 0) {
                await api.courtRules.seed();
                const newJurisdictions = await api.courtRules.jurisdictions();
                setJurisdictions(newJurisdictions);
            }
        } catch (error) {
            console.error('Failed to load jurisdictions:', error);
        } finally {
            setLoading(false);
        }
    };

    const loadRulesForJurisdiction = async (jurisdiction: string) => {
        try {
            const [rulesData, holidayData] = await Promise.all([
                api.courtRules.list({ jurisdiction }),
                api.holidays.list(jurisdiction)
            ]);
            setRules(rulesData);
            setSelectedRule('');
            setCalculatedDeadline(null);
            setHolidays(Array.isArray(holidayData) ? holidayData : []);
        } catch (error) {
            console.error('Failed to load rules:', error);
        }
    };

    const calculateDeadline = async () => {
        if (!selectedRule) return;

        setCalculating(true);
        try {
            const result = await api.deadlines.calculate({
                courtRuleId: selectedRule,
                triggerDate: triggerDate,
                serviceMethod: serviceMethod
            });
            setCalculatedDeadline(result);
            setDeadlineTitle(result.ruleName);
        } catch (error) {
            console.error('Failed to calculate deadline:', error);
        } finally {
            setCalculating(false);
        }
    };

    const createDeadline = async () => {
        if (!matterId || !calculatedDeadline || !deadlineTitle) return;

        setCreating(true);
        try {
            const deadline = await api.deadlines.create({
                matterId,
                title: deadlineTitle,
                dueDate: calculatedDeadline.dueDate,
                description: calculatedDeadline.description,
                deadlineType: 'Filing',
                reminderDays
            });
            onDeadlineCreate?.(deadline);
            setShowCreateForm(false);
            setCalculatedDeadline(null);
            setSelectedRule('');
        } catch (error) {
            console.error('Failed to create deadline:', error);
        } finally {
            setCreating(false);
        }
    };

    const formatDate = (dateString: string) => {
        return new Date(dateString).toLocaleDateString('en-US', {
            weekday: 'long',
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    const formatShortDate = (dateString: string) => {
        return new Date(dateString).toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        });
    };

    const getDaysUntil = (dateString: string) => {
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        const due = new Date(dateString);
        due.setHours(0, 0, 0, 0);
        const diff = Math.ceil((due.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
        return diff;
    };

    if (loading) {
        return (
            <div className="p-8 text-center">
                <div className="w-8 h-8 border-2 border-blue-200 border-t-blue-600 rounded-full animate-spin mx-auto" />
                <p className="text-slate-500 mt-2">Loading court rules...</p>
            </div>
        );
    }

    return (
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">
            {/* Header */}
            <div className="px-6 py-4 border-b bg-gradient-to-r from-indigo-50 to-purple-50">
                <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-full bg-indigo-100 flex items-center justify-center">
                        <Calendar className="w-5 h-5 text-indigo-600" />
                    </div>
                    <div>
                        <h2 className="text-lg font-semibold text-slate-800">Deadline Calculator</h2>
                        <p className="text-sm text-slate-500">Calculate deadlines based on court rules</p>
                    </div>
                </div>
            </div>

            {/* Calculator Form */}
            <div className="p-6 space-y-4">
                {/* Jurisdiction Selection */}
                <div>
                    <label className="block text-sm font-medium text-slate-700 mb-1">
                        Jurisdiction
                    </label>
                    <select
                        value={selectedJurisdiction}
                        onChange={(e) => setSelectedJurisdiction(e.target.value)}
                        className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">Select jurisdiction...</option>
                        {jurisdictions.map(j => (
                            <option key={j} value={j}>{j}</option>
                        ))}
                    </select>
                </div>

                {/* Rule Selection */}
                {selectedJurisdiction && (
                    <div>
                        <label className="block text-sm font-medium text-slate-700 mb-1">
                            Court Rule
                        </label>
                        <select
                            value={selectedRule}
                            onChange={(e) => setSelectedRule(e.target.value)}
                            className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-indigo-500"
                        >
                            <option value="">Select rule...</option>
                            {rules.map(rule => (
                                <option key={rule.id} value={rule.id}>
                                    {rule.name} ({rule.daysCount} {rule.dayType.toLowerCase()} days)
                                </option>
                            ))}
                        </select>
                    </div>
                )}

                {/* Trigger Date & Service Method */}
                {selectedRule && (
                    <div className="grid grid-cols-2 gap-4">
                        <div>
                            <label className="block text-sm font-medium text-slate-700 mb-1">
                                Trigger Date
                            </label>
                            <input
                                type="date"
                                value={triggerDate}
                                onChange={(e) => setTriggerDate(e.target.value)}
                                className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-indigo-500"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-slate-700 mb-1">
                                Service Method
                            </label>
                            <select
                                value={serviceMethod}
                                onChange={(e) => setServiceMethod(e.target.value)}
                                className="w-full px-4 py-2 border border-slate-200 rounded-lg focus:ring-2 focus:ring-indigo-500"
                            >
                                <option value="Personal">Personal Service</option>
                                <option value="Mail">Mail Service (rule-based)</option>
                                <option value="Electronic">Electronic Service (+2 days)</option>
                            </select>
                        </div>
                    </div>
                )}

                {/* Calculate Button */}
                {selectedRule && (
                    <button
                        onClick={calculateDeadline}
                        disabled={calculating}
                        className="w-full py-3 bg-indigo-600 text-white rounded-lg font-medium hover:bg-indigo-700 disabled:opacity-50 transition flex items-center justify-center gap-2"
                    >
                        {calculating ? (
                            <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                        ) : (
                            <Calendar className="w-5 h-5" />
                        )}
                        Calculate Deadline
                    </button>
                )}
            </div>

            {/* Calculated Result */}
            {calculatedDeadline && (
                <div className="border-t">
                    <div className="p-6 bg-gradient-to-br from-green-50 to-emerald-50">
                        <div className="flex items-start gap-4">
                            <div className="w-12 h-12 rounded-full bg-green-100 flex items-center justify-center flex-shrink-0">
                                <CheckCircle className="w-6 h-6 text-green-600" />
                            </div>
                            <div className="flex-1">
                                <h3 className="font-semibold text-slate-800 text-lg">
                                    {formatDate(calculatedDeadline.dueDate)}
                                </h3>
                                <p className="text-sm text-slate-600 mt-1">
                                    {calculatedDeadline.description}
                                </p>
                                {calculatedDeadline.ruleCitation && (
                                    <p className="text-xs text-slate-500 mt-1">
                                        Citation: {calculatedDeadline.ruleCitation}
                                    </p>
                                )}
                                {calculatedDeadline.serviceDaysAdded > 0 && (
                                    <p className="text-xs text-slate-500 mt-1">
                                        Includes +{calculatedDeadline.serviceDaysAdded} service days.
                                    </p>
                                )}
                                <div className="mt-3 flex items-center gap-3">
                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${getDaysUntil(calculatedDeadline.dueDate) <= 7
                                            ? 'bg-red-100 text-red-700'
                                            : getDaysUntil(calculatedDeadline.dueDate) <= 14
                                                ? 'bg-yellow-100 text-yellow-700'
                                                : 'bg-green-100 text-green-700'
                                        }`}>
                                        {getDaysUntil(calculatedDeadline.dueDate)} days from today
                                    </span>
                                </div>
                            </div>
                        </div>

                        {/* Create Deadline */}
                        {matterId && !showCreateForm && (
                            <button
                                onClick={() => setShowCreateForm(true)}
                                className="mt-4 w-full py-2 border-2 border-dashed border-green-300 text-green-700 rounded-lg hover:bg-green-100 transition flex items-center justify-center gap-2"
                            >
                                <Plus className="w-4 h-4" />
                                Add to Matter Deadlines
                            </button>
                        )}

                        {showCreateForm && matterId && (
                            <div className="mt-4 p-4 bg-white rounded-lg border border-green-200">
                                <input
                                    type="text"
                                    value={deadlineTitle}
                                    onChange={(e) => setDeadlineTitle(e.target.value)}
                                    placeholder="Deadline title"
                                    className="w-full px-3 py-2 border border-slate-200 rounded-lg mb-3"
                                />
                                <div className="mb-3">
                                    <label className="block text-xs font-semibold text-slate-600 mb-1">
                                        Reminder (days before due date)
                                    </label>
                                    <input
                                        type="number"
                                        min={0}
                                        max={60}
                                        value={reminderDays}
                                        onChange={(e) => setReminderDays(Math.max(0, Number(e.target.value)))}
                                        className="w-full px-3 py-2 border border-slate-200 rounded-lg"
                                    />
                                </div>
                                <div className="flex gap-2">
                                    <button
                                        onClick={createDeadline}
                                        disabled={creating || !deadlineTitle}
                                        className="flex-1 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 transition"
                                    >
                                        {creating ? 'Creating...' : 'Create Deadline'}
                                    </button>
                                    <button
                                        onClick={() => setShowCreateForm(false)}
                                        className="px-4 py-2 border border-slate-200 rounded-lg hover:bg-slate-50 transition"
                                    >
                                        <X className="w-4 h-4" />
                                    </button>
                                </div>
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* Rule Info */}
            {selectedRule && !calculatedDeadline && (
                <div className="border-t p-4 bg-slate-50 space-y-4">
                    {rules.filter(r => r.id === selectedRule).map(rule => (
                        <div key={rule.id} className="text-sm text-slate-600">
                            <p><strong>Rule:</strong> {rule.name}</p>
                            {rule.citation && <p><strong>Citation:</strong> {rule.citation}</p>}
                            <p><strong>Calculation:</strong> {rule.daysCount} {rule.dayType.toLowerCase()} days {rule.direction.toLowerCase()} {rule.triggerEvent}</p>
                            {rule.serviceDaysAdd > 0 && (
                                <p className="text-amber-600">
                                    <AlertTriangle className="w-4 h-4 inline mr-1" />
                                    Mail service adds +{rule.serviceDaysAdd} days. Electronic service adds +2 days.
                                </p>
                            )}
                        </div>
                    ))}

                    {holidays.length > 0 && (
                        <div className="bg-white border border-slate-200 rounded-lg p-3 text-sm text-slate-600">
                            <div className="flex items-center justify-between">
                                <strong>Court Holidays</strong>
                                <span className="text-xs text-slate-500">Includes US Federal + {selectedJurisdiction}</span>
                            </div>
                            <div className="mt-2 grid grid-cols-1 sm:grid-cols-2 gap-2">
                                {holidays
                                    .filter(h => new Date(h.date) >= new Date())
                                    .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
                                    .slice(0, 6)
                                    .map(h => (
                                        <div key={h.id} className="flex items-center justify-between rounded-md border border-slate-100 px-2 py-1">
                                            <span className="text-xs font-medium text-slate-700">{h.name}</span>
                                            <span className="text-xs text-slate-500">{formatShortDate(h.date)}</span>
                                        </div>
                                    ))}
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
