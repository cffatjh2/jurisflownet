import React, { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle,
  Copy,
  Download,
  Edit,
  ExternalLink,
  FileText,
  Filter,
  Plus,
  RefreshCw,
  Search,
  Shield,
  X
} from './Icons';
import { api } from '../services/api';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { useAuth } from '../contexts/AuthContext';
import { CaseStatus, FeeStructure, PracticeArea } from '../types';
import IntakeFormBuilder from './IntakeFormBuilder';
import { toast } from './Toast';
import { downloadIntakeFormPdf, type IntakePdfField } from '../utils/intakeFormPdf';

interface IntakeFormRecord {
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
  updatedAt?: string;
  thankYouMessage?: string;
  redirectUrl?: string;
  notifyEmail?: string;
}

interface IntakeSubmissionRecord {
  id: string;
  intakeFormId: string;
  dataJson: string;
  status: string;
  reviewNotes?: string;
  reviewedBy?: string;
  reviewedAt?: string;
  leadId?: string;
  clientId?: string;
  createdAt: string;
}

interface ConflictResult {
  id: string;
  matchedEntityType: string;
  matchedEntityId: string;
  matchedEntityName: string;
  matchType: string;
  matchScore: number;
  riskLevel: string;
  relatedMatterId?: string;
  relatedMatterName?: string;
}

interface ConflictCheckResult {
  id: string;
  status: string;
  matchCount: number;
  results: ConflictResult[];
  waiverReason?: string;
}

interface MatterDraft {
  clientMode: 'existing' | 'new';
  clientId: string;
  clientName: string;
  clientEmail: string;
  clientPhone: string;
  clientType: 'Individual' | 'Corporate';
  matterName: string;
  caseNumber: string;
  practiceArea: string;
  feeStructure: FeeStructure;
  responsibleAttorney: string;
  billableRate: string;
  trustBalance: string;
}

const Intake: React.FC = () => {
  const { formatDate } = useTranslation();
  const { clients, matters, addClient, addMatter } = useData();
  const { user } = useAuth();

  const [view, setView] = useState<'submissions' | 'forms'>('submissions');
  const [forms, setForms] = useState<IntakeFormRecord[]>([]);
  const [submissions, setSubmissions] = useState<IntakeSubmissionRecord[]>([]);
  const [loadingForms, setLoadingForms] = useState(false);
  const [loadingSubmissions, setLoadingSubmissions] = useState(false);

  const [selectedSubmissionId, setSelectedSubmissionId] = useState<string | null>(null);
  const [submissionSearch, setSubmissionSearch] = useState('');
  const [submissionStatusFilter, setSubmissionStatusFilter] = useState('all');
  const [submissionFormFilter, setSubmissionFormFilter] = useState('all');

  const [showFormBuilder, setShowFormBuilder] = useState(false);
  const [editingFormId, setEditingFormId] = useState<string | undefined>(undefined);

  const [conflictQuery, setConflictQuery] = useState('');
  const [conflictLoading, setConflictLoading] = useState(false);
  const [conflictResult, setConflictResult] = useState<ConflictCheckResult | null>(null);
  const [showWaiveForm, setShowWaiveForm] = useState(false);
  const [waiveReason, setWaiveReason] = useState('');

  const [reviewNotes, setReviewNotes] = useState('');

  const [showMatterModal, setShowMatterModal] = useState(false);
  const [matterDraft, setMatterDraft] = useState<MatterDraft | null>(null);
  const [creatingMatter, setCreatingMatter] = useState(false);

  const formsById = useMemo(() => {
    return new Map(forms.map(form => [form.id, form]));
  }, [forms]);

  const selectedSubmission = useMemo(() => {
    if (!selectedSubmissionId) return null;
    return submissions.find(item => item.id === selectedSubmissionId) || null;
  }, [submissions, selectedSubmissionId]);

  useEffect(() => {
    let alive = true;
    const load = async () => {
      setLoadingForms(true);
      try {
        const data = await api.intake.forms.list(false);
        if (!alive) return;
        setForms(Array.isArray(data) ? data : []);
      } catch (error) {
        console.error('Failed to load intake forms', error);
        if (alive) toast.error('Failed to load intake forms.');
      } finally {
        if (alive) setLoadingForms(false);
      }
    };
    load();
    return () => {
      alive = false;
    };
  }, []);

  useEffect(() => {
    let alive = true;
    const load = async () => {
      setLoadingSubmissions(true);
      try {
        const params: { formId?: string; status?: string; limit?: number } = { limit: 200 };
        if (submissionFormFilter !== 'all') params.formId = submissionFormFilter;
        if (submissionStatusFilter !== 'all') params.status = submissionStatusFilter;
        const data = await api.intake.submissions.list(params);
        if (!alive) return;
        setSubmissions(Array.isArray(data) ? data : []);
      } catch (error) {
        console.error('Failed to load intake submissions', error);
        if (alive) toast.error('Failed to load intake submissions.');
      } finally {
        if (alive) setLoadingSubmissions(false);
      }
    };
    load();
    return () => {
      alive = false;
    };
  }, [submissionFormFilter, submissionStatusFilter]);

  useEffect(() => {
    if (!selectedSubmission) {
      setConflictQuery('');
      setConflictResult(null);
      setShowWaiveForm(false);
      setWaiveReason('');
      setReviewNotes('');
      return;
    }

    const data = parseSubmissionData(selectedSubmission.dataJson);
    const name = getSubmissionName(data);
    const email = getSubmissionEmail(data);
    const phone = getSubmissionPhone(data);
    setConflictQuery(email || name || phone || '');
    setConflictResult(null);
    setShowWaiveForm(false);
    setWaiveReason('');
    setReviewNotes(selectedSubmission.reviewNotes || '');
  }, [selectedSubmissionId]);

  useEffect(() => {
    if (!selectedSubmissionId) return;
    if (!submissions.some(item => item.id === selectedSubmissionId)) {
      setSelectedSubmissionId(null);
    }
  }, [submissions, selectedSubmissionId]);

  const filteredSubmissions = useMemo(() => {
    const query = submissionSearch.trim().toLowerCase();
    if (!query) return submissions;

    return submissions.filter((submission) => {
      const data = parseSubmissionData(submission.dataJson);
      const name = getSubmissionName(data);
      const email = getSubmissionEmail(data);
      const phone = getSubmissionPhone(data);
      const formName = formsById.get(submission.intakeFormId)?.name || '';
      const haystack = [name, email, phone, formName]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(query);
    });
  }, [submissions, submissionSearch, formsById]);

  const activeForms = useMemo(() => forms.filter(form => form.isActive).length, [forms]);
  const publicForms = useMemo(() => forms.filter(form => form.isPublic).length, [forms]);
  const totalSubmissions = useMemo(() => forms.reduce((sum, form) => sum + (form.submissionCount || 0), 0), [forms]);

  const getStatusLabel = (status?: string) => {
    if (!status) return 'New';
    const normalized = status.toLowerCase();
    if (normalized === 'reviewed') return 'Reviewed';
    if (normalized === 'converted') return 'Converted';
    if (normalized === 'rejected') return 'Rejected';
    return 'New';
  };

  const getStatusBadge = (status?: string) => {
    const normalized = (status || '').toLowerCase();
    if (normalized === 'reviewed') return 'bg-blue-50 text-blue-700 border-blue-100';
    if (normalized === 'converted') return 'bg-emerald-50 text-emerald-700 border-emerald-100';
    if (normalized === 'rejected') return 'bg-red-50 text-red-700 border-red-100';
    return 'bg-amber-50 text-amber-700 border-amber-100';
  };

  const parseSubmissionData = (raw?: string) => {
    if (!raw) return {};
    try {
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object') return parsed as Record<string, any>;
      return {};
    } catch {
      return {};
    }
  };

  const getFirstValue = (data: Record<string, any>, keys: string[]) => {
    for (const key of keys) {
      const value = data?.[key];
      if (typeof value === 'string' && value.trim()) {
        return value.trim();
      }
    }
    return '';
  };

  const getSubmissionName = (data: Record<string, any>) => {
    const direct = getFirstValue(data, ['fullName', 'name', 'clientName', 'contactName']);
    if (direct) return direct;
    const firstName = getFirstValue(data, ['firstName', 'first_name']);
    const lastName = getFirstValue(data, ['lastName', 'last_name']);
    const combined = [firstName, lastName].filter(Boolean).join(' ').trim();
    if (combined) return combined;
    return getFirstValue(data, ['company', 'businessName', 'organization', 'orgName']) || 'Unknown';
  };

  const getSubmissionEmail = (data: Record<string, any>) => {
    return getFirstValue(data, ['email', 'emailAddress', 'contactEmail', 'primaryEmail']);
  };

  const getSubmissionPhone = (data: Record<string, any>) => {
    return getFirstValue(data, ['phone', 'phoneNumber', 'mobile', 'cell', 'mobilePhone']);
  };

  const getCaseNumberFromNotes = (notes?: string) => {
    if (!notes) return '';
    const match = notes.match(/Matter created:\s*(.+)$/i);
    return match ? match[1].trim() : '';
  };

  const getLinkedMatter = (notes?: string) => {
    const caseNumber = getCaseNumberFromNotes(notes);
    if (!caseNumber) return null;
    return matters.find(matter => matter.caseNumber === caseNumber) || null;
  };

  const parseFormFields = (raw?: string) => {
    if (!raw) return [] as IntakePdfField[];

    try {
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [] as IntakePdfField[];

      return parsed
        .filter(field => field && typeof field === 'object')
        .map((field, index) => ({
          id: typeof field.id === 'string' ? field.id : undefined,
          name: typeof field.name === 'string' ? field.name : '',
          label: typeof field.label === 'string' ? field.label : '',
          type: typeof field.type === 'string' ? field.type : 'text',
          required: Boolean(field.required),
          placeholder: typeof field.placeholder === 'string' ? field.placeholder : '',
          helpText: typeof field.helpText === 'string' ? field.helpText : '',
          options: typeof field.options === 'string' ? field.options : '',
          defaultValue: typeof field.defaultValue === 'string' ? field.defaultValue : '',
          validationPattern: typeof field.validationPattern === 'string' ? field.validationPattern : '',
          validationMessage: typeof field.validationMessage === 'string' ? field.validationMessage : '',
          conditionalLogic: typeof field.conditionalLogic === 'string' ? field.conditionalLogic : '',
          order: typeof field.order === 'number' ? field.order : index
        }));
    } catch {
      return [] as IntakePdfField[];
    }
  };

  const handleCopyLink = async (slug: string) => {
    const baseUrl = typeof window !== 'undefined' ? window.location.origin : '';
    const url = `${baseUrl}/intake/${slug}`;
    try {
      await navigator.clipboard.writeText(url);
      toast.success('Public intake link copied.');
    } catch (error) {
      console.error('Failed to copy intake link', error);
      toast.error('Failed to copy intake link.');
    }
  };

  const handleDownloadFormPdf = async (form: IntakeFormRecord) => {
    try {
      const fullForm = await api.intake.forms.get(form.id);
      const fields = parseFormFields(fullForm?.fieldsJson || form.fieldsJson);
      const baseUrl = typeof window !== 'undefined' ? window.location.origin : '';

      await downloadIntakeFormPdf({
        ...form,
        ...fullForm,
        shareUrl: form.slug ? `${baseUrl}/intake/${form.slug}` : '',
        fields
      });

      toast.success('Form PDF downloaded.');
    } catch (error) {
      console.error('Failed to download intake form PDF', error);
      toast.error('Failed to download intake form PDF.');
    }
  };

  const navigateToTab = (tab: string) => {
    if (typeof window === 'undefined') return;
    window.dispatchEvent(new CustomEvent('jf:navigate', { detail: { tab } }));
  };

  const openLinkedLead = (leadId: string) => {
    if (typeof window === 'undefined') return;
    localStorage.setItem('cmd_target_lead', leadId);
    navigateToTab('crm');
  };

  const openLinkedMatter = (matterId: string) => {
    if (typeof window === 'undefined') return;
    localStorage.setItem('cmd_target_matter', matterId);
    navigateToTab('matters');
  };

  const handleToggleForm = async (form: IntakeFormRecord, field: 'isActive' | 'isPublic') => {
    try {
      const payload: any = { [field]: !form[field] };
      const updated = await api.intake.forms.update(form.id, payload);
      setForms(prev => prev.map(item => item.id === form.id ? updated : item));
      toast.success('Form updated.');
    } catch (error) {
      console.error('Failed to update intake form', error);
      toast.error('Failed to update form.');
    }
  };

  const handleRunConflict = async () => {
    if (!selectedSubmission || !conflictQuery.trim()) return;
    setConflictLoading(true);
    try {
      const result = await api.conflicts.check({
        searchQuery: conflictQuery.trim(),
        checkType: 'IntakeSubmission',
        entityType: 'Client',
        entityId: selectedSubmission.id
      });
      setConflictResult(result);
    } catch (error) {
      console.error('Conflict check failed', error);
      toast.error('Conflict check failed.');
    } finally {
      setConflictLoading(false);
    }
  };

  const handleWaiveConflict = async () => {
    if (!conflictResult || !waiveReason.trim()) return;
    try {
      await api.conflicts.waive(conflictResult.id, waiveReason.trim());
      setConflictResult({ ...conflictResult, status: 'Waived', waiverReason: waiveReason.trim() });
      setShowWaiveForm(false);
      toast.success('Conflict waiver recorded.');
    } catch (error) {
      console.error('Failed to waive conflict', error);
      toast.error('Failed to waive conflict.');
    }
  };

  const updateSubmissionStatus = (id: string, status: string, notes?: string) => {
    setSubmissions(prev => prev.map(item => item.id === id ? {
      ...item,
      status,
      reviewNotes: notes ?? item.reviewNotes,
      reviewedAt: new Date().toISOString()
    } : item));
  };

  const handleReview = async (status: 'Reviewed' | 'Rejected') => {
    if (!selectedSubmission) return;
    try {
      await api.intake.submissions.review(selectedSubmission.id, { status, notes: reviewNotes || undefined });
      updateSubmissionStatus(selectedSubmission.id, status, reviewNotes);
      toast.success(`Submission marked as ${status.toLowerCase()}.`);
    } catch (error) {
      console.error('Failed to update submission', error);
      toast.error('Failed to update submission.');
    }
  };

  const handleConvertToLead = async () => {
    if (!selectedSubmission) return;
    try {
      const result = await api.intake.submissions.convertToLead(selectedSubmission.id);
      const leadId = result?.leadId as string | undefined;
      setSubmissions(prev => prev.map(item => item.id === selectedSubmission.id ? {
        ...item,
        leadId: leadId || item.leadId
      } : item));
      updateSubmissionStatus(selectedSubmission.id, 'Converted', reviewNotes);
      toast.success('Submission converted to lead.');
    } catch (error) {
      console.error('Failed to convert to lead', error);
      toast.error('Failed to convert to lead.');
    }
  };

  const generateCaseNumber = () => {
    const year = new Date().getFullYear();
    const suffix = String(Date.now()).slice(-5);
    return `INT-${year}-${suffix}`;
  };

  const buildMatterDraft = (submission: IntakeSubmissionRecord): MatterDraft => {
    const data = parseSubmissionData(submission.dataJson);
    const name = getSubmissionName(data);
    const email = getSubmissionEmail(data);
    const phone = getSubmissionPhone(data);
    const form = formsById.get(submission.intakeFormId);
    const matchedClient = clients.find(client => client.email?.toLowerCase() === email.toLowerCase());

    return {
      clientMode: matchedClient ? 'existing' : 'new',
      clientId: matchedClient?.id || '',
      clientName: name === 'Unknown' ? '' : name,
      clientEmail: email,
      clientPhone: phone,
      clientType: 'Individual',
      matterName: name && name !== 'Unknown' ? `Intake - ${name}` : 'New Intake Matter',
      caseNumber: generateCaseNumber(),
      practiceArea: form?.practiceArea || 'General Practice',
      feeStructure: FeeStructure.Hourly,
      responsibleAttorney: user?.initials || user?.name || 'Partner',
      billableRate: '',
      trustBalance: ''
    };
  };

  const handleOpenMatterModal = () => {
    if (!selectedSubmission) return;
    setMatterDraft(buildMatterDraft(selectedSubmission));
    setShowMatterModal(true);
  };

  const handleCreateMatter = async () => {
    if (!selectedSubmission || !matterDraft) return;
    setCreatingMatter(true);
    try {
      let client = null;
      if (matterDraft.clientMode === 'existing') {
        client = clients.find(item => item.id === matterDraft.clientId) || null;
        if (!client) {
          toast.error('Select an existing client.');
          return;
        }
      } else {
        if (!matterDraft.clientName.trim() || !matterDraft.clientEmail.trim()) {
          toast.error('Client name and email are required.');
          return;
        }
        client = await addClient({
          name: matterDraft.clientName.trim(),
          email: matterDraft.clientEmail.trim(),
          phone: matterDraft.clientPhone.trim() || undefined,
          type: matterDraft.clientType,
          status: 'Active'
        });
      }

      const practiceArea = matterDraft.practiceArea.trim() || 'General Practice';
      const billableRate = parseFloat(matterDraft.billableRate) || 0;
      const trustBalance = parseFloat(matterDraft.trustBalance) || 0;
      const caseNumber = matterDraft.caseNumber.trim() || generateCaseNumber();

      await addMatter({
        name: matterDraft.matterName.trim() || 'New Matter',
        caseNumber,
        practiceArea: practiceArea as PracticeArea,
        feeStructure: matterDraft.feeStructure || FeeStructure.Hourly,
        status: CaseStatus.Open,
        responsibleAttorney: matterDraft.responsibleAttorney || user?.initials || 'Partner',
        billableRate,
        trustBalance,
        clientId: client.id,
        client
      });

      await api.intake.submissions.review(selectedSubmission.id, {
        status: 'Converted',
        notes: `Matter created: ${caseNumber}`
      });
      updateSubmissionStatus(selectedSubmission.id, 'Converted', `Matter created: ${caseNumber}`);

      toast.success('Matter created from intake submission.');
      setShowMatterModal(false);
      setMatterDraft(null);
    } catch (error) {
      console.error('Failed to create matter', error);
      toast.error('Failed to create matter.');
    } finally {
      setCreatingMatter(false);
    }
  };

  const renderConflictStatus = () => {
    if (!conflictResult) {
      return (
        <div className="text-sm text-gray-500">
          Run a conflict check to clear this intake before engagement.
        </div>
      );
    }
    const status = conflictResult.status.toLowerCase();
    if (status === 'clear') {
      return (
        <div className="flex items-center gap-3 p-3 bg-emerald-50 border border-emerald-100 rounded-lg">
          <CheckCircle className="w-5 h-5 text-emerald-600" />
          <div>
            <p className="text-sm font-semibold text-emerald-700">No conflicts found</p>
            <p className="text-xs text-emerald-600">You may proceed with this intake.</p>
          </div>
        </div>
      );
    }
    if (status === 'waived') {
      return (
        <div className="flex items-center gap-3 p-3 bg-amber-50 border border-amber-100 rounded-lg">
          <AlertTriangle className="w-5 h-5 text-amber-600" />
          <div>
            <p className="text-sm font-semibold text-amber-700">Conflict waived</p>
            <p className="text-xs text-amber-600">{conflictResult.waiverReason || 'Waiver recorded.'}</p>
          </div>
        </div>
      );
    }
    return (
      <div className="flex items-center gap-3 p-3 bg-red-50 border border-red-100 rounded-lg">
        <AlertTriangle className="w-5 h-5 text-red-600" />
        <div>
          <p className="text-sm font-semibold text-red-700">
            {conflictResult.matchCount} potential conflict{conflictResult.matchCount === 1 ? '' : 's'} found
          </p>
          <p className="text-xs text-red-600">Review matches before proceeding.</p>
        </div>
      </div>
    );
  };

  const getRiskBadge = (riskLevel: string) => {
    const level = (riskLevel || '').toLowerCase();
    if (level === 'high') return 'bg-red-50 text-red-700 border-red-100';
    if (level === 'medium') return 'bg-amber-50 text-amber-700 border-amber-100';
    return 'bg-blue-50 text-blue-700 border-blue-100';
  };

  // RENDER
  return (
    <div className="p-8 h-full flex flex-col bg-gray-50/50">
      <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Intake</h1>
          <p className="text-sm text-gray-500 mt-1">Manage intake forms, submissions, and conflict checks.</p>
        </div>
        <div className="flex items-center gap-3">
          <div className="flex gap-2 bg-gray-100 p-1 rounded-lg">
            <button
              onClick={() => setView('submissions')}
              className={`px-4 py-1.5 text-sm font-medium rounded-md transition-all ${view === 'submissions'
                ? 'bg-white text-slate-800 shadow-sm'
                : 'text-gray-500 hover:text-gray-700'
                }`}
            >
              Submissions
            </button>
            <button
              onClick={() => setView('forms')}
              className={`px-4 py-1.5 text-sm font-medium rounded-md transition-all ${view === 'forms'
                ? 'bg-white text-slate-800 shadow-sm'
                : 'text-gray-500 hover:text-gray-700'
                }`}
            >
              Forms
            </button>
          </div>
          {view === 'forms' && (
            <button
              onClick={() => {
                setEditingFormId(undefined);
                setShowFormBuilder(true);
              }}
              className="flex items-center gap-2 px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-semibold hover:bg-slate-900"
            >
              <Plus className="w-4 h-4" />
              New Form
            </button>
          )}
        </div>
      </div>

      {view === 'forms' ? (
        <div className="flex-1 overflow-hidden flex flex-col">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
              <p className="text-xs font-semibold text-gray-500 uppercase">Total Forms</p>
              <p className="text-3xl font-bold text-slate-800 mt-2">{forms.length}</p>
            </div>
            <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
              <p className="text-xs font-semibold text-gray-500 uppercase">Active Forms</p>
              <p className="text-3xl font-bold text-slate-800 mt-2">{activeForms}</p>
            </div>
            <div className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
              <p className="text-xs font-semibold text-gray-500 uppercase">Public Forms</p>
              <p className="text-3xl font-bold text-slate-800 mt-2">{publicForms}</p>
            </div>
          </div>

          <div className="bg-white rounded-xl border border-gray-200 shadow-card flex-1 overflow-hidden flex flex-col">
            <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100">
              <div>
                <h2 className="text-lg font-semibold text-slate-800">Intake Forms</h2>
                <p className="text-xs text-gray-500">Total submissions: {totalSubmissions}</p>
              </div>
              <button
                onClick={async () => {
                  setLoadingForms(true);
                  try {
                    const data = await api.intake.forms.list(false);
                    setForms(Array.isArray(data) ? data : []);
                    toast.success('Forms refreshed.');
                  } catch (error) {
                    console.error('Failed to refresh forms', error);
                    toast.error('Failed to refresh forms.');
                  } finally {
                    setLoadingForms(false);
                  }
                }}
                className="flex items-center gap-2 px-3 py-2 text-sm font-medium text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50"
              >
                <RefreshCw className="w-4 h-4" />
                Refresh
              </button>
            </div>

            <div className="flex-1 overflow-y-auto p-6">
              {loadingForms ? (
                <div className="flex items-center justify-center h-full text-gray-400">Loading forms...</div>
              ) : forms.length === 0 ? (
                <div className="text-center text-gray-500 py-16">
                  <FileText className="w-10 h-10 mx-auto text-gray-300 mb-3" />
                  <p className="font-medium">No intake forms yet.</p>
                  <p className="text-sm text-gray-400 mt-1">Create your first form to start collecting submissions.</p>
                </div>
              ) : (
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
                  {forms.map(form => (
                    <div key={form.id} className="border border-gray-200 rounded-xl p-5 bg-white shadow-sm">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-lg font-semibold text-slate-800">{form.name}</p>
                          {form.description && (
                            <p className="text-sm text-gray-500 mt-1">{form.description}</p>
                          )}
                        </div>
                        <button
                          onClick={() => {
                            setEditingFormId(form.id);
                            setShowFormBuilder(true);
                          }}
                          title="Edit form"
                          aria-label="Edit form"
                          className="p-2 text-gray-500 hover:bg-gray-100 rounded-lg"
                        >
                          <Edit className="w-4 h-4" />
                        </button>
                      </div>

                      <div className="flex flex-wrap items-center gap-2 mt-3">
                        <span className={`text-xs font-semibold px-2 py-1 rounded-full border ${form.isActive ? 'bg-emerald-50 text-emerald-700 border-emerald-100' : 'bg-gray-100 text-gray-600 border-gray-200'}`}>
                          {form.isActive ? 'Active' : 'Inactive'}
                        </span>
                        <span className={`text-xs font-semibold px-2 py-1 rounded-full border ${form.isPublic ? 'bg-blue-50 text-blue-700 border-blue-100' : 'bg-gray-100 text-gray-600 border-gray-200'}`}>
                          {form.isPublic ? 'Public' : 'Private'}
                        </span>
                        {form.practiceArea && (
                          <span className="text-xs font-semibold px-2 py-1 rounded-full border bg-slate-50 text-slate-600 border-slate-200">
                            {form.practiceArea}
                          </span>
                        )}
                      </div>

                      <div className="mt-4 flex items-center justify-between text-sm text-gray-500">
                        <span>Submissions: {form.submissionCount || 0}</span>
                        <span>Created: {formatDate(form.createdAt)}</span>
                      </div>

                      <div className="mt-4 bg-gray-50 border border-gray-200 rounded-lg p-3 text-xs text-gray-600 flex items-center gap-2">
                        <span className="flex-1 truncate">/intake/{form.slug}</span>
                        <button
                          onClick={() => handleCopyLink(form.slug)}
                          title="Copy public link"
                          aria-label="Copy public link"
                          className="p-1.5 rounded-md hover:bg-white border border-transparent hover:border-gray-200"
                        >
                          <Copy className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDownloadFormPdf(form)}
                          title="Download form PDF"
                          aria-label="Download form PDF"
                          className="p-1.5 rounded-md hover:bg-white border border-transparent hover:border-gray-200"
                        >
                          <Download className="w-4 h-4" />
                        </button>
                        <a
                          href={`/intake/${form.slug}`}
                          target="_blank"
                          rel="noreferrer"
                          title="Open public form"
                          aria-label="Open public form"
                          className="p-1.5 rounded-md hover:bg-white border border-transparent hover:border-gray-200"
                        >
                          <ExternalLink className="w-4 h-4" />
                        </a>
                      </div>

                      <div className="mt-4 flex flex-wrap gap-2">
                        <button
                          onClick={() => handleToggleForm(form, 'isActive')}
                          className="px-3 py-1.5 text-xs font-semibold border border-gray-200 rounded-lg hover:bg-gray-50"
                        >
                          {form.isActive ? 'Deactivate' : 'Activate'}
                        </button>
                        <button
                          onClick={() => handleToggleForm(form, 'isPublic')}
                          className="px-3 py-1.5 text-xs font-semibold border border-gray-200 rounded-lg hover:bg-gray-50"
                        >
                          {form.isPublic ? 'Make Private' : 'Make Public'}
                        </button>
                        <button
                          onClick={() => {
                            setView('submissions');
                            setSubmissionFormFilter(form.id);
                            setSubmissionStatusFilter('all');
                          }}
                          className="px-3 py-1.5 text-xs font-semibold border border-gray-200 rounded-lg hover:bg-gray-50"
                        >
                          View Submissions
                        </button>
                        <button
                          onClick={() => handleDownloadFormPdf(form)}
                          className="inline-flex items-center gap-2 px-3 py-1.5 text-xs font-semibold border border-gray-200 rounded-lg hover:bg-gray-50"
                        >
                          <Download className="w-3.5 h-3.5" />
                          Download PDF
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      ) : (
        <div className="flex-1 overflow-hidden">
          <div className="flex-1 overflow-hidden grid grid-cols-1 lg:grid-cols-[360px_1fr] gap-6 min-h-0">
            <div className="bg-white border border-gray-200 rounded-xl shadow-card flex flex-col min-h-0">
              <div className="p-4 border-b border-gray-100 space-y-3">
                <div className="relative">
                  <Search className="w-4 h-4 text-gray-400 absolute left-3 top-2.5" />
                  <input
                    value={submissionSearch}
                    onChange={(e) => setSubmissionSearch(e.target.value)}
                    placeholder="Search submissions..."
                    className="w-full pl-9 pr-3 py-2 border border-gray-200 rounded-lg text-sm bg-white text-slate-900 focus:ring-2 focus:ring-slate-200 focus:border-slate-300"
                  />
                </div>
                <div className="flex items-center gap-2">
                  <div className="flex items-center gap-2 text-xs font-semibold text-gray-500">
                    <Filter className="w-3.5 h-3.5" />
                    Status
                  </div>
                  <select
                    value={submissionStatusFilter}
                    onChange={(e) => setSubmissionStatusFilter(e.target.value)}
                    className="flex-1 border border-gray-200 rounded-lg px-2 py-1.5 text-xs text-gray-700"
                  >
                    <option value="all">All</option>
                    <option value="New">New</option>
                    <option value="Reviewed">Reviewed</option>
                    <option value="Converted">Converted</option>
                    <option value="Rejected">Rejected</option>
                  </select>
                </div>
                <div className="flex items-center gap-2">
                  <div className="flex items-center gap-2 text-xs font-semibold text-gray-500">
                    <Filter className="w-3.5 h-3.5" />
                    Form
                  </div>
                  <select
                    value={submissionFormFilter}
                    onChange={(e) => setSubmissionFormFilter(e.target.value)}
                    className="flex-1 border border-gray-200 rounded-lg px-2 py-1.5 text-xs text-gray-700"
                  >
                    <option value="all">All forms</option>
                    {forms.map(form => (
                      <option key={form.id} value={form.id}>{form.name}</option>
                    ))}
                  </select>
                </div>
              </div>
              <div className="flex-1 overflow-y-auto">
                {loadingSubmissions ? (
                  <div className="flex items-center justify-center h-full text-gray-400">Loading submissions...</div>
                ) : filteredSubmissions.length === 0 ? (
                  <div className="text-center text-gray-400 py-10">No submissions found.</div>
                ) : (
                  filteredSubmissions.map(submission => {
                    const data = parseSubmissionData(submission.dataJson);
                    const name = getSubmissionName(data);
                    const email = getSubmissionEmail(data);
                    const phone = getSubmissionPhone(data);
                    const formName = formsById.get(submission.intakeFormId)?.name || 'Intake Form';
                    const linkedMatter = getLinkedMatter(submission.reviewNotes);
                    const hasLead = Boolean(submission.leadId);

                    return (
                      <div
                        key={submission.id}
                        role="button"
                        tabIndex={0}
                        onClick={() => setSelectedSubmissionId(submission.id)}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            setSelectedSubmissionId(submission.id);
                          }
                        }}
                        className={`w-full text-left px-4 py-3 border-b border-gray-100 hover:bg-gray-50 transition cursor-pointer ${selectedSubmissionId === submission.id ? 'bg-slate-50' : ''}`}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-slate-800 truncate">{name}</span>
                          <span className={`text-[11px] font-semibold px-2 py-0.5 rounded-full border ${getStatusBadge(submission.status)}`}>
                            {getStatusLabel(submission.status)}
                          </span>
                        </div>
                        <div className="text-xs text-gray-500 mt-1 truncate">
                          {email || phone || 'No contact details'}
                        </div>
                        <div className="text-[11px] text-gray-400 mt-1 flex items-center justify-between">
                          <span className="truncate">{formName}</span>
                          <span>{formatDate(submission.createdAt)}</span>
                        </div>
                        {(hasLead || linkedMatter) && (
                          <div className="mt-2 flex flex-wrap gap-2 text-[11px]">
                            {hasLead && submission.leadId && (
                              <button
                                type="button"
                                onClick={(event) => {
                                  event.stopPropagation();
                                  openLinkedLead(submission.leadId as string);
                                }}
                                className="px-2 py-1 rounded border border-blue-200 text-blue-700 hover:bg-blue-50"
                              >
                                Open Lead
                              </button>
                            )}
                            {linkedMatter && (
                              <button
                                type="button"
                                onClick={(event) => {
                                  event.stopPropagation();
                                  openLinkedMatter(linkedMatter.id);
                                }}
                                className="px-2 py-1 rounded border border-emerald-200 text-emerald-700 hover:bg-emerald-50"
                              >
                                Open Matter
                              </button>
                            )}
                          </div>
                        )}
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            <div className="bg-white border border-gray-200 rounded-xl shadow-card flex flex-col min-h-0 overflow-hidden">
              {!selectedSubmission ? (
                <div className="flex-1 flex flex-col items-center justify-center text-gray-400">
                  <Shield className="w-10 h-10 text-gray-300 mb-3" />
                  <p className="text-sm">Select a submission to review details.</p>
                </div>
              ) : (
                <>
                  <div className="p-6 border-b border-gray-100">
                    <div className="flex items-start justify-between gap-4">
                      <div>
                        <p className="text-xs uppercase text-gray-400 font-semibold">Submission</p>
                        <h2 className="text-xl font-bold text-slate-900 mt-1">
                          {getSubmissionName(parseSubmissionData(selectedSubmission.dataJson))}
                        </h2>
                        <p className="text-sm text-gray-500 mt-1">
                          {formsById.get(selectedSubmission.intakeFormId)?.name || 'Intake Form'}
                        </p>
                      </div>
                      <span className={`text-xs font-semibold px-3 py-1 rounded-full border ${getStatusBadge(selectedSubmission.status)}`}>
                        {getStatusLabel(selectedSubmission.status)}
                      </span>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3 mt-4 text-sm text-gray-600">
                      <div>
                        <p className="text-xs text-gray-400 uppercase font-semibold">Submitted</p>
                        <p className="font-medium">{formatDate(selectedSubmission.createdAt)}</p>
                      </div>
                      <div>
                        <p className="text-xs text-gray-400 uppercase font-semibold">Email</p>
                        <p className="font-medium">{getSubmissionEmail(parseSubmissionData(selectedSubmission.dataJson)) || 'Not provided'}</p>
                      </div>
                      <div>
                        <p className="text-xs text-gray-400 uppercase font-semibold">Phone</p>
                        <p className="font-medium">{getSubmissionPhone(parseSubmissionData(selectedSubmission.dataJson)) || 'Not provided'}</p>
                      </div>
                    </div>

                    <div className="mt-5 flex flex-wrap gap-2">
                      <button
                        onClick={() => handleReview('Reviewed')}
                        className="px-3 py-2 text-xs font-semibold bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                        disabled={(selectedSubmission.status || '').toLowerCase() === 'rejected'}
                      >
                        Mark Reviewed
                      </button>
                      <button
                        onClick={handleConvertToLead}
                        className="px-3 py-2 text-xs font-semibold bg-emerald-600 text-white rounded-lg hover:bg-emerald-700"
                        disabled={(selectedSubmission.status || '').toLowerCase() === 'rejected'}
                      >
                        Convert to Lead
                      </button>
                      <button
                        onClick={handleOpenMatterModal}
                        className="px-3 py-2 text-xs font-semibold bg-slate-800 text-white rounded-lg hover:bg-slate-900"
                        disabled={(selectedSubmission.status || '').toLowerCase() === 'rejected'}
                      >
                        Create Matter
                      </button>
                      <button
                        onClick={() => handleReview('Rejected')}
                        className="px-3 py-2 text-xs font-semibold bg-red-50 text-red-600 rounded-lg hover:bg-red-100"
                      >
                        Reject
                      </button>
                    </div>
                  </div>

                  <div className="flex-1 overflow-y-auto">
                    <div className="p-6 border-b border-gray-100">
                      <div className="flex items-center justify-between mb-3">
                        <h3 className="text-sm font-semibold text-slate-800">Conflict Check</h3>
                        <span className="text-xs text-gray-400">ABA 1.7 review</span>
                      </div>
                      <div className="flex flex-wrap gap-2 mb-3">
                        <div className="flex-1 min-w-[220px]">
                          <input
                            value={conflictQuery}
                            onChange={(e) => setConflictQuery(e.target.value)}
                            placeholder="Search name, email, or company"
                            className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                          />
                        </div>
                        <button
                          onClick={handleRunConflict}
                          disabled={conflictLoading || !conflictQuery.trim()}
                          className="px-4 py-2 text-sm font-semibold bg-amber-500 text-white rounded-lg hover:bg-amber-600 disabled:opacity-60"
                        >
                          {conflictLoading ? 'Checking...' : 'Run Check'}
                        </button>
                      </div>
                      {renderConflictStatus()}

                      {conflictResult && conflictResult.results?.length > 0 && (
                        <div className="mt-4 space-y-3">
                          {conflictResult.results.map(match => (
                            <div key={match.id} className="border border-gray-200 rounded-lg p-3">
                              <div className="flex items-center justify-between">
                                <div>
                                  <p className="text-sm font-semibold text-slate-800">{match.matchedEntityName}</p>
                                  <p className="text-xs text-gray-500">
                                    {match.matchedEntityType} - {match.matchType} ({match.matchScore}%)
                                  </p>
                                  {match.relatedMatterName && (
                                    <p className="text-xs text-gray-500 mt-1">Related matter: {match.relatedMatterName}</p>
                                  )}
                                </div>
                                <span className={`text-[11px] font-semibold px-2 py-1 rounded-full border ${getRiskBadge(match.riskLevel)}`}>
                                  {match.riskLevel} Risk
                                </span>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}

                      {conflictResult && conflictResult.status.toLowerCase() === 'conflict' && (
                        <div className="mt-4">
                          {!showWaiveForm ? (
                            <button
                              onClick={() => setShowWaiveForm(true)}
                              className="text-xs font-semibold text-amber-600 hover:text-amber-700"
                            >
                              Record conflict waiver
                            </button>
                          ) : (
                            <div className="space-y-3">
                              <textarea
                                value={waiveReason}
                                onChange={(e) => setWaiveReason(e.target.value)}
                                placeholder="Document the waiver reason and consent."
                                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm min-h-[90px]"
                              />
                              <div className="flex items-center gap-2">
                                <button
                                  onClick={handleWaiveConflict}
                                  disabled={!waiveReason.trim()}
                                  className="px-3 py-2 text-xs font-semibold bg-amber-600 text-white rounded-lg hover:bg-amber-700 disabled:opacity-60"
                                >
                                  Save Waiver
                                </button>
                                <button
                                  onClick={() => {
                                    setShowWaiveForm(false);
                                    setWaiveReason('');
                                  }}
                                  className="px-3 py-2 text-xs font-semibold text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50"
                                >
                                  Cancel
                                </button>
                              </div>
                            </div>
                          )}
                        </div>
                      )}
                    </div>

                    <div className="p-6 border-b border-gray-100">
                      <h3 className="text-sm font-semibold text-slate-800 mb-3">Review Notes</h3>
                      <textarea
                        value={reviewNotes}
                        onChange={(e) => setReviewNotes(e.target.value)}
                        placeholder="Add internal review notes or next steps."
                        className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm min-h-[90px]"
                      />
                    </div>

                    <div className="p-6">
                      <h3 className="text-sm font-semibold text-slate-800 mb-3">Submission Data</h3>
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-sm">
                        {Object.entries(parseSubmissionData(selectedSubmission.dataJson))
                          .filter(([, value]) => value !== null && value !== undefined && String(value).trim() !== '')
                          .map(([key, value]) => (
                            <div key={key} className="border border-gray-200 rounded-lg p-3 bg-gray-50">
                              <p className="text-xs font-semibold text-gray-500 uppercase">{key}</p>
                              <p className="text-sm text-slate-800 mt-1">
                                {typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
                                  ? String(value)
                                  : JSON.stringify(value)}
                              </p>
                            </div>
                          ))}
                      </div>
                    </div>
                  </div>
                </>
              )}
            </div>
          </div>
        </div>
      )}

      {showFormBuilder && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="flex h-[94vh] w-[min(96vw,1800px)] flex-col overflow-hidden rounded-xl bg-white shadow-2xl">
            <div className="flex shrink-0 items-center justify-between border-b border-gray-100 bg-gray-50 px-6 py-4">
              <div>
                <p className="text-xs uppercase text-gray-500 font-semibold">Intake Form</p>
                <h3 className="text-lg font-bold text-slate-800">{editingFormId ? 'Edit Form' : 'New Form'}</h3>
              </div>
              <button
                onClick={() => {
                  setShowFormBuilder(false);
                  setEditingFormId(undefined);
                }}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="min-h-0 flex-1 overflow-hidden p-4 md:p-6">
              <IntakeFormBuilder
                formId={editingFormId}
                onSave={(form) => {
                  setShowFormBuilder(false);
                  setEditingFormId(undefined);
                  setForms(prev => {
                    const exists = prev.some(item => item.id === form.id);
                    if (exists) {
                      return prev.map(item => item.id === form.id ? form : item);
                    }
                    return [form, ...prev];
                  });
                  toast.success('Form saved.');
                }}
                onCancel={() => {
                  setShowFormBuilder(false);
                  setEditingFormId(undefined);
                }}
              />
            </div>
          </div>
        </div>
      )}

      {showMatterModal && matterDraft && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-2xl overflow-hidden">
            <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100 bg-gray-50">
              <div>
                <p className="text-xs uppercase text-gray-500 font-semibold">Create Matter</p>
                <h3 className="text-lg font-bold text-slate-800">Convert Intake to Matter</h3>
              </div>
              <button
                onClick={() => {
                  setShowMatterModal(false);
                  setMatterDraft(null);
                }}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="p-6 space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Matter Name</label>
                  <input
                    value={matterDraft.matterName}
                    onChange={(e) => setMatterDraft({ ...matterDraft, matterName: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Case Number</label>
                  <input
                    value={matterDraft.caseNumber}
                    onChange={(e) => setMatterDraft({ ...matterDraft, caseNumber: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Practice Area</label>
                  <select
                    value={matterDraft.practiceArea}
                    onChange={(e) => setMatterDraft({ ...matterDraft, practiceArea: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  >
                    {Object.values(PracticeArea).map(area => (
                      <option key={area} value={area}>{area}</option>
                    ))}
                    <option value="General Practice">General Practice</option>
                  </select>
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Fee Structure</label>
                  <select
                    value={matterDraft.feeStructure}
                    onChange={(e) => setMatterDraft({ ...matterDraft, feeStructure: e.target.value as FeeStructure })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  >
                    {Object.values(FeeStructure).map(item => (
                      <option key={item} value={item}>{item}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Responsible Attorney</label>
                  <input
                    value={matterDraft.responsibleAttorney}
                    onChange={(e) => setMatterDraft({ ...matterDraft, responsibleAttorney: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Billable Rate</label>
                  <input
                    value={matterDraft.billableRate}
                    onChange={(e) => setMatterDraft({ ...matterDraft, billableRate: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                    placeholder="0.00"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-gray-500 uppercase">Trust Balance</label>
                  <input
                    value={matterDraft.trustBalance}
                    onChange={(e) => setMatterDraft({ ...matterDraft, trustBalance: e.target.value })}
                    className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                    placeholder="0.00"
                  />
                </div>
              </div>

              <div className="border-t border-gray-100 pt-4 space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-semibold text-gray-500 uppercase">Client</p>
                    <p className="text-sm text-gray-500">Link or create the client record.</p>
                  </div>
                  <div className="flex gap-2 bg-gray-100 p-1 rounded-lg">
                    <button
                      onClick={() => setMatterDraft({ ...matterDraft, clientMode: 'existing' })}
                      className={`px-3 py-1.5 text-xs font-semibold rounded-md ${matterDraft.clientMode === 'existing'
                        ? 'bg-white text-slate-800 shadow-sm'
                        : 'text-gray-500'
                        }`}
                    >
                      Existing
                    </button>
                    <button
                      onClick={() => setMatterDraft({ ...matterDraft, clientMode: 'new' })}
                      className={`px-3 py-1.5 text-xs font-semibold rounded-md ${matterDraft.clientMode === 'new'
                        ? 'bg-white text-slate-800 shadow-sm'
                        : 'text-gray-500'
                        }`}
                    >
                      New
                    </button>
                  </div>
                </div>

                {matterDraft.clientMode === 'existing' ? (
                  <select
                    value={matterDraft.clientId}
                    onChange={(e) => setMatterDraft({ ...matterDraft, clientId: e.target.value })}
                    className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                  >
                    <option value="">Select a client...</option>
                    {clients.map(client => (
                      <option key={client.id} value={client.id}>{client.name} ({client.email})</option>
                    ))}
                  </select>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase">Client Name</label>
                      <input
                        value={matterDraft.clientName}
                        onChange={(e) => setMatterDraft({ ...matterDraft, clientName: e.target.value })}
                        className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                      />
                    </div>
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase">Client Email</label>
                      <input
                        value={matterDraft.clientEmail}
                        onChange={(e) => setMatterDraft({ ...matterDraft, clientEmail: e.target.value })}
                        className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                      />
                    </div>
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase">Client Phone</label>
                      <input
                        value={matterDraft.clientPhone}
                        onChange={(e) => setMatterDraft({ ...matterDraft, clientPhone: e.target.value })}
                        className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                      />
                    </div>
                    <div>
                      <label className="text-xs font-semibold text-gray-500 uppercase">Client Type</label>
                      <select
                        value={matterDraft.clientType}
                        onChange={(e) => setMatterDraft({ ...matterDraft, clientType: e.target.value as 'Individual' | 'Corporate' })}
                        className="mt-1 w-full border border-gray-300 rounded-lg px-3 py-2 text-sm"
                      >
                        <option value="Individual">Individual</option>
                        <option value="Corporate">Corporate</option>
                      </select>
                    </div>
                  </div>
                )}
              </div>
            </div>
            <div className="flex items-center justify-end gap-2 px-6 py-4 border-t border-gray-100 bg-gray-50">
              <button
                onClick={() => {
                  setShowMatterModal(false);
                  setMatterDraft(null);
                }}
                className="px-3 py-2 text-sm font-semibold text-gray-600 hover:bg-gray-100 rounded-lg"
                disabled={creatingMatter}
              >
                Cancel
              </button>
              <button
                onClick={handleCreateMatter}
                className="px-4 py-2 text-sm font-semibold bg-slate-800 text-white rounded-lg hover:bg-slate-900 disabled:opacity-60"
                disabled={creatingMatter}
              >
                {creatingMatter ? 'Creating...' : 'Create Matter'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Intake;
