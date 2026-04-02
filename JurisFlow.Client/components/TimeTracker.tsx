import React, { useState, useEffect, useRef } from 'react';
import { useData } from '../contexts/DataContext';
import { useTranslation } from '../contexts/LanguageContext';
import { Clock, Pause, Timer, CheckSquare, CreditCard, Plus, Camera, Check, XCircle } from './Icons';
import Tesseract from 'tesseract.js';
import { TimeEntry, Expense, ActivityCode, ExpenseCode } from '../types';
import { toast } from './Toast';
import { useAuth } from '../contexts/AuthContext';

const TimeTracker = () => {
    const { t, formatCurrency, formatDate } = useTranslation();
    const { matters, addTimeEntry, addExpense, timeEntries, expenses, activeTimer, startTimer, stopTimer, pauseTimer, resumeTimer, approveTimeEntry, rejectTimeEntry, approveExpense, rejectExpense } = useData();
    const { can } = useAuth();
    const canApprove = can('billing.approve');

    const [activeTab, setActiveTab] = useState<'time' | 'expense'>('time');
    const [showBilled, setShowBilled] = useState(false);
    const [approvalFilter, setApprovalFilter] = useState<'all' | 'pending' | 'approved' | 'rejected'>('all');
    const [rejectDialogOpen, setRejectDialogOpen] = useState(false);
    const [rejectReason, setRejectReason] = useState('');
    const [rejectTarget, setRejectTarget] = useState<TimeEntry | Expense | null>(null);
    const [rejectSubmitting, setRejectSubmitting] = useState(false);

    // Display-only state (synced with activeTimer)
    const [timerDisplay, setTimerDisplay] = useState(0);

    // Interval ref for UI updating
    const intervalRef = useRef<any>(null);

    // Form State
    const [selectedMatterId, setSelectedMatterId] = useState('');
    const [description, setDescription] = useState('');
    const [hourlyRate, setHourlyRate] = useState<number | string>(450);
    const [activityCode, setActivityCode] = useState<string>(ActivityCode.A103);
    const [isBillable, setIsBillable] = useState(true);

    // Sync form with active timer on load
    useEffect(() => {
        if (activeTimer) {
            setSelectedMatterId(activeTimer.matterId || '');
            setDescription(activeTimer.description || '');
            // Calculate initial display time
            let totalSeconds = Math.floor(activeTimer.elapsed / 1000);
            if (activeTimer.isRunning) {
                totalSeconds += Math.floor((Date.now() - activeTimer.startTime) / 1000);
            }
            setTimerDisplay(totalSeconds);
            // Sync rate if it exists in timer
            if (activeTimer.rate) {
                setHourlyRate(activeTimer.rate);
            }
            if (activeTimer.activityCode) {
                setActivityCode(activeTimer.activityCode);
            }
            if (activeTimer.isBillable !== undefined) {
                setIsBillable(activeTimer.isBillable);
            }
        }
    }, [activeTimer?.startTime, activeTimer?.elapsed]);

    // Update rate when matter is selected (if no active timer to avoid overwriting running timer rate)
    useEffect(() => {
        if (!activeTimer && selectedMatterId) {
            const m = matters.find(matter => matter.id === selectedMatterId);
            if (m && m.billableRate) {
                setHourlyRate(m.billableRate);
            }
        }
    }, [selectedMatterId, matters, activeTimer]);

    useEffect(() => {
        if (matters.length > 0 && selectedMatterId && !matters.some(matter => matter.id === selectedMatterId)) {
            setSelectedMatterId('');
            toast.warning('Selected matter is no longer available. Please reselect.');
        }
    }, [matters, selectedMatterId]);

    // Expense Form State
    const [expenseAmount, setExpenseAmount] = useState('');
    const [expenseCategory, setExpenseCategory] = useState<string>(ExpenseCode.E112);
    const [expenseDesc, setExpenseDesc] = useState('');
    const [expenseMatterId, setExpenseMatterId] = useState('');
    const [isScanning, setIsScanning] = useState(false);
    const fileInputRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        if (matters.length > 0 && expenseMatterId && !matters.some(matter => matter.id === expenseMatterId)) {
            setExpenseMatterId('');
            toast.warning('Selected matter is no longer available. Please reselect.');
        }
    }, [matters, expenseMatterId]);

    const handleScan = async (e: React.ChangeEvent<HTMLInputElement>) => {
        if (!e.target.files || e.target.files.length === 0) return;

        setIsScanning(true);
        const file = e.target.files[0];

        try {
            const { data: { text } } = await Tesseract.recognize(
                file,
                'tur+eng', // Try both Turkish and English
                { logger: m => console.log(m) }
            );

            // Simple heuristics extraction
            console.log('OCR Text:', text);
            let extractedAmount = '';
            let extractedDesc = '';

            // Try to find amount (looking for currency symbols or "Total")
            const amountMatches = text.match(/(?:Total|Tutar|Toplam|Amount|Grand Total).*?(\d+[.,]\d{2})/i);
            if (amountMatches && amountMatches[1]) {
                extractedAmount = amountMatches[1].replace(',', '.');
            } else {
                // Fallback: look for largest number that looks like a price
                const prices = text.match(/\d+[.,]\d{2}/g);
                if (prices && prices.length > 0) {
                    // rudimentary guess: usually total is near the end or is the largest? 
                    // Let's just take the first one found after typical keywords if above failed, 
                    // or just leave it blank to avoid bad guesses.
                    // Actually, let's try to match standalone numbers with currency signs
                    const currencyMatch = text.match(/[₺$€£]\s*(\d+[.,]\d{2})/);
                    if (currencyMatch) extractedAmount = currencyMatch[1].replace(',', '.');
                }
            }

            // Try to find description (Vendor name usually at top)
            const lines = text.split('\n').filter(l => l.trim().length > 3);
            if (lines.length > 0) {
                // First non-empty line is often the vendor name
                extractedDesc = lines[0].trim();
            }

            if (extractedAmount) setExpenseAmount(extractedAmount);
            if (extractedDesc) setExpenseDesc(`Invoice: ${extractedDesc}`);

            toast.success('Invoice scanned! Please verify the information.');

        } catch (error) {
            console.error(error);
            toast.error('OCR operation failed.');
        } finally {
            setIsScanning(false);
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    // --- TIMER UI LOOP ---
    useEffect(() => {
        if (activeTimer && activeTimer.isRunning) {
            intervalRef.current = setInterval(() => {
                const now = Date.now();
                const currentSession = Math.floor((now - activeTimer.startTime) / 1000);
                const total = Math.floor(activeTimer.elapsed / 1000) + currentSession;
                setTimerDisplay(total);
            }, 1000);
        } else {
            clearInterval(intervalRef.current);
        }
        return () => clearInterval(intervalRef.current);
    }, [activeTimer]); // Depend on activeTimer state

    const handleToggleTimer = () => {
        if (!activeTimer) {
            // START NEW TIMER
            startTimer(selectedMatterId || undefined, description, Number(hourlyRate) || 0, activityCode, isBillable);
        } else {
            if (activeTimer.isRunning) {
                pauseTimer();
            } else {
                resumeTimer();
            }
        }
    };

    const handleSaveTimeEntry = async () => {
        if (!activeTimer) return;

        if (matters.length > 0 && activeTimer.matterId && !matters.some(matter => matter.id === activeTimer.matterId)) {
            toast.warning('Selected matter was not found. Time entry will be logged as unassigned.');
        }

        toast.info('Saving time entry...', 2500);
        setTimerDisplay(0);
        const saved = await stopTimer();
        if (!saved) {
            toast.error('Failed to save time entry. Timer was restored.');
            return;
        }

        toast.success('Time Entry Saved');
        setDescription('');
    };

    const saveExpenseEntry = async () => {
        // Only amount is required, matter is optional (can be "Free/Unassigned")
        if (!expenseAmount) {
            toast.error('Please fill in required fields.');
            return;
        }

        const hasValidMatter = !expenseMatterId || matters.some(matter => matter.id === expenseMatterId);
        if (expenseMatterId && matters.length > 0 && !hasValidMatter) {
            toast.warning('Selected matter was not found. Expense will be logged as unassigned.');
        }

        const newExpense: Expense = {
            id: `e${Date.now()}`,
            matterId: hasValidMatter ? expenseMatterId || undefined : undefined, // Optional - can be null for "Free/Unassigned"
            description: expenseDesc || expenseCategory,
            amount: parseFloat(expenseAmount),
            date: new Date().toISOString(),
            category: expenseCategory as any,
            billed: false,
            type: 'expense',
            expenseCode: expenseCategory
        };

        const saved = await addExpense(newExpense);
        if (saved) {
            toast.success('Expense Logged');
            setExpenseAmount('');
            setExpenseDesc('');
            if (!hasValidMatter) {
                setExpenseMatterId('');
            }
        } else {
            toast.error('Failed to log expense. Please try again.');
        }
    };

    const normalizeApprovalStatus = (status?: string) => {
        if (!status) return 'Approved';
        const normalized = status.toLowerCase();
        if (normalized === 'approved') return 'Approved';
        if (normalized === 'pending') return 'Pending';
        if (normalized === 'rejected') return 'Rejected';
        return status;
    };

    const getApprovalBadgeClass = (status: string) => {
        if (status === 'Approved') return 'bg-emerald-100 text-emerald-700';
        if (status === 'Pending') return 'bg-yellow-100 text-yellow-700';
        if (status === 'Rejected') return 'bg-red-100 text-red-700';
        return 'bg-gray-100 text-gray-600';
    };

    const handleApproveEntry = async (entry: TimeEntry | Expense) => {
        const ok = entry.type === 'expense'
            ? await approveExpense(entry.id)
            : await approveTimeEntry(entry.id);
        if (ok) {
            toast.success('Entry approved');
        } else {
            toast.error('Approval failed. Please try again.');
        }
    };

    const handleRejectEntry = (entry: TimeEntry | Expense) => {
        setRejectTarget(entry);
        setRejectReason('');
        setRejectDialogOpen(true);
    };

    const submitRejectEntry = async () => {
        if (!rejectTarget) return;
        setRejectSubmitting(true);
        const reason = rejectReason.trim() || undefined;
        const ok = rejectTarget.type === 'expense'
            ? await rejectExpense(rejectTarget.id, reason)
            : await rejectTimeEntry(rejectTarget.id, reason);
        setRejectSubmitting(false);
        setRejectDialogOpen(false);
        setRejectTarget(null);
        setRejectReason('');
        if (ok) {
            toast.success('Entry rejected');
        } else {
            toast.error('Rejection failed. Please try again.');
        }
    };

    const formatSeconds = (sec: number) => {
        const h = Math.floor(sec / 3600).toString().padStart(2, '0');
        const m = Math.floor((sec % 3600) / 60).toString().padStart(2, '0');
        const s = (sec % 60).toString().padStart(2, '0');
        return `${h}:${m}:${s}`;
    };

    // Combine and sort list
    const allEntries = [
        ...timeEntries.map(x => ({ ...x, sortDate: new Date(x.date) })),
        ...expenses.map(x => ({ ...x, sortDate: new Date(x.date) }))
    ]
        .filter(entry => showBilled ? true : !entry.billed)
        .sort((a, b) => b.sortDate.getTime() - a.sortDate.getTime());

    const approvalCounts = allEntries.reduce((acc, entry) => {
        const status = normalizeApprovalStatus(entry.approvalStatus) || 'Pending';
        if (status === 'Pending') acc.pending += 1;
        if (status === 'Approved') acc.approved += 1;
        if (status === 'Rejected') acc.rejected += 1;
        return acc;
    }, { pending: 0, approved: 0, rejected: 0 });

    const filteredEntries = allEntries.filter(entry => {
        if (approvalFilter === 'all') return true;
        const status = (normalizeApprovalStatus(entry.approvalStatus) || 'pending').toLowerCase();
        return status === approvalFilter;
    });

    const isRunning = activeTimer?.isRunning || false;
    const hasActiveTimer = !!activeTimer;

    return (
        <div className="h-full flex flex-col bg-gray-50 overflow-hidden relative">

            {/* HEADER SECTION */}
            <div className="px-8 pt-8 pb-6 bg-white border-b border-gray-200 shrink-0">
                <div className="flex justify-between items-center mb-6">
                    <div>
                        <h1 className="text-2xl font-bold text-slate-900">{t('time_title')}</h1>
                        <p className="text-gray-500 text-sm">{t('time_subtitle')}</p>
                    </div>
                    <div className="flex bg-gray-100 p-1 rounded-lg">
                        <button
                            onClick={() => setActiveTab('time')}
                            className={`px-4 py-2 text-sm font-bold rounded-md transition-all ${activeTab === 'time' ? 'bg-white text-slate-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}
                        >
                            {t('tab_time')}
                        </button>
                        <button
                            onClick={() => setActiveTab('expense')}
                            className={`px-4 py-2 text-sm font-bold rounded-md transition-all ${activeTab === 'expense' ? 'bg-white text-slate-900 shadow-sm' : 'text-gray-500 hover:text-gray-700'}`}
                        >
                            {t('tab_expense')}
                        </button>
                    </div>
                </div>

                {/* CONTROLS AREA */}
                {activeTab === 'time' ? (
                    <div className="flex flex-col md:flex-row gap-6 items-end">
                        {/* Timer Display */}
                        <div className="bg-slate-900 text-white px-6 py-3 rounded-xl shadow-lg flex flex-col items-center justify-center min-w-[180px]">
                            <span className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-1">Duration</span>
                            <span className="text-4xl font-mono font-bold tabular-nums tracking-wider">{formatSeconds(timerDisplay)}</span>
                        </div>

                        {/* Inputs */}
                        <div className="flex-1 grid grid-cols-1 md:grid-cols-3 gap-4 w-full">
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">{t('select_matter')}</label>
                                <select
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm focus:ring-2 focus:ring-primary-500 outline-none"
                                    value={selectedMatterId}
                                    onChange={e => setSelectedMatterId(e.target.value)}
                                    disabled={isRunning} // Disable changing matter while timer is running
                                >
                                    <option value="">Free/Unassigned</option>
                                    {matters.map(m => <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>)}
                                </select>
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">{t('description')}</label>
                                <input
                                    type="text"
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm focus:ring-2 focus:ring-primary-500 outline-none"
                                    placeholder="Drafting motions..."
                                    value={description}
                                    onChange={e => setDescription(e.target.value)}
                                    disabled={isRunning} // Disable desc change while timer is running
                                />
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">Activity Code</label>
                                <select
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm focus:ring-2 focus:ring-primary-500 outline-none"
                                    value={activityCode}
                                    onChange={e => setActivityCode(e.target.value)}
                                    disabled={isRunning}
                                >
                                    {Object.values(ActivityCode).map(code => (
                                        <option key={code} value={code}>{code}</option>
                                    ))}
                                </select>
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">{t('hourly_rate')}</label>
                                <div className="flex items-center gap-2">
                                    <div className="relative flex-1">
                                        <span className="absolute left-3 top-2.5 text-gray-500 text-sm">$</span>
                                        <input
                                            type="number"
                                            className="pl-6 border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm focus:ring-2 focus:ring-primary-500 outline-none w-full"
                                            value={hourlyRate}
                                            onChange={e => {
                                                const val = e.target.value;
                                                setHourlyRate(val === '' ? '' : parseFloat(val));
                                            }}
                                        />
                                    </div>
                                    <button
                                        type="button"
                                        onClick={() => setIsBillable(!isBillable)}
                                        className={`px-3 py-2.5 rounded-lg text-xs font-bold border transition-colors ${isBillable
                                                ? 'bg-green-50 text-green-700 border-green-200'
                                                : 'bg-gray-50 text-gray-500 border-gray-200'
                                            }`}
                                    >
                                        {isBillable ? '✓ Billable' : 'Non-Billable'}
                                    </button>
                                </div>
                            </div>
                        </div>

                        {/* Buttons */}
                        <div className="flex gap-2 min-w-[240px]">
                            <button
                                onClick={handleToggleTimer}
                                className={`flex-1 py-3 px-4 rounded-xl font-bold text-white shadow-md transition-transform active:scale-95 flex items-center justify-center gap-2 ${isRunning ? 'bg-amber-500 hover:bg-amber-600' : 'bg-primary-600 hover:bg-primary-700'}`}
                            >
                                {isRunning ? <Pause className="w-5 h-5" /> : <Timer className="w-5 h-5" />}
                                {isRunning ? "Pause" : (hasActiveTimer ? "Resume" : "Start")}
                            </button>
                            <button
                                onClick={handleSaveTimeEntry}
                                disabled={!hasActiveTimer}
                                className="flex-1 py-3 px-4 rounded-xl font-bold text-white bg-emerald-600 hover:bg-emerald-700 shadow-md transition-transform active:scale-95 flex items-center justify-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                <CheckSquare className="w-5 h-5" />
                                Save
                            </button>
                        </div>
                    </div>
                ) : (
                    // EXPENSE TAB (Keep as is)
                    <div className="flex flex-col md:flex-row gap-6 items-end animate-in fade-in slide-in-from-top-4 duration-300 relative">
                        {isScanning && (
                            <div className="absolute inset-0 bg-white/80 z-10 flex items-center justify-center rounded-xl backdrop-blur-sm">
                                <div className="flex flex-col items-center">
                                    <div className="w-8 h-8 border-4 border-slate-900 border-t-transparent rounded-full animate-spin mb-2"></div>
                                    <p className="text-sm font-bold text-slate-800">Scanning Invoice...</p>
                                </div>
                            </div>
                        )}
                        <input
                            type="file"
                            ref={fileInputRef}
                            className="hidden"
                            accept="image/*,.pdf"
                            onChange={handleScan}
                        />
                        <div className="flex-1 grid grid-cols-1 md:grid-cols-4 gap-4 w-full">
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">{t('select_matter')}</label>
                                <select
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm outline-none"
                                    value={expenseMatterId}
                                    onChange={e => setExpenseMatterId(e.target.value)}
                                >
                                    <option value="">Free/Unassigned</option>
                                    {matters.map(m => <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>)}
                                </select>
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1 flex justify-between items-center">
                                    {t('expense_amount')}
                                    <button
                                        onClick={() => fileInputRef.current?.click()}
                                        className="text-xs text-primary-600 hover:text-primary-800 flex items-center gap-1 font-bold"
                                        title="Scan Invoice"
                                    >
                                        <Camera className="w-3 h-3" /> Scan
                                    </button>
                                </label>
                                <div className="relative">
                                    <span className="absolute left-3 top-2.5 text-gray-500 text-sm">$</span>
                                    <input
                                        type="number"
                                        className="pl-6 border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm outline-none w-full"
                                        placeholder="0.00"
                                        value={expenseAmount}
                                        onChange={e => setExpenseAmount(e.target.value)}
                                    />
                                </div>
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">Expense Code</label>
                                <select
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm outline-none"
                                    value={expenseCategory}
                                    onChange={e => setExpenseCategory(e.target.value)}
                                >
                                    {Object.values(ExpenseCode).map(code => (
                                        <option key={code} value={code}>{code}</option>
                                    ))}
                                </select>
                            </div>
                            <div className="flex flex-col">
                                <label className="text-xs font-bold text-gray-500 uppercase mb-1">{t('description')}</label>
                                <input
                                    type="text"
                                    className="border border-gray-300 rounded-lg p-2.5 bg-white text-slate-900 text-sm outline-none"
                                    placeholder="Details..."
                                    value={expenseDesc}
                                    onChange={e => setExpenseDesc(e.target.value)}
                                />
                            </div>
                        </div>
                        <button
                            onClick={saveExpenseEntry}
                            className="py-3 px-8 rounded-xl font-bold text-white bg-slate-900 hover:bg-slate-800 shadow-md transition-transform active:scale-95 whitespace-nowrap"
                        >
                            {t('save_expense')}
                        </button>
                    </div>
                )}
            </div>

            {/* LIST SECTION - THIS IS THE FIX FOR SCROLLING */}
            <div className="flex-1 min-h-0 bg-gray-50 p-8 flex flex-col">
                <div className="bg-white border border-gray-200 rounded-xl shadow-sm flex flex-col h-full overflow-hidden">
                    <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-gray-50/50">
                        <div className="flex items-center gap-3">
                            <h3 className="font-bold text-slate-800">{t('recent_entries')}</h3>
                            <span className="text-xs bg-gray-200 text-gray-600 px-2 py-1 rounded-full font-bold">{filteredEntries.length} Items</span>
                        </div>
                        <div className="flex flex-wrap items-center gap-3">
                            <div className="flex items-center gap-2 text-xs font-bold text-gray-500">
                                <button
                                    onClick={() => setApprovalFilter('all')}
                                    className={`px-2.5 py-1 rounded-full border ${approvalFilter === 'all' ? 'bg-slate-900 text-white border-slate-900' : 'border-gray-200 text-gray-600 hover:border-gray-300'}`}
                                >
                                    All
                                </button>
                                <button
                                    onClick={() => setApprovalFilter('pending')}
                                    className={`px-2.5 py-1 rounded-full border ${approvalFilter === 'pending' ? 'bg-amber-500 text-white border-amber-500' : 'border-gray-200 text-gray-600 hover:border-gray-300'}`}
                                >
                                    Pending ({approvalCounts.pending})
                                </button>
                                <button
                                    onClick={() => setApprovalFilter('approved')}
                                    className={`px-2.5 py-1 rounded-full border ${approvalFilter === 'approved' ? 'bg-emerald-600 text-white border-emerald-600' : 'border-gray-200 text-gray-600 hover:border-gray-300'}`}
                                >
                                    Approved ({approvalCounts.approved})
                                </button>
                                <button
                                    onClick={() => setApprovalFilter('rejected')}
                                    className={`px-2.5 py-1 rounded-full border ${approvalFilter === 'rejected' ? 'bg-red-600 text-white border-red-600' : 'border-gray-200 text-gray-600 hover:border-gray-300'}`}
                                >
                                    Rejected ({approvalCounts.rejected})
                                </button>
                            </div>
                            <label className="flex items-center gap-2 text-xs font-bold text-gray-500 cursor-pointer select-none">
                                <input
                                    type="checkbox"
                                    checked={showBilled}
                                    onChange={e => setShowBilled(e.target.checked)}
                                    className="rounded border-gray-300 text-slate-900 focus:ring-slate-900"
                                />
                                Show Billed / Archived
                            </label>
                        </div>
                    </div>

                    {/* SCROLLABLE TABLE CONTAINER */}
                    <div className="flex-1 overflow-y-auto">
                        {filteredEntries.length === 0 ? (
                            <div className="flex flex-col items-center justify-center h-full text-gray-400">
                                <Clock className="w-12 h-12 mb-2 opacity-20" />
                                <p>No activity logged yet.</p>
                            </div>
                        ) : (
                            <table className="w-full text-left border-collapse">
                                <thead className="bg-white sticky top-0 z-10 shadow-sm">
                                    <tr>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider w-10"></th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('col_date')}</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('case_name')}</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('description')}</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('duration')}</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">{t('status')}</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider">Approval</th>
                                        <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider text-right">{t('expense_amount')} / Cost</th>
                                        {canApprove && (
                                            <th className="px-6 py-3 text-xs font-bold text-gray-500 uppercase tracking-wider text-right">Actions</th>
                                        )}
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-100">
                                    {filteredEntries.map((entry) => {
                                        const matter = matters.find(m => m.id === entry.matterId);
                                        const isExp = entry.type === 'expense';
                                        const isLocalOnly = entry.id.startsWith('te-') || entry.id.startsWith('e-');
                                        const approvalStatus = normalizeApprovalStatus(entry.approvalStatus);
                                        const canReview = canApprove && approvalStatus === 'Pending' && !entry.billed && !isLocalOnly;
                                        // Calculate cost correctly: Expense Amount OR (Minutes / 60) * Rate
                                        const cost = isExp
                                            ? (entry as Expense).amount
                                            : ((entry as TimeEntry).duration / 60) * (entry as TimeEntry).rate;

                                        return (
                                            <tr key={entry.id} className="hover:bg-gray-50 transition-colors">
                                                <td className="px-6 py-4">
                                                    <div className={`p-1.5 rounded-lg w-fit ${isExp ? 'bg-orange-100 text-orange-600' : 'bg-blue-100 text-blue-600'}`}>
                                                        {isExp ? <CreditCard className="w-4 h-4" /> : <Clock className="w-4 h-4" />}
                                                    </div>
                                                </td>
                                                <td className="px-6 py-4 text-sm font-mono text-gray-500">{formatDate(entry.date)}</td>
                                                <td className="px-6 py-4 text-sm font-bold text-slate-700">{matter?.name || 'Unknown Matter'}</td>
                                                <td className="px-6 py-4 text-sm text-gray-600 max-w-xs truncate">{entry.description}</td>
                                                <td className="px-6 py-4 text-sm text-gray-600">
                                                    {isExp ? '-' : (
                                                        <span className="font-mono">{Math.floor((entry as TimeEntry).duration / 60)}h {(entry as TimeEntry).duration % 60}m</span>
                                                    )}
                                                </td>
                                                <td className="px-6 py-4">
                                                    <span className={`px-2 py-1 text-xs font-bold rounded ${entry.billed ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                                                        {entry.billed ? 'Billed' : 'Unbilled'}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4">
                                                    <span
                                                        title={approvalStatus === 'Rejected' ? (entry as any).rejectionReason || 'Rejected' : undefined}
                                                        className={`px-2 py-1 text-xs font-bold rounded ${getApprovalBadgeClass(approvalStatus)}`}
                                                    >
                                                        {approvalStatus}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4 text-sm font-bold text-slate-800 text-right">
                                                    {formatCurrency(cost)}
                                                </td>
                                                {canApprove && (
                                                    <td className="px-6 py-4 text-right">
                                                        {canReview && (
                                                            <div className="inline-flex items-center gap-2">
                                                                <button
                                                                    onClick={() => handleApproveEntry(entry as any)}
                                                                    className="text-emerald-600 hover:text-emerald-800"
                                                                    title="Approve"
                                                                >
                                                                    <Check className="w-4 h-4" />
                                                                </button>
                                                                <button
                                                                    onClick={() => handleRejectEntry(entry as any)}
                                                                    className="text-red-600 hover:text-red-800"
                                                                    title="Reject"
                                                                >
                                                                    <XCircle className="w-4 h-4" />
                                                                </button>
                                                            </div>
                                                        )}
                                                    </td>
                                                )}
                                            </tr>
                                        )
                                    })}
                                </tbody>
                            </table>
                        )}
                    </div>
                </div>
            </div>
            {rejectDialogOpen && (
                <div className="fixed inset-0 z-[10000] bg-black/50 flex items-center justify-center p-4">
                    <div className="w-full max-w-md bg-white rounded-xl shadow-2xl border border-gray-200 overflow-hidden">
                        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
                            <div>
                                <h3 className="font-bold text-slate-900">Reject Entry</h3>
                                <p className="text-sm text-gray-500">Provide an optional reason for this rejection.</p>
                            </div>
                            <button
                                onClick={() => {
                                    if (!rejectSubmitting) {
                                        setRejectDialogOpen(false);
                                        setRejectTarget(null);
                                        setRejectReason('');
                                    }
                                }}
                                className="text-gray-400 hover:text-gray-600 transition-colors"
                                aria-label="Close"
                            >
                                <XCircle className="w-5 h-5" />
                            </button>
                        </div>
                        <div className="px-6 py-4">
                            <label className="block text-sm font-medium text-gray-700 mb-2">Rejection reason</label>
                            <textarea
                                value={rejectReason}
                                onChange={(e) => setRejectReason(e.target.value)}
                                rows={3}
                                className="w-full border border-gray-200 rounded-lg p-2 text-sm"
                                placeholder="Optional context for the submitter..."
                            />
                        </div>
                        <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex items-center justify-end gap-2">
                            <button
                                onClick={() => {
                                    if (!rejectSubmitting) {
                                        setRejectDialogOpen(false);
                                        setRejectTarget(null);
                                        setRejectReason('');
                                    }
                                }}
                                className="px-4 py-2 text-sm font-semibold text-gray-600 hover:bg-gray-100 rounded-lg"
                                disabled={rejectSubmitting}
                            >
                                Cancel
                            </button>
                            <button
                                onClick={submitRejectEntry}
                                className="px-4 py-2 text-sm font-semibold text-white bg-red-600 hover:bg-red-700 rounded-lg"
                                disabled={rejectSubmitting}
                            >
                                Reject Entry
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default TimeTracker;
