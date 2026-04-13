import React, { useEffect, useMemo, useState } from 'react';
import html2canvas from 'html2canvas';
import jsPDF from 'jspdf';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, PieChart, Pie, Cell } from 'recharts';
import { FileText, Download, Calendar, DollarSign, Clock, Users, BarChart3, TrendingUp } from './Icons';
import EntityOfficeFilter from './common/EntityOfficeFilter';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../services/api';
import { FirmEntity, FirmSettings, Office } from '../types';
import { toast } from './Toast';

// Helper function for status checks (handles both legacy strings and new enums)
const isPaid = (status: any) => status === 'Paid' || status === 'PAID';

const currencyFormatter = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0
});

const shortDateFormatter = new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric'
});

const longDateTimeFormatter = new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
});

const formatCurrency = (value?: number | null) => currencyFormatter.format(Number.isFinite(value) ? Number(value) : 0);

const formatDate = (value?: Date | string | null) => {
    if (!value) return 'N/A';
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) return 'N/A';
    return shortDateFormatter.format(parsed);
};

const formatDateTime = (value?: Date | string | null) => {
    if (!value) return 'N/A';
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) return 'N/A';
    return longDateTimeFormatter.format(parsed);
};

interface DateRange {
    start: Date;
    end: Date;
}

const Reports: React.FC = () => {
    const { t } = useTranslation();
    const { user } = useAuth();
    const { matters, clients, timeEntries, invoices, tasks, leads } = useData();

    const [activeReport, setActiveReport] = useState<'executive' | 'performance' | 'profitability' | 'matters' | 'billing' | 'kpis'>('executive');
    const [dateRange, setDateRange] = useState<DateRange>({
        start: new Date(new Date().setMonth(new Date().getMonth() - 3)),
        end: new Date()
    });
    const [isExporting, setIsExporting] = useState(false);
    const [entityFilter, setEntityFilter] = useState('');
    const [officeFilter, setOfficeFilter] = useState('');
    const [firmSettings, setFirmSettings] = useState<Partial<FirmSettings> | null>(null);
    const [entities, setEntities] = useState<FirmEntity[]>([]);
    const [offices, setOffices] = useState<Office[]>([]);

    useEffect(() => {
        let isMounted = true;

        const loadExportMetadata = async () => {
            try {
                const [firmData, entityData] = await Promise.all([
                    api.settings.getFirm().catch(() => null),
                    api.entities.list().catch(() => [])
                ]);

                if (!isMounted) return;

                setFirmSettings(firmData || null);
                setEntities(Array.isArray(entityData)
                    ? entityData.filter((entity): entity is FirmEntity => !!entity && typeof entity.id === 'string')
                    : []);
            } catch (error) {
                if (!isMounted) return;
                setFirmSettings(null);
                setEntities([]);
            }
        };

        loadExportMetadata();

        return () => {
            isMounted = false;
        };
    }, []);

    useEffect(() => {
        let isMounted = true;

        if (!entityFilter) {
            setOffices([]);
            return () => {
                isMounted = false;
            };
        }

        const loadOffices = async () => {
            try {
                const officeData = await api.entities.offices.list(entityFilter).catch(() => []);
                if (!isMounted) return;

                setOffices(Array.isArray(officeData)
                    ? officeData.filter((office): office is Office => !!office && typeof office.id === 'string')
                    : []);
            } catch {
                if (!isMounted) return;
                setOffices([]);
            }
        };

        loadOffices();

        return () => {
            isMounted = false;
        };
    }, [entityFilter]);

    const filteredMatters = useMemo(() => {
        return (matters || []).filter(matter => {
            if (entityFilter && matter.entityId !== entityFilter) return false;
            if (officeFilter && matter.officeId !== officeFilter) return false;
            return true;
        });
    }, [matters, entityFilter, officeFilter]);

    const matterMap = useMemo(() => {
        return new Map(filteredMatters.map(matter => [matter.id, matter]));
    }, [filteredMatters]);

    const filteredInvoices = useMemo(() => {
        return (invoices || []).filter(invoice => {
            if (entityFilter && invoice.entityId !== entityFilter) return false;
            if (officeFilter && invoice.officeId !== officeFilter) return false;
            return true;
        });
    }, [invoices, entityFilter, officeFilter]);

    const filteredTimeEntries = useMemo(() => {
        return (timeEntries || []).filter(entry => {
            if (!entityFilter && !officeFilter) return true;
            if (!entry.matterId) return false;
            return matterMap.has(entry.matterId);
        });
    }, [timeEntries, entityFilter, officeFilter, matterMap]);

    const filteredTasks = useMemo(() => {
        return (tasks || []).filter(task => {
            if (!entityFilter && !officeFilter) return true;
            if (!task.matterId) return false;
            return matterMap.has(task.matterId);
        });
    }, [tasks, entityFilter, officeFilter, matterMap]);

    // Calculate attorney performance data
    const performanceData = useMemo(() => {
        const attorneyStats: Record<string, { name: string; billableHours: number; revenue: number; matters: number; tasks: number }> = {};

        // Get unique attorneys from matters
        filteredMatters.forEach(matter => {
            if (matter.responsibleAttorney && !attorneyStats[matter.responsibleAttorney]) {
                attorneyStats[matter.responsibleAttorney] = {
                    name: matter.responsibleAttorney,
                    billableHours: 0,
                    revenue: 0,
                    matters: 0,
                    tasks: 0
                };
            }
        });

        // Count time entries
        filteredTimeEntries.forEach(entry => {
            const date = new Date(entry.date);
            if (date >= dateRange.start && date <= dateRange.end) {
                // For simplicity, attribute to first available attorney
                const firstAttorney = Object.keys(attorneyStats)[0];
                if (firstAttorney) {
                    attorneyStats[firstAttorney].billableHours += entry.duration / 60;
                    attorneyStats[firstAttorney].revenue += (entry.duration / 60) * entry.rate;
                }
            }
        });

        // Count matters by responsible attorney
        filteredMatters.forEach(matter => {
            const date = new Date(matter.openDate);
            if (date >= dateRange.start && date <= dateRange.end) {
                const attorney = Object.values(attorneyStats).find(a => a.name === matter.responsibleAttorney);
                if (attorney) {
                    attorney.matters += 1;
                }
            }
        });

        // Count completed tasks
        filteredTasks.forEach(task => {
            if (task.completedAt) {
                const date = new Date(task.completedAt);
                if (date >= dateRange.start && date <= dateRange.end && task.assignedTo) {
                    const attorney = Object.values(attorneyStats).find(a => a.name === task.assignedTo);
                    if (attorney) {
                        attorney.tasks += 1;
                    }
                }
            }
        });

        return Object.values(attorneyStats);
    }, [filteredTimeEntries, filteredMatters, filteredTasks, dateRange]);

    // Calculate client profitability
    const profitabilityData = useMemo(() => {
        const clientStats: Record<string, { name: string; revenue: number; hours: number; matters: number }> = {};

        clients?.forEach(client => {
            clientStats[client.id] = {
                name: client.name,
                revenue: 0,
                hours: 0,
                matters: 0
            };
        });

        // Count invoices
        filteredInvoices.forEach(invoice => {
            const clientId = invoice.client?.id;
            if (isPaid(invoice.status) && clientId && clientStats[clientId]) {
                clientStats[clientId].revenue += invoice.amount;
            }
        });

        // Count matters and hours per client
        filteredMatters.forEach(matter => {
            const clientId = matter.client?.id;
            if (clientId && clientStats[clientId]) {
                clientStats[clientId].matters += 1;

                // Sum time entries for this matter
                const matterEntries = filteredTimeEntries.filter(e => e.matterId === matter.id);
                clientStats[clientId].hours += matterEntries.reduce((sum, e) => sum + e.duration / 60, 0);
            }
        });

        return Object.values(clientStats)
            .filter(c => c.revenue > 0 || c.hours > 0)
            .sort((a, b) => b.revenue - a.revenue)
            .slice(0, 10);
    }, [clients, filteredInvoices, filteredMatters, filteredTimeEntries]);

    // Matter statistics by practice area
    const matterStats = useMemo(() => {
        const areaStats: Record<string, { area: string; open: number; closed: number; total: number }> = {};

        filteredMatters.forEach(matter => {
            if (!areaStats[matter.practiceArea]) {
                areaStats[matter.practiceArea] = { area: matter.practiceArea, open: 0, closed: 0, total: 0 };
            }
            areaStats[matter.practiceArea].total += 1;
            if (matter.status === 'Open') {
                areaStats[matter.practiceArea].open += 1;
            } else {
                areaStats[matter.practiceArea].closed += 1;
            }
        });

        return Object.values(areaStats);
    }, [filteredMatters]);

    // Billing trends
    const billingTrends = useMemo(() => {
        const months: Record<string, { month: string; billed: number; collected: number }> = {};

        // Last 6 months
        for (let i = 5; i >= 0; i--) {
            const date = new Date();
            date.setMonth(date.getMonth() - i);
            const key = date.toLocaleDateString('en-US', { month: 'short', year: '2-digit' });
            months[key] = { month: key, billed: 0, collected: 0 };
        }

        filteredInvoices.forEach(invoice => {
            const date = new Date(invoice.dueDate);
            const key = date.toLocaleDateString('en-US', { month: 'short', year: '2-digit' });
            if (months[key]) {
                months[key].billed += invoice.amount;
                if (isPaid(invoice.status)) {
                    months[key].collected += invoice.amount;
                }
            }
        });

        return Object.values(months);
    }, [filteredInvoices]);

    const COLORS = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899'];

    const activeEntity = useMemo(
        () => entities.find(entity => entity.id === entityFilter) || null,
        [entities, entityFilter]
    );

    const activeOffice = useMemo(
        () => offices.find(office => office.id === officeFilter) || null,
        [offices, officeFilter]
    );

    const reportTabs = [
        { id: 'executive', label: 'Executive KPIs', icon: TrendingUp },
        { id: 'performance', label: t('rep_attorney_perf'), icon: BarChart3 },
        { id: 'profitability', label: t('rep_top_clients'), icon: DollarSign },
        { id: 'matters', label: t('rep_matters'), icon: FileText },
        { id: 'billing', label: t('rep_billing_trends'), icon: Clock },
        { id: 'kpis', label: t('rep_kpi'), icon: Users }
    ];

    const activeReportConfig = reportTabs.find(tab => tab.id === activeReport) || reportTabs[0];

    // Export to CSV
    const exportToCSV = async (data: any[], filename: string) => {
        setIsExporting(true);
        try {
            if (!data || data.length === 0) {
                toast.error('No data available for export');
                return;
            }
            const headers = Object.keys(data[0] || {}).join(',');
            const rows = data.map(row => Object.values(row).join(','));
            const csv = [headers, ...rows].join('\n');

            const blob = new Blob([csv], { type: 'text/csv' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `${filename}_${new Date().toISOString().split('T')[0]}.csv`;
            a.click();
            URL.revokeObjectURL(url);

            toast.success('Report exported successfully');
        } catch (error) {
            toast.error('Failed to export report');
        } finally {
            setIsExporting(false);
        }
    };

    // Export to PDF (Client-side)
    const exportToPDF = async () => {
        setIsExporting(true);
        try {
            const element = document.getElementById('report-content');
            if (!element) throw new Error('Report content not found');

            const canvas = await html2canvas(element, {
                scale: 2,
                logging: false,
                useCORS: true,
                backgroundColor: '#ffffff'
            } as any);

            const imgData = canvas.toDataURL('image/png');
            const pdf = new jsPDF('p', 'pt', 'a4');
            const pageWidth = pdf.internal.pageSize.getWidth();
            const pageHeight = pdf.internal.pageSize.getHeight();
            const margin = 36;
            const contentWidth = pageWidth - (margin * 2);
            const coverMetrics = exportCoverMetrics.slice(0, 4);
            const metricRows = Math.max(1, Math.ceil(coverMetrics.length / 2));
            const metricsHeight = metricRows * 72;
            const reportImageWidth = contentWidth;
            const reportImageHeight = (canvas.height * reportImageWidth) / canvas.width;
            const topBandHeight = 88;
            const reportImageStartY = 262 + metricsHeight;

            const writeMuted = (text: string, x: number, y: number, align: 'left' | 'right' = 'left') => {
                pdf.setFont('helvetica', 'normal');
                pdf.setFontSize(10);
                pdf.setTextColor(100, 116, 139);
                pdf.text(text, x, y, { align });
            };

            const writeStrong = (text: string, x: number, y: number, size = 12, align: 'left' | 'right' = 'left') => {
                pdf.setFont('helvetica', 'bold');
                pdf.setFontSize(size);
                pdf.setTextColor(15, 23, 42);
                pdf.text(text, x, y, { align });
            };

            pdf.setFillColor(15, 23, 42);
            pdf.rect(0, 0, pageWidth, topBandHeight, 'F');
            pdf.setTextColor(255, 255, 255);
            pdf.setFont('helvetica', 'bold');
            pdf.setFontSize(22);
            pdf.text('JurisFlow', margin, 38);
            pdf.setFontSize(18);
            pdf.text(activeReportConfig.label, margin, 62);

            pdf.setFont('helvetica', 'normal');
            pdf.setFontSize(10);
            pdf.text('Report export', pageWidth - margin, 30, { align: 'right' });
            pdf.text(formatDateTime(new Date()), pageWidth - margin, 48, { align: 'right' });

            const firmName = activeEntity?.name || firmSettings?.firmName || 'JurisFlow Legal System';
            const scopeLabel = [
                activeEntity?.name || 'All entities',
                activeOffice?.name || (entityFilter ? 'All offices' : 'Firm-wide')
            ].join(' / ');
            const preparedFor = user?.name || user?.email || 'Current user';
            const coverageText = [
                `Window: ${formatDate(dateRange.start)} - ${formatDate(dateRange.end)}`,
                `Scope: ${scopeLabel}`,
                `Prepared for: ${preparedFor}`
            ];
            const firmAddress = [firmSettings?.address, firmSettings?.city, firmSettings?.state, firmSettings?.zipCode]
                .filter(Boolean)
                .join(', ');
            const metadataLines = [
                firmName,
                firmAddress,
                firmSettings?.website || '',
                firmSettings?.phone ? `Phone: ${firmSettings.phone}` : '',
                exportDataSnapshot
            ].filter(Boolean);

            let y = topBandHeight + 32;
            writeStrong('Report Context', margin, y, 13);
            y += 18;
            metadataLines.forEach(line => {
                writeMuted(line, margin, y);
                y += 14;
            });

            let metaRightY = topBandHeight + 50;
            coverageText.forEach(line => {
                writeMuted(line, pageWidth - margin, metaRightY, 'right');
                metaRightY += 14;
            });

            const metricCardWidth = (contentWidth - 14) / 2;
            const metricsStartY = 174;
            coverMetrics.forEach((metric, index) => {
                const col = index % 2;
                const row = Math.floor(index / 2);
                const x = margin + (col * (metricCardWidth + 14));
                const cardY = metricsStartY + (row * 72);

                pdf.setFillColor(248, 250, 252);
                pdf.setDrawColor(226, 232, 240);
                pdf.roundedRect(x, cardY, metricCardWidth, 58, 10, 10, 'FD');
                writeMuted(metric.label, x + 12, cardY + 17);
                writeStrong(metric.value, x + 12, cardY + 37, 16);
                if (metric.detail) {
                    writeMuted(metric.detail, x + 12, cardY + 51);
                }
            });

            pdf.setDrawColor(226, 232, 240);
            pdf.line(margin, reportImageStartY - 18, pageWidth - margin, reportImageStartY - 18);

            pdf.addImage(imgData, 'PNG', margin, reportImageStartY, reportImageWidth, reportImageHeight);

            let remainingHeight = reportImageHeight - (pageHeight - reportImageStartY - margin);
            let currentOffset = reportImageStartY - reportImageHeight + (pageHeight - reportImageStartY - margin);

            while (remainingHeight > 0) {
                pdf.addPage();
                pdf.addImage(imgData, 'PNG', margin, currentOffset, reportImageWidth, reportImageHeight);
                remainingHeight -= (pageHeight - (margin * 2));
                currentOffset -= (pageHeight - (margin * 2));
            }

            pdf.save(`report-${activeReport}-${new Date().toISOString().split('T')[0]}.pdf`);
            toast.success(t('entry_saved') || 'Report exported successfully');
        } catch (error) {
            console.error('Error exporting PDF:', error);
            toast.error(t('error_login') || 'Failed to export PDF');
        } finally {
            setIsExporting(false);
        }
    };

    // Advanced KPIs calculation
    const kpiData = useMemo(() => {
        const now = new Date();

        // Total available hours (standard 8h/day, 20 days/month for 3 months = 480 hours per attorney)
        const attorneyCount = Object.keys(performanceData).length || 1;
        const totalAvailableHours = attorneyCount * 480;

        // Total billable hours worked
        const totalBillableHours = filteredTimeEntries.reduce((sum, t) => sum + (t.duration / 60), 0);

        // Total hours billed to clients (from invoices)
        const totalBilledAmount = filteredInvoices.reduce((sum, inv) => sum + inv.amount, 0);
        const totalCollected = filteredInvoices.filter(i => i.status === 'PAID').reduce((sum, inv) => sum + inv.amount, 0);

        // Worked value (hours * rate)
        const totalWorkedValue = filteredTimeEntries.reduce((sum, t) => sum + ((t.duration / 60) * t.rate), 0);

        // Utilization Rate = Billable Hours / Available Hours
        const utilizationRate = totalAvailableHours > 0 ? (totalBillableHours / totalAvailableHours) * 100 : 0;

        // Realization Rate = Billed / Worked Value
        const realizationRate = totalWorkedValue > 0 ? (totalBilledAmount / totalWorkedValue) * 100 : 0;

        // Collection Rate = Collected / Billed
        const collectionRate = totalBilledAmount > 0 ? (totalCollected / totalBilledAmount) * 100 : 0;

        // A/R Aging buckets
        const aging = { current: 0, days30: 0, days60: 0, days90: 0, over90: 0 };

        filteredInvoices.forEach(inv => {
            if (inv.status === 'PAID') return;

            const dueDate = new Date(inv.dueDate);
            const daysOverdue = Math.floor((now.getTime() - dueDate.getTime()) / (1000 * 60 * 60 * 24));

            if (daysOverdue <= 0) {
                aging.current += inv.amount;
            } else if (daysOverdue <= 30) {
                aging.days30 += inv.amount;
            } else if (daysOverdue <= 60) {
                aging.days60 += inv.amount;
            } else if (daysOverdue <= 90) {
                aging.days90 += inv.amount;
            } else {
                aging.over90 += inv.amount;
            }
        });

        // WIP (Work In Progress) - unbilled time & expenses
        const wip = filteredTimeEntries.filter(t => !t.billed).reduce((sum, t) => sum + ((t.duration / 60) * t.rate), 0);

        return {
            utilizationRate,
            realizationRate,
            collectionRate,
            totalBillableHours,
            totalBilledAmount,
            totalCollected,
            wip,
            aging,
            agingData: [
                { name: 'Current', value: aging.current, color: '#10b981' },
                { name: '1-30 Days', value: aging.days30, color: '#f59e0b' },
                { name: '31-60 Days', value: aging.days60, color: '#f97316' },
                { name: '61-90 Days', value: aging.days90, color: '#ef4444' },
                { name: '90+ Days', value: aging.over90, color: '#dc2626' }
            ]
        };
    }, [filteredTimeEntries, filteredInvoices, performanceData]);

    const leadFunnelData = useMemo(() => {
        if (!leads || leads.length === 0) {
            return [];
        }
        const stages = ['New', 'Contacted', 'Consultation', 'Retained', 'Lost'];
        return stages.map(stage => ({
            stage,
            count: (leads || []).filter(lead => lead.status === stage).length
        }));
    }, [leads]);

    const matterAgingData = useMemo(() => {
        const buckets = [
            { label: '0-30 Days', min: 0, max: 30, color: '#10b981' },
            { label: '31-90 Days', min: 31, max: 90, color: '#3b82f6' },
            { label: '91-180 Days', min: 91, max: 180, color: '#f59e0b' },
            { label: '181+ Days', min: 181, max: Number.POSITIVE_INFINITY, color: '#ef4444' }
        ];

        const counts = buckets.map(bucket => ({ bucket: bucket.label, count: 0, color: bucket.color }));
        const now = Date.now();

        const openMatters = filteredMatters.filter(m => m.status === 'Open');
        if (openMatters.length === 0) {
            return [];
        }

        openMatters.forEach(matter => {
            const openedAt = new Date(matter.openDate).getTime();
            if (Number.isNaN(openedAt)) return;
            const daysOpen = Math.floor((now - openedAt) / (1000 * 60 * 60 * 24));
            const target = buckets.findIndex(b => daysOpen >= b.min && daysOpen <= b.max);
            if (target >= 0) {
                counts[target].count += 1;
            }
        });

        return counts;
    }, [filteredMatters]);

    const practiceAreaRevenue = useMemo(() => {
        const revenueMap: Record<string, number> = {};
        filteredInvoices.forEach(invoice => {
            const matter = filteredMatters.find(m => m.id === invoice.matterId);
            const key = matter?.practiceArea || 'Unassigned';
            revenueMap[key] = (revenueMap[key] || 0) + invoice.amount;
        });
        return Object.entries(revenueMap)
            .map(([area, revenue]) => ({ area, revenue }))
            .sort((a, b) => b.revenue - a.revenue);
    }, [filteredInvoices, filteredMatters]);

    const topTimekeepers = useMemo(() => {
        return [...performanceData]
            .sort((a, b) => b.billableHours - a.billableHours)
            .slice(0, 5);
    }, [performanceData]);

    const executiveStats = useMemo(() => {
        const openMatters = filteredMatters.filter(m => m.status === 'Open');
        const matterClientIds = new Set(
            filteredMatters
                .map(m => m.client?.id)
                .filter((id): id is string => Boolean(id))
        );
        const activeClients = (clients || []).filter(c => c.status === 'Active' && matterClientIds.has(c.id));
        const totalClients = matterClientIds.size;
        const totalLeads = leads?.length || 0;
        const retainedLeads = leads?.filter(l => l.status === 'Retained').length || 0;
        const leadConversionRate = totalLeads > 0 ? (retainedLeads / totalLeads) * 100 : 0;

        const now = Date.now();
        const avgMatterAgeDays = openMatters.length > 0
            ? openMatters.reduce((sum, matter) => {
                const openedAt = new Date(matter.openDate).getTime();
                if (Number.isNaN(openedAt)) return sum;
                return sum + Math.max(0, (now - openedAt) / (1000 * 60 * 60 * 24));
            }, 0) / openMatters.length
            : 0;

        const paidInvoices = filteredInvoices.filter(i => isPaid(i.status));
        const daysToPay = paidInvoices
            .map(inv => {
                const issueDate = new Date(inv.issueDate).getTime();
                const paidDateRaw = inv.paidDate || inv.updatedAt || inv.sentAt || inv.issueDate;
                const paidDate = new Date(paidDateRaw).getTime();
                if (Number.isNaN(issueDate) || Number.isNaN(paidDate)) return null;
                return Math.max(0, (paidDate - issueDate) / (1000 * 60 * 60 * 24));
            })
            .filter((value): value is number => value !== null);

        const avgDaysToPay = daysToPay.length > 0
            ? daysToPay.reduce((sum, value) => sum + value, 0) / daysToPay.length
            : 0;

        return {
            openMatters: openMatters.length,
            activeClients: activeClients.length,
            totalClients,
            totalLeads,
            leadConversionRate,
            avgMatterAgeDays,
            avgDaysToPay
        };
    }, [filteredMatters, filteredInvoices, clients, leads]);

    const exportDataSnapshot = useMemo(() => {
        return [
            `${filteredMatters.length} matters`,
            `${filteredInvoices.length} invoices`,
            `${filteredTimeEntries.length} time entries`,
            `${filteredTasks.length} tasks`
        ].join(' • ');
    }, [filteredMatters.length, filteredInvoices.length, filteredTimeEntries.length, filteredTasks.length]);

    const exportCoverMetrics = useMemo(() => {
        if (activeReport === 'executive') {
            return [
                { label: 'Open matters', value: String(executiveStats.openMatters), detail: `${executiveStats.activeClients} active clients` },
                { label: 'Lead conversion', value: `${executiveStats.leadConversionRate.toFixed(1)}%`, detail: `${executiveStats.totalLeads} leads tracked` },
                { label: 'Average days to pay', value: `${executiveStats.avgDaysToPay.toFixed(0)} days`, detail: 'Paid invoices only' },
                { label: 'Average matter age', value: `${executiveStats.avgMatterAgeDays.toFixed(0)} days`, detail: 'Open matters only' }
            ];
        }

        if (activeReport === 'performance') {
            const topAttorney = [...performanceData].sort((a, b) => b.billableHours - a.billableHours)[0];
            const completedTasks = performanceData.reduce((sum, item) => sum + item.tasks, 0);
            return [
                { label: 'Top attorney', value: topAttorney?.name || 'No data', detail: topAttorney ? `${topAttorney.billableHours.toFixed(1)}h logged` : 'No billable activity yet' },
                { label: 'Billable hours', value: `${performanceData.reduce((sum, item) => sum + item.billableHours, 0).toFixed(1)}h`, detail: `${performanceData.length} timekeepers represented` },
                { label: 'Matters handled', value: String(performanceData.reduce((sum, item) => sum + item.matters, 0)), detail: 'Within selected window' },
                { label: 'Tasks completed', value: String(completedTasks), detail: 'Completed by assigned attorneys' }
            ];
        }

        if (activeReport === 'profitability') {
            const topClient = profitabilityData[0];
            return [
                { label: 'Top client', value: topClient?.name || 'No data', detail: topClient ? `${formatCurrency(topClient.revenue)} revenue` : 'No paid invoices yet' },
                { label: 'Tracked client revenue', value: formatCurrency(profitabilityData.reduce((sum, item) => sum + item.revenue, 0)), detail: `${profitabilityData.length} clients in ranking` },
                { label: 'Tracked client hours', value: `${profitabilityData.reduce((sum, item) => sum + item.hours, 0).toFixed(1)}h`, detail: 'Across linked matters' },
                { label: 'Client matters', value: String(profitabilityData.reduce((sum, item) => sum + item.matters, 0)), detail: 'Revenue-bearing client matters' }
            ];
        }

        if (activeReport === 'matters') {
            const topArea = [...matterStats].sort((a, b) => b.total - a.total)[0];
            const totalClosed = matterStats.reduce((sum, item) => sum + item.closed, 0);
            const totalOpen = matterStats.reduce((sum, item) => sum + item.open, 0);
            return [
                { label: 'Total matters', value: String(filteredMatters.length), detail: `${matterStats.length} practice areas` },
                { label: 'Open matters', value: String(totalOpen), detail: 'Currently active' },
                { label: 'Closed matters', value: String(totalClosed), detail: 'Closed or archived' },
                { label: 'Top practice area', value: topArea?.area || 'No data', detail: topArea ? `${topArea.total} total matters` : 'No practice data yet' }
            ];
        }

        if (activeReport === 'billing') {
            const totalBilled = billingTrends.reduce((sum, item) => sum + item.billed, 0);
            const totalCollected = billingTrends.reduce((sum, item) => sum + item.collected, 0);
            const openInvoiceCount = filteredInvoices.filter(invoice => !isPaid(invoice.status)).length;
            return [
                { label: 'Total billed', value: formatCurrency(totalBilled), detail: 'Across billing trend window' },
                { label: 'Total collected', value: formatCurrency(totalCollected), detail: 'Paid invoice volume' },
                { label: 'Collection rate', value: `${totalBilled > 0 ? ((totalCollected / totalBilled) * 100).toFixed(1) : '0.0'}%`, detail: 'Collected / billed' },
                { label: 'Open invoices', value: String(openInvoiceCount), detail: 'Still pending collection' }
            ];
        }

        return [
            { label: 'Utilization', value: `${kpiData.utilizationRate.toFixed(1)}%`, detail: `${kpiData.totalBillableHours.toFixed(1)} billable hours` },
            { label: 'Realization', value: `${kpiData.realizationRate.toFixed(1)}%`, detail: `${formatCurrency(kpiData.totalBilledAmount)} billed` },
            { label: 'Collection', value: `${kpiData.collectionRate.toFixed(1)}%`, detail: `${formatCurrency(kpiData.totalCollected)} collected` },
            { label: 'Work in progress', value: formatCurrency(kpiData.wip), detail: 'Unbilled time value' }
        ];
    }, [
        activeReport,
        executiveStats,
        performanceData,
        profitabilityData,
        matterStats,
        filteredMatters.length,
        billingTrends,
        filteredInvoices,
        kpiData
    ]);

    return (
        <div className="p-6 space-y-6 h-full overflow-y-auto">
            {/* Header */}
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-slate-800">Reports & Analytics</h1>
                    <p className="text-gray-500 text-sm mt-1">Analyze firm performance and trends</p>
                </div>

                <div className="flex flex-wrap items-center gap-3">
                    <EntityOfficeFilter
                        entityId={entityFilter}
                        officeId={officeFilter}
                        onEntityChange={setEntityFilter}
                        onOfficeChange={setOfficeFilter}
                        allowAll
                    />
                    {/* Date Range Picker */}
                    <div className="flex items-center gap-2 bg-white border border-gray-200 rounded-lg px-3 py-2">
                        <Calendar className="w-4 h-4 text-gray-400" />
                        <input
                            type="date"
                            value={dateRange.start.toISOString().split('T')[0]}
                            onChange={(e) => setDateRange(prev => ({ ...prev, start: new Date(e.target.value) }))}
                            className="border-none text-sm focus:ring-0 bg-transparent"
                        />
                        <span className="text-gray-400">to</span>
                        <input
                            type="date"
                            value={dateRange.end.toISOString().split('T')[0]}
                            onChange={(e) => setDateRange(prev => ({ ...prev, end: new Date(e.target.value) }))}
                            className="border-none text-sm focus:ring-0 bg-transparent"
                        />
                    </div>

                    {/* Export Buttons */}
                    <button
                        onClick={() => {
                            const data = activeReport === 'executive' ? leadFunnelData :
                                activeReport === 'performance' ? performanceData :
                                    activeReport === 'profitability' ? profitabilityData :
                                        activeReport === 'matters' ? matterStats :
                                            activeReport === 'kpis' ? kpiData.agingData : billingTrends;
                            exportToCSV(data, activeReport);
                        }}
                        disabled={isExporting}
                        className="flex items-center gap-2 px-4 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium hover:bg-gray-50 transition-colors"
                    >
                        <Download className="w-4 h-4" />
                        {t('export_csv')}
                    </button>
                    <button
                        onClick={exportToPDF}
                        disabled={isExporting}
                        className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
                    >
                        <FileText className="w-4 h-4" />
                        {t('export_pdf')}
                    </button>
                </div>
            </div>

            {(entityFilter || officeFilter) && (
                <div className="text-xs text-gray-500">
                    Lead metrics remain firm-wide until leads are assigned to an entity or office.
                </div>
            )}

            {/* Report Type Tabs */}
            <div className="flex gap-2 border-b border-gray-200 pb-4">
                {reportTabs.map(tab => {
                    const Icon = tab.icon;
                    return (
                        <button
                            key={tab.id}
                            onClick={() => setActiveReport(tab.id as any)}
                            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-all ${activeReport === tab.id
                                ? 'bg-blue-50 text-blue-700 ring-1 ring-blue-200'
                                : 'text-gray-600 hover:bg-gray-50'
                                }`}
                        >
                            <Icon className="w-4 h-4" />
                            {tab.label}
                        </button>
                    );
                })}
            </div>

            {/* Report Content */}
            <div id="report-content" className="bg-white rounded-xl border border-gray-200 p-6 shadow-sm">
                {activeReport === 'executive' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">Executive KPIs</h3>

                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                            <div className="bg-slate-50 border border-slate-100 rounded-lg p-4">
                                <p className="text-xs text-gray-500 uppercase">Active Matters</p>
                                <p className="text-2xl font-bold text-slate-800">{executiveStats.openMatters}</p>
                                <p className="text-xs text-gray-500 mt-1">{executiveStats.activeClients} active clients</p>
                            </div>
                            <div className="bg-slate-50 border border-slate-100 rounded-lg p-4">
                                <p className="text-xs text-gray-500 uppercase">Lead Conversion</p>
                                <p className="text-2xl font-bold text-slate-800">{executiveStats.leadConversionRate.toFixed(1)}%</p>
                                <p className="text-xs text-gray-500 mt-1">{executiveStats.totalLeads} total leads</p>
                            </div>
                            <div className="bg-slate-50 border border-slate-100 rounded-lg p-4">
                                <p className="text-xs text-gray-500 uppercase">Avg Days to Pay</p>
                                <p className="text-2xl font-bold text-slate-800">{executiveStats.avgDaysToPay.toFixed(0)} days</p>
                                <p className="text-xs text-gray-500 mt-1">Paid invoices only</p>
                            </div>
                            <div className="bg-slate-50 border border-slate-100 rounded-lg p-4">
                                <p className="text-xs text-gray-500 uppercase">Avg Matter Age</p>
                                <p className="text-2xl font-bold text-slate-800">{executiveStats.avgMatterAgeDays.toFixed(0)} days</p>
                                <p className="text-xs text-gray-500 mt-1">Open matters</p>
                            </div>
                        </div>

                        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                            <div className="bg-gray-50 rounded-lg p-4">
                                <h4 className="font-semibold text-slate-800 mb-4">Lead Funnel</h4>
                                {leadFunnelData.length === 0 ? (
                                    <p className="text-sm text-gray-500">No lead activity yet.</p>
                                ) : (
                                    <div className="h-72">
                                        <ResponsiveContainer width="100%" height="100%">
                                            <BarChart data={leadFunnelData}>
                                                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                                <XAxis dataKey="stage" tick={{ fontSize: 11 }} />
                                                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                                                <Tooltip />
                                                <Bar dataKey="count" name="Leads" radius={[4, 4, 0, 0]}>
                                                    {leadFunnelData.map((entry, index) => (
                                                        <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                                                    ))}
                                                </Bar>
                                            </BarChart>
                                        </ResponsiveContainer>
                                    </div>
                                )}
                            </div>
                            <div className="bg-gray-50 rounded-lg p-4">
                                <h4 className="font-semibold text-slate-800 mb-4">Revenue by Practice Area</h4>
                                {practiceAreaRevenue.length === 0 ? (
                                    <p className="text-sm text-gray-500">No invoice revenue recorded.</p>
                                ) : (
                                    <div className="h-72">
                                        <ResponsiveContainer width="100%" height="100%">
                                            <PieChart>
                                                <Pie
                                                    data={practiceAreaRevenue}
                                                    dataKey="revenue"
                                                    nameKey="area"
                                                    innerRadius={50}
                                                    outerRadius={90}
                                                >
                                                    {practiceAreaRevenue.map((entry, index) => (
                                                        <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                                                    ))}
                                                </Pie>
                                                <Tooltip formatter={(value: any) => `$${value.toLocaleString()}`} />
                                            </PieChart>
                                        </ResponsiveContainer>
                                    </div>
                                )}
                            </div>
                        </div>

                        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                            <div className="bg-gray-50 rounded-lg p-4">
                                <h4 className="font-semibold text-slate-800 mb-4">Open Matter Aging</h4>
                                {matterAgingData.length === 0 ? (
                                    <p className="text-sm text-gray-500">No open matters yet.</p>
                                ) : (
                                    <div className="h-64">
                                        <ResponsiveContainer width="100%" height="100%">
                                            <BarChart data={matterAgingData}>
                                                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                                <XAxis dataKey="bucket" tick={{ fontSize: 11 }} />
                                                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                                                <Tooltip />
                                                <Bar dataKey="count" name="Matters" radius={[4, 4, 0, 0]}>
                                                    {matterAgingData.map((entry, index) => (
                                                        <Cell key={`cell-${index}`} fill={entry.color} />
                                                    ))}
                                                </Bar>
                                            </BarChart>
                                        </ResponsiveContainer>
                                    </div>
                                )}
                            </div>
                            <div className="bg-gray-50 rounded-lg p-4">
                                <h4 className="font-semibold text-slate-800 mb-4">Top Timekeepers</h4>
                                {topTimekeepers.length === 0 ? (
                                    <p className="text-sm text-gray-500">No time activity recorded.</p>
                                ) : (
                                    <div className="space-y-3">
                                        {topTimekeepers.map((attorney, index) => (
                                            <div key={`${attorney.name}-${index}`} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-3">
                                                <div>
                                                    <p className="text-sm font-semibold text-slate-800">{attorney.name}</p>
                                                    <p className="text-xs text-gray-500">{attorney.matters} matters · {attorney.tasks} tasks</p>
                                                </div>
                                                <div className="text-right">
                                                    <p className="text-sm font-semibold text-slate-800">{attorney.billableHours.toFixed(1)}h</p>
                                                    <p className="text-xs text-emerald-600">${attorney.revenue.toLocaleString()}</p>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>
                    </div>
                )}

                {activeReport === 'performance' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">{t('rep_attorney_perf')}</h3>
                        <div className="h-80">
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart data={performanceData}>
                                    <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                    <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                                    <YAxis tick={{ fontSize: 12 }} />
                                    <Tooltip />
                                    <Legend />
                                    <Bar dataKey="billableHours" name={t('rep_billable_hours')} fill="#3b82f6" radius={[4, 4, 0, 0]} />
                                    <Bar dataKey="matters" name={t('rep_matters')} fill="#10b981" radius={[4, 4, 0, 0]} />
                                    <Bar dataKey="tasks" name={t('rep_tasks_completed')} fill="#f59e0b" radius={[4, 4, 0, 0]} />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>

                        {/* Stats Cards */}
                        <div className="grid grid-cols-4 gap-4">
                            {performanceData.slice(0, 4).map((attorney, i) => (
                                <div key={i} className="bg-gray-50 rounded-lg p-4">
                                    <p className="text-sm text-gray-500">{attorney.name}</p>
                                    <p className="text-2xl font-bold text-slate-800">{attorney.billableHours.toFixed(1)}h</p>
                                    <p className="text-xs text-green-600">${attorney.revenue.toLocaleString()} revenue</p>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {activeReport === 'profitability' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">{t('rep_top_clients')}</h3>
                        <div className="h-80">
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart data={profitabilityData} layout="vertical">
                                    <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                    <XAxis type="number" tick={{ fontSize: 12 }} />
                                    <YAxis dataKey="name" type="category" tick={{ fontSize: 11 }} width={120} />
                                    <Tooltip />
                                    <Legend />
                                    <Bar dataKey="revenue" name={t('rep_revenue')} fill="#10b981" radius={[0, 4, 4, 0]} />
                                    <Bar dataKey="hours" name={t('rep_hours')} fill="#3b82f6" radius={[0, 4, 4, 0]} />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>
                    </div>
                )}

                {activeReport === 'matters' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">{t('rep_matters_by_area')}</h3>
                        <div className="grid grid-cols-2 gap-6">
                            <div className="h-80">
                                <ResponsiveContainer width="100%" height="100%">
                                    <PieChart>
                                        <Pie
                                            data={matterStats}
                                            dataKey="total"
                                            nameKey="area"
                                            cx="50%"
                                            cy="50%"
                                            outerRadius={100}
                                            label={({ name, percent }: any) => `${name} (${(percent * 100).toFixed(0)}%)`}
                                        >
                                            {matterStats.map((_, index) => (
                                                <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                                            ))}
                                        </Pie>
                                        <Tooltip />
                                    </PieChart>
                                </ResponsiveContainer>
                            </div>
                            <div className="space-y-3">
                                {matterStats.map((stat, i) => (
                                    <div key={i} className="flex items-center justify-between p-3 bg-gray-50 rounded-lg">
                                        <div className="flex items-center gap-3">
                                            <div className="w-3 h-3 rounded-full" style={{ backgroundColor: COLORS[i % COLORS.length] }} />
                                            <span className="font-medium text-slate-700">{stat.area}</span>
                                        </div>
                                        <div className="flex items-center gap-4 text-sm">
                                            <span className="text-green-600">{stat.open} open</span>
                                            <span className="text-gray-500">{stat.closed} closed</span>
                                            <span className="font-bold text-slate-800">{stat.total} total</span>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>
                )}

                {activeReport === 'billing' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">{t('rep_billing_trends')}</h3>
                        <div className="h-80">
                            <ResponsiveContainer width="100%" height="100%">
                                <BarChart data={billingTrends}>
                                    <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                    <XAxis dataKey="month" tick={{ fontSize: 12 }} />
                                    <YAxis tick={{ fontSize: 12 }} tickFormatter={(v) => `$${(v / 1000).toFixed(0)}k`} />
                                    <Tooltip formatter={(value: any) => `$${value.toLocaleString()}`} />
                                    <Legend />
                                    <Bar dataKey="billed" name={t('rep_billed')} fill="#3b82f6" radius={[4, 4, 0, 0]} />
                                    <Bar dataKey="collected" name={t('rep_collected')} fill="#10b981" radius={[4, 4, 0, 0]} />
                                </BarChart>
                            </ResponsiveContainer>
                        </div>

                        {/* Summary Stats */}
                        <div className="grid grid-cols-3 gap-4">
                            <div className="bg-blue-50 rounded-lg p-4 border border-blue-100">
                                <p className="text-sm text-blue-600 font-medium">{t('rep_total_billed')}</p>
                                <p className="text-2xl font-bold text-blue-700">
                                    ${billingTrends.reduce((sum, m) => sum + m.billed, 0).toLocaleString()}
                                </p>
                            </div>
                            <div className="bg-green-50 rounded-lg p-4 border border-green-100">
                                <p className="text-sm text-green-600 font-medium">{t('rep_total_collected')}</p>
                                <p className="text-2xl font-bold text-green-700">
                                    ${billingTrends.reduce((sum, m) => sum + m.collected, 0).toLocaleString()}
                                </p>
                            </div>
                            <div className="bg-amber-50 rounded-lg p-4 border border-amber-100">
                                <p className="text-sm text-amber-600 font-medium">{t('rep_collection_rate')}</p>
                                <p className="text-2xl font-bold text-amber-700">
                                    {(() => {
                                        const billed = billingTrends.reduce((sum, m) => sum + m.billed, 0);
                                        const collected = billingTrends.reduce((sum, m) => sum + m.collected, 0);
                                        return billed > 0 ? ((collected / billed) * 100).toFixed(1) : 0;
                                    })()}%
                                </p>
                            </div>
                        </div>
                    </div>
                )}

                {activeReport === 'kpis' && (
                    <div className="space-y-6">
                        <h3 className="text-lg font-bold text-slate-800">{t('rep_kpi')}</h3>

                        {/* Rate Cards */}
                        <div className="grid grid-cols-4 gap-4">
                            <div className="bg-gradient-to-br from-blue-500 to-blue-600 rounded-xl p-5 text-white">
                                <p className="text-sm text-blue-100 font-medium">{t('rep_utilization')}</p>
                                <p className="text-3xl font-bold mt-2">{kpiData.utilizationRate.toFixed(1)}%</p>
                                <p className="text-xs text-blue-200 mt-1">{t('rep_util_desc')}</p>
                                <div className="mt-3 bg-blue-400/30 rounded-full h-2">
                                    <div
                                        className="bg-white rounded-full h-2 transition-all"
                                        style={{ width: `${Math.min(kpiData.utilizationRate, 100)}%` }}
                                    />
                                </div>
                            </div>

                            <div className="bg-gradient-to-br from-purple-500 to-purple-600 rounded-xl p-5 text-white">
                                <p className="text-sm text-purple-100 font-medium">{t('rep_realization')}</p>
                                <p className="text-3xl font-bold mt-2">{kpiData.realizationRate.toFixed(1)}%</p>
                                <p className="text-xs text-purple-200 mt-1">{t('rep_real_desc')}</p>
                                <div className="mt-3 bg-purple-400/30 rounded-full h-2">
                                    <div
                                        className="bg-white rounded-full h-2 transition-all"
                                        style={{ width: `${Math.min(kpiData.realizationRate, 100)}%` }}
                                    />
                                </div>
                            </div>

                            <div className="bg-gradient-to-br from-emerald-500 to-emerald-600 rounded-xl p-5 text-white">
                                <p className="text-sm text-emerald-100 font-medium">{t('rep_collection_kpi')}</p>
                                <p className="text-3xl font-bold mt-2">{kpiData.collectionRate.toFixed(1)}%</p>
                                <p className="text-xs text-emerald-200 mt-1">{t('rep_coll_desc')}</p>
                                <div className="mt-3 bg-emerald-400/30 rounded-full h-2">
                                    <div
                                        className="bg-white rounded-full h-2 transition-all"
                                        style={{ width: `${Math.min(kpiData.collectionRate, 100)}%` }}
                                    />
                                </div>
                            </div>

                            <div className="bg-gradient-to-br from-amber-500 to-amber-600 rounded-xl p-5 text-white">
                                <p className="text-sm text-amber-100 font-medium">{t('rep_wip')}</p>
                                <p className="text-3xl font-bold mt-2">${kpiData.wip.toLocaleString()}</p>
                                <p className="text-xs text-amber-200 mt-1">{t('rep_wip_desc')}</p>
                            </div>
                        </div>

                        {/* A/R Aging Chart */}
                        <div className="grid grid-cols-2 gap-6">
                            <div>
                                <h4 className="font-bold text-gray-700 mb-4">{t('rep_ar_aging')}</h4>
                                <div className="h-64">
                                    <ResponsiveContainer width="100%" height="100%">
                                        <BarChart data={kpiData.agingData} layout="vertical">
                                            <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                                            <XAxis type="number" tickFormatter={(v) => `$${(v / 1000).toFixed(0)}k`} />
                                            <YAxis dataKey="name" type="category" width={80} />
                                            <Tooltip formatter={(value: any) => `$${value.toLocaleString()}`} />
                                            <Bar dataKey="value" radius={[0, 4, 4, 0]}>
                                                {kpiData.agingData.map((entry, index) => (
                                                    <Cell key={`cell-${index}`} fill={entry.color} />
                                                ))}
                                            </Bar>
                                        </BarChart>
                                    </ResponsiveContainer>
                                </div>
                            </div>

                            <div>
                                <h4 className="font-bold text-gray-700 mb-4">{t('rep_aging_breakdown')}</h4>
                                <div className="space-y-3">
                                    {kpiData.agingData.map((item, i) => (
                                        <div key={i} className="flex items-center justify-between p-3 bg-gray-50 rounded-lg">
                                            <div className="flex items-center gap-3">
                                                <div className="w-3 h-3 rounded-full" style={{ backgroundColor: item.color }} />
                                                <span className="font-medium text-gray-700">{item.name}</span>
                                            </div>
                                            <span className="font-bold text-gray-900">${item.value.toLocaleString()}</span>
                                        </div>
                                    ))}
                                    <div className="flex items-center justify-between p-3 bg-slate-800 rounded-lg text-white">
                                        <span className="font-bold">{t('rep_total_outstanding')}</span>
                                        <span className="font-bold text-lg">
                                            ${Object.values(kpiData.aging).reduce((a, b) => a + b, 0).toLocaleString()}
                                        </span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default Reports;
