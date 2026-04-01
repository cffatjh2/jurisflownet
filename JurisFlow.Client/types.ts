export enum CaseStatus {
  Open = 'Open',
  Pending = 'Pending',
  Trial = 'Trial',
  Closed = 'Closed',
  Archived = 'Archived'
}

// US Legal Practice Areas (Comprehensive)
export enum PracticeArea {
  // Personal Injury
  PersonalInjury = 'Personal Injury',
  AutoAccident = 'Auto Accident',
  MedicalMalpractice = 'Medical Malpractice',
  ProductLiability = 'Product Liability',
  WorkersCompensation = 'Workers Compensation',
  WrongfulDeath = 'Wrongful Death',

  // Commercial Litigation
  CivilLitigation = 'Civil Litigation',
  CommercialLitigation = 'Commercial Litigation',
  ContractDisputes = 'Contract Disputes',

  // Criminal
  CriminalDefense = 'Criminal Defense',
  WhiteCollarCrime = 'White Collar Crime',
  DUI = 'DUI/DWI',

  // Family Law
  FamilyLaw = 'Family Law',
  Divorce = 'Divorce',
  ChildCustody = 'Child Custody',
  ChildSupport = 'Child Support',
  Adoption = 'Adoption',

  // Corporate/Business
  Corporate = 'Corporate Law',
  BusinessFormation = 'Business Formation',
  MergersAcquisitions = 'Mergers & Acquisitions',
  Securities = 'Securities',

  // Real Estate
  RealEstate = 'Real Estate',
  CommercialRealEstate = 'Commercial Real Estate',
  LandlordTenant = 'Landlord/Tenant',

  // Intellectual Property
  IntellectualProperty = 'Intellectual Property',
  Patent = 'Patent',
  Trademark = 'Trademark',
  Copyright = 'Copyright',

  // Estate/Probate
  EstatePlanning = 'Estate Planning',
  Probate = 'Probate',
  TrustAdministration = 'Trust Administration',

  // Other Specialty
  Bankruptcy = 'Bankruptcy',
  Immigration = 'Immigration',
  Employment = 'Employment Law',
  Tax = 'Tax Law',
  EnvironmentalLaw = 'Environmental Law',
  HealthcareLaw = 'Healthcare Law'
}

// US Court Types (Federal & State Hierarchy)
export enum CourtType {
  // Federal Courts
  USSupremeCourt = 'U.S. Supreme Court',
  USCourtOfAppeals = 'U.S. Court of Appeals',
  USDistrictCourt = 'U.S. District Court',
  USBankruptcyCourt = 'U.S. Bankruptcy Court',
  USMagistrateJudge = 'U.S. Magistrate Judge',

  // State Courts
  StateSupremeCourt = 'State Supreme Court',
  StateAppellateCourt = 'State Court of Appeals',
  StateSuperiorCourt = 'Superior Court',
  StateCircuitCourt = 'Circuit Court',
  StateDistrictCourt = 'District Court',
  CountyCourt = 'County Court',
  MunicipalCourt = 'Municipal Court',

  // Specialty Courts
  FamilyCourt = 'Family Court',
  ProbateCourt = 'Probate Court',
  SmallClaimsCourt = 'Small Claims Court',
  TrafficCourt = 'Traffic Court',

  // Alternative Dispute Resolution
  Arbitration = 'Arbitration',
  Mediation = 'Mediation',
  AdminHearing = 'Administrative Hearing'
}

// Matter/Case Phases (Litigation Lifecycle)
export enum MatterPhase {
  Investigation = 'Investigation',
  PreLitigation = 'Pre-Litigation',
  ComplaintFiled = 'Complaint Filed',
  Discovery = 'Discovery',
  Depositions = 'Depositions',
  MotionPractice = 'Motion Practice',
  SettlementNegotiation = 'Settlement Negotiation',
  MediationPhase = 'Mediation',
  TrialPreparation = 'Trial Preparation',
  Trial = 'Trial',
  PostTrial = 'Post-Trial',
  Appeal = 'Appeal',
  Closed = 'Closed'
}

// Lead Pipeline Status (8-Stage Funnel)
export enum LeadStatus {
  NewInquiry = 'New Inquiry',
  InitialContact = 'Initial Contact',
  Qualified = 'Qualified',
  ConsultationScheduled = 'Consultation Scheduled',
  ConsultationCompleted = 'Consultation Completed',
  ProposalSent = 'Proposal Sent',
  Retained = 'Retained',
  Declined = 'Declined',
  Lost = 'Lost'
}

// Lead Source Tracking
export enum LeadSource {
  Referral = 'Client Referral',
  AttorneyReferral = 'Attorney Referral',
  Website = 'Website',
  GoogleAds = 'Google Ads',
  SocialMedia = 'Social Media',
  Avvo = 'Avvo',
  BarReferral = 'Bar Referral',
  WalkIn = 'Walk-In',
  ReturningClient = 'Returning Client',
  Other = 'Other'
}

// Task Types
export enum TaskType {
  CourtDeadline = 'Court Deadline',
  StatuteOfLimitations = 'Statute of Limitations',
  Filing = 'Filing',
  Hearing = 'Hearing',
  Trial = 'Trial',
  Deposition = 'Deposition',
  Mediation = 'Mediation',
  ClientMeeting = 'Client Meeting',
  ClientCall = 'Client Call',
  Research = 'Legal Research',
  Drafting = 'Drafting',
  DocumentReview = 'Document Review',
  InternalMeeting = 'Internal Meeting',
  FollowUp = 'Follow-Up',
  Administrative = 'Administrative'
}

// Communication Types
export enum CommunicationType {
  ClientEmail = 'Client Email',
  ClientCall = 'Client Call',
  ClientMeeting = 'Client Meeting',
  OpposingCounsel = 'Opposing Counsel',
  CourtFiling = 'Court Filing',
  InternalMemo = 'Internal Memo',
  ThirdParty = 'Third Party'
}

// Document Categories (Legal DMS Standard)
export enum DocumentCategory {
  Pleading = 'Pleading',
  Motion = 'Motion',
  Brief = 'Brief',
  Contract = 'Contract',
  Correspondence = 'Correspondence',
  Discovery = 'Discovery',
  Evidence = 'Evidence',
  CourtOrder = 'Court Order',
  Deposition = 'Deposition',
  ExpertReport = 'Expert Report',
  ClientDocument = 'Client Document',
  InternalMemo = 'Internal Memo',
  Template = 'Template',
  Other = 'Other'
}

// Document Status
export enum DocumentStatus {
  Draft = 'Draft',
  UnderReview = 'Under Review',
  Final = 'Final',
  Executed = 'Executed',
  Filed = 'Filed',
  OnLegalHold = 'Legal Hold'
}

// UTBMS Activity Codes (ABA Standard)
export enum ActivityCode {
  A101 = 'A101 - Plan and Prepare',
  A102 = 'A102 - Research',
  A103 = 'A103 - Draft/Revise',
  A104 = 'A104 - Review/Analyze',
  A105 = 'A105 - Communicate (Client)',
  A106 = 'A106 - Communicate (Other)',
  A107 = 'A107 - Appear/Attend',
  A108 = 'A108 - Manage Data/Files',
  A109 = 'A109 - Other'
}

// UTBMS Expense Codes
export enum ExpenseCode {
  E101 = 'E101 - Copying',
  E102 = 'E102 - Printing',
  E105 = 'E105 - Telephone',
  E106 = 'E106 - Online Research',
  E107 = 'E107 - Delivery',
  E108 = 'E108 - Postage',
  E109 = 'E109 - Local Travel',
  E110 = 'E110 - Out of Town Travel',
  E111 = 'E111 - Meals',
  E112 = 'E112 - Court Fees',
  E113 = 'E113 - Subpoena Fees',
  E114 = 'E114 - Witness Fees',
  E115 = 'E115 - Deposition Costs',
  E116 = 'E116 - Expert Fees',
  E117 = 'E117 - Investigation',
  E118 = 'E118 - Other'
}

// Trust Transaction Types
export enum TrustTransactionType {
  Deposit = 'Deposit',
  Withdrawal = 'Withdrawal',
  Transfer = 'Transfer',
  EarnedFees = 'Earned Fees',
  RefundToClient = 'Refund to Client'
}

// Retainer Type
export enum RetainerType {
  Standard = 'Standard',
  Evergreen = 'Evergreen',
  Flat = 'Flat Fee'
}

export enum FeeStructure {
  Hourly = 'Hourly',
  FlatFee = 'Flat Fee',
  Contingency = 'Contingency'
}


// Employee Roles (US Legal System)
export enum EmployeeRole {
  PARTNER = 'Partner',
  ASSOCIATE = 'Associate',
  OF_COUNSEL = 'OfCounsel',
  PARALEGAL = 'Paralegal',
  LEGAL_SECRETARY = 'LegalSecretary',
  LEGAL_ASSISTANT = 'LegalAssistant',
  OFFICE_MANAGER = 'OfficeManager',
  RECEPTIONIST = 'Receptionist',
  ACCOUNTANT = 'Accountant'
}

export type Permission =
  | 'system.admin' // User management, Global config
  | 'system.config' // Infrastructure settings
  | 'user.manage' // Manage staff
  | 'matter.view'
  | 'matter.create'
  | 'matter.edit'
  | 'matter.delete'
  | 'billing.view'
  | 'billing.manage' // Create/Edit invoices
  | 'billing.approve'
  // IOLTA Trust Permissions (Granular)
  | 'trust.view'
  | 'trust.deposit'
  | 'trust.withdraw'
  | 'trust.transfer'
  | 'trust.void'
  | 'trust.reconcile'
  | 'trust.approve'
  | 'trust.close_ledger'
  | 'trust.export'
  | 'trust.admin'
  | 'document.view'
  | 'document.create'
  | 'document.edit'
  | 'document.delete' // Special permission
  | 'report.financial'
  | 'calendar.manage'
  | 'task.manage'
  | 'client.manage';

export const ROLE_PERMISSIONS: Record<EmployeeRole, Permission[]> = {
  [EmployeeRole.PARTNER]: [
    'system.admin', 'system.config', 'user.manage',
    'matter.view', 'matter.create', 'matter.edit', 'matter.delete',
    'billing.view', 'billing.manage', 'billing.approve',
    'document.view', 'document.create', 'document.edit', 'document.delete',
    'report.financial', 'calendar.manage', 'task.manage', 'client.manage',
    'trust.view', 'trust.deposit', 'trust.withdraw', 'trust.transfer',
    'trust.void', 'trust.reconcile', 'trust.approve', 'trust.close_ledger',
    'trust.export', 'trust.admin'
  ],
  [EmployeeRole.ASSOCIATE]: [
    'user.manage',
    'matter.view', 'matter.create', 'matter.edit', 'matter.delete',
    'billing.view', 'billing.manage', 'billing.approve',
    'document.view', 'document.create', 'document.edit', 'document.delete',
    'report.financial', 'calendar.manage', 'task.manage', 'client.manage',
    'trust.view', 'trust.deposit', 'trust.withdraw', 'trust.transfer',
    'trust.void', 'trust.reconcile', 'trust.approve', 'trust.close_ledger',
    'trust.export', 'trust.admin'
  ],
  [EmployeeRole.OF_COUNSEL]: [
    'matter.view', 'matter.create', 'matter.edit',
    'billing.view', 'billing.manage',
    'document.view', 'document.create', 'document.edit',
    'report.financial', 'calendar.manage', 'task.manage', 'client.manage',
    'trust.view', 'trust.deposit'
  ],
  [EmployeeRole.PARALEGAL]: [
    'matter.view', 'matter.create', 'matter.edit',
    'document.view', 'document.create', 'document.edit',
    'calendar.manage', 'task.manage', 'client.manage', 'billing.view',
    'trust.view', 'trust.deposit'
  ],
  [EmployeeRole.LEGAL_SECRETARY]: [
    'matter.view', 'document.view', 'document.create',
    'calendar.manage', 'task.manage', 'client.manage',
    'trust.view'
  ],
  [EmployeeRole.LEGAL_ASSISTANT]: [
    'matter.view', 'document.view', 'document.create',
    'calendar.manage', 'task.manage',
    'trust.view'
  ],
  [EmployeeRole.OFFICE_MANAGER]: [
    'user.manage', 'matter.view', 'matter.create', 'matter.edit', 'document.view',
    'billing.view', 'billing.manage',
    'calendar.manage', 'task.manage', 'client.manage',
    'trust.view', 'trust.deposit', 'trust.withdraw'
  ],
  [EmployeeRole.RECEPTIONIST]: [
    'calendar.manage', 'client.manage', 'matter.view'
  ],
  [EmployeeRole.ACCOUNTANT]: [
    'billing.view', 'billing.manage', 'billing.approve',
    'report.financial', 'matter.view', 'document.view',
    'trust.view', 'trust.deposit', 'trust.withdraw', 'trust.reconcile', 'trust.export'
  ]
};

// Employee Status
export enum EmployeeStatus {
  ACTIVE = 'Active',
  ON_LEAVE = 'OnLeave',
  TERMINATED = 'Terminated'
}

// Bar License Status
export enum BarLicenseStatus {
  Active = 'Active',
  Inactive = 'Inactive',
  Suspended = 'Suspended',
  Pending = 'Pending'
}

export type USState = 'NY' | 'CA' | 'TX' | 'FL' | 'NJ' | 'MA' | 'DC' | string;

// Employee
export interface Employee {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  avatar?: string;
  phone?: string;
  mobile?: string;
  role: EmployeeRole;
  status: EmployeeStatus;
  hireDate: string;
  terminationDate?: string;
  hourlyRate?: number;
  salary?: number;
  userId?: string;
  notes?: string;
  address?: string;
  emergencyContact?: string;
  emergencyPhone?: string;
  supervisorId?: string;
  createdAt: string;
  updatedAt: string;

  // Bar License
  barNumber?: string;
  barJurisdiction?: string;
  barAdmissionDate?: string; // ISO String
  barStatus?: BarLicenseStatus;
  entityId?: string;
  officeId?: string;
  user?: {
    id?: string;
    avatar?: string;
    email?: string;
    name?: string;
    role?: string;
  };
}

export interface Client {
  id: string;
  clientNumber?: string;  // CLT-0001, CLT-0002, etc.
  name: string;
  email: string;
  phone?: string;
  mobile?: string;
  company?: string;
  type: 'Individual' | 'Corporate';
  status: 'Active' | 'Inactive';
  portalEnabled?: boolean;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  country?: string;
  taxId?: string;
  notes?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface Lead {
  id: string;
  name: string;
  email?: string;
  phone?: string;
  source: string;
  status: 'New' | 'Contacted' | 'Consultation' | 'Retained' | 'Lost';
  estimatedValue: number;
  practiceArea: PracticeArea;
}

export interface OpposingParty {
  id: string;
  matterId: string;
  name: string;
  type: 'Individual' | 'Corporation' | 'LLC' | 'Partnership' | 'Government' | 'Other';
  company?: string;
  taxId?: string;
  incorporationState?: string;
  counselName?: string;
  counselFirm?: string;
  counselEmail?: string;
  counselPhone?: string;
  counselAddress?: string;
  notes?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface Matter {
  id: string;
  caseNumber: string;
  name: string;
  clientId?: string;
  client?: Client;
  practiceArea: PracticeArea;
  status: CaseStatus;
  feeStructure: FeeStructure; // Added
  openDate: string;
  responsibleAttorney: string;
  billableRate: number;
  trustBalance: number;
  courtType?: string;
  entityId?: string;
  officeId?: string;
  bailStatus?: 'None' | 'Set' | 'Posted' | 'Forfeited' | 'Exonerated' | 'Returned';
  bailAmount?: number;
  outcome?: string;
  timeEntries?: TimeEntry[];
  expenses?: Expense[];
  events?: CalendarEvent[];
}

export interface TimeEntry {
  id: string;
  matterId?: string;
  description: string;
  duration: number; // in minutes
  rate: number;
  date: string;
  billed: boolean;
  isBillable?: boolean;
  type: 'time';
  activityCode?: string;
  taskCode?: string;
  approvalStatus?: string;
  submittedBy?: string;
  submittedAt?: string;
  approvedBy?: string;
  approvedAt?: string;
  rejectedBy?: string;
  rejectedAt?: string;
  rejectionReason?: string;
}

export interface Expense {
  id: string;
  matterId?: string;
  description: string;
  amount: number;
  date: string;
  category: 'Court Fee' | 'Travel' | 'Printing' | 'Research' | 'Expert' | 'Courier' | 'Other';
  billed: boolean;
  type: 'expense';
  expenseCode?: string;
  approvalStatus?: string;
  submittedBy?: string;
  submittedAt?: string;
  approvedBy?: string;
  approvedAt?: string;
  rejectedBy?: string;
  rejectedAt?: string;
  rejectionReason?: string;
}

// Fatura Durumu
export enum InvoiceStatus {
  DRAFT = 'DRAFT',
  PENDING_APPROVAL = 'PENDING_APPROVAL',
  APPROVED = 'APPROVED',
  SENT = 'SENT',
  PARTIALLY_PAID = 'PARTIALLY_PAID',
  PAID = 'PAID',
  OVERDUE = 'OVERDUE',
  WRITTEN_OFF = 'WRITTEN_OFF',
  CANCELLED = 'CANCELLED'
}

// Fatura Kalemi Tipi
export enum LineItemType {
  TIME = 'TIME',
  EXPENSE = 'EXPENSE',
  FIXED_FEE = 'FIXED_FEE',
  DISCOUNT = 'DISCOUNT',
  TAX = 'TAX',
  WRITE_OFF = 'WRITE_OFF',
  RETAINER = 'RETAINER',
  COURT_FEE = 'COURT_FEE',
  OTHER = 'OTHER'
}

// Fatura Kalemi
export interface InvoiceLineItem {
  id: string;
  invoiceId: string;
  type: LineItemType;
  description: string;
  quantity: number;
  rate: number;
  amount: number;
  utbmsActivityCode?: string;
  utbmsExpenseCode?: string;
  utbmsTaskCode?: string;
  timeEntryId?: string;
  expenseId?: string;
  taxable: boolean;
  billable: boolean;
  writtenOff: boolean;
  date: string;
}

// Invoice Payment
export interface InvoicePayment {
  id: string;
  invoiceId: string;
  amount: number;
  method: 'cash' | 'check' | 'credit_card' | 'bank_transfer' | 'trust';
  reference?: string;
  stripePaymentId?: string;
  isRefund: boolean;
  refundReason?: string;
  notes?: string;
  paidAt: string;
}

export interface Invoice {
  id: string;
  number: string;
  client: Client;
  clientId: string;
  matterId?: string;
  entityId?: string;
  officeId?: string;

  // Tutarlar
  subtotal: number;
  taxRate?: number;
  taxAmount: number;
  discount: number;
  amount: number;
  amountPaid: number;
  balance: number;

  // Tarihler
  issueDate: string;
  dueDate: string;
  paidDate?: string;

  // Workflow
  status: InvoiceStatus;
  approvedBy?: string;
  approvedAt?: string;
  sentAt?: string;

  // LEDES
  ledesCode?: string;

  // Alt tablolar
  lineItems?: InvoiceLineItem[];
  payments?: InvoicePayment[];

  // Notlar
  notes?: string;
  terms?: string;

  createdAt?: string;
  updatedAt?: string;
}

export type TaskStatus = 'To Do' | 'In Progress' | 'Review' | 'Done' | 'Archived';
export type TaskOutcome = 'success' | 'failed' | 'cancelled';

export interface Task {
  id: string;
  title: string;
  description?: string;
  startDate?: string;        // When task was started
  dueDate?: string;
  reminderAt?: string;
  priority: 'High' | 'Medium' | 'Low';
  status: TaskStatus;
  isCompleted?: boolean;     // Quick completion flag
  outcome?: TaskOutcome;     // for completed tasks
  matterId?: string;
  assignedTo?: string;       // Initials
  templateId?: string;
  completedAt?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface TaskTemplate {
  id: string;
  name: string;
  category?: string;
  description?: string;
  definition: string; // JSON string
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CalendarEvent {
  id: string;
  title: string;
  date: string;
  type: 'Court' | 'Meeting' | 'Deadline' | 'Deposition' | 'Consultation' | 'Filing' | 'Hearing' | 'Trial' | 'Conference' | 'Other';
  matterId?: string;
  description?: string;       // Event description (optional)
  location?: string;          // Event location (optional)
  recurrencePattern?: 'none' | 'daily' | 'weekly' | 'monthly'; // Recurrence (optional)
  reminderMinutes?: number;   // Minutes before reminder (0, 15, 30, 60, 120, 1440)
  duration?: number;          // Duration in minutes
  reminderSent?: boolean;     // Has reminder been sent
}

export interface DocumentFile {
  id: string;
  name: string;
  type: 'pdf' | 'docx' | 'txt' | 'folder' | 'img';
  size?: string;
  fileSize?: number;
  updatedAt: string;
  matterId?: string;
  content?: string; // optional data URL for inline open/download
  filePath?: string; // server file path for uploaded documents
  description?: string;
  tags?: string[];
  category?: string;
  status?: string; // DocumentStatus enum value
  version?: number;
  uploadedBy?: string;
  legalHoldReason?: string;
  legalHoldPlacedAt?: string;
  legalHoldReleasedAt?: string;
  legalHoldPlacedBy?: string;
  permissions?: DocumentPermissions;
}

export interface DocumentPermissions {
  canView?: boolean;
  canDownload?: boolean;
  canComment?: boolean;
  canUpload?: boolean;
  sharedAt?: string;
  expiresAt?: string;
}

export interface Message {
  id: string;
  from: string;
  subject: string;
  preview: string;
  date: string;
  read: boolean;
  matterId?: string;
}

export interface StaffMessage {
  id: string;
  senderId: string;
  recipientId: string;
  body: string;
  status: 'Unread' | 'Read';
  createdAt: string;
  readAt?: string;
  senderName?: string;
  recipientName?: string;
  attachmentsJson?: string;
  attachments?: {
    fileName: string;
    filePath: string;
    mimeType: string;
    size: number;
  }[];
}

export interface AIRequest {
  prompt: string;
  tone: 'Professional' | 'Aggressive' | 'Empathetic' | 'Academic' | 'Persuasive';
  context?: string;
  docType?: 'Motion' | 'Email' | 'Memo' | 'Contract' | 'Letter';
}

export interface Notification {
  id: string;
  userId: string;
  title: string;
  message: string;
  type: 'info' | 'warning' | 'error' | 'success';
  read: boolean;
  link?: string;
  createdAt: string;
}

export interface AuditLogEntry {
  id: string;
  userId?: string | null;
  userEmail?: string | null;
  clientId?: string | null;
  clientEmail?: string | null;
  action: string;
  entityType: string;
  entityId?: string | null;
  oldValues?: any | null;
  newValues?: any | null;
  oldValuesRaw?: string | null;
  newValuesRaw?: string | null;
  details?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  createdAt: string;
}

export interface AuditLogListResponse {
  page: number;
  limit: number;
  total: number;
  items: AuditLogEntry[];
}

// ========== V2.0 NEW TYPES ==========

export interface TrustTransaction {
  id: string;
  matterId: string;
  type: 'deposit' | 'withdrawal' | 'transfer' | 'refund';
  amount: number;
  description: string;
  reference?: string;
  balance: number;
  createdBy?: string;
  createdAt: string;
}

export interface AppointmentRequest {
  id: string;
  clientId: string;
  matterId?: string;
  requestedDate: string;
  duration: number;
  type: 'consultation' | 'meeting' | 'call' | 'court';
  notes?: string;
  status: 'pending' | 'approved' | 'rejected' | 'cancelled';
  assignedTo?: string;
  approvedDate?: string;
  createdAt: string;
}

export interface Workflow {
  id: string;
  name: string;
  description?: string;
  trigger: 'matter_created' | 'status_changed' | 'deadline_approaching' | 'task_completed' | 'invoice_created';
  triggerConfig?: string;
  actions: string;
  isActive: boolean;
  runCount: number;
  lastRunAt?: string;
  createdBy?: string;
  createdAt: string;
}

export interface WorkflowExecution {
  id: string;
  workflowId: string;
  triggeredBy: string;
  status: 'success' | 'failed' | 'partial';
  actionsRun?: string;
  error?: string;
  executedAt: string;
}

export interface SignatureRequest {
  id: string;
  documentId: string;
  clientId: string;
  status: 'pending' | 'signed' | 'declined' | 'expired';
  signedAt?: string;
  signatureData?: string;
  ipAddress?: string;
  expiresAt?: string;
  createdAt: string;
}

export interface IntakeForm {
  id: string;
  name: string;
  description?: string;
  fields: string;
  practiceArea?: string;
  isActive: boolean;
  createdBy?: string;
  createdAt: string;
}

export interface IntakeSubmission {
  id: string;
  formId: string;
  data: string;
  status: 'new' | 'reviewed' | 'converted' | 'rejected';
  convertedToClientId?: string;
  convertedToMatterId?: string;
  reviewedBy?: string;
  reviewedAt?: string;
  notes?: string;
  createdAt: string;
}

export interface SettlementStatement {
  id: string;
  matterId: string;
  grossSettlement: number;
  attorneyFees: number;
  expenses: number;
  liens?: number;
  netToClient: number;
  breakdown?: string;
  status: 'draft' | 'sent' | 'approved' | 'disputed';
  clientApprovedAt?: string;
  pdfPath?: string;
  createdAt: string;
}

export interface Payment {
  id: string;
  invoiceId: string;
  amount: number;
  currency: string;
  status: 'pending' | 'succeeded' | 'failed' | 'refunded';
  stripePaymentId?: string;
  paymentMethod?: string;
  last4?: string;
  brand?: string;
  receiptUrl?: string;
  paidAt?: string;
  paymentPlanId?: string;
  scheduledFor?: string;
  source?: string;
  createdAt: string;
}

// Document Version
export interface DocumentVersion {
  id: string;
  documentId: string;
  versionNumber: number;
  fileName: string;
  filePath: string;
  fileSize: number;
  mimeType: string;
  changeNote?: string;
  changedBy?: string;
  checksum?: string;
  diffSummary?: string;
  isLatest: boolean;
  createdAt: string;
}

// Son Tarih Kural Tipi
export enum DeadlineRuleType {
  COURT_FILING = 'COURT_FILING',
  RESPONSE_DUE = 'RESPONSE_DUE',
  DISCOVERY = 'DISCOVERY',
  MOTION = 'MOTION',
  APPEAL = 'APPEAL',
  STATUTE_OF_LIMIT = 'STATUTE_OF_LIMIT',
  CONTRACT = 'CONTRACT',
  CUSTOM = 'CUSTOM'
}

// Deadline Rule
export interface DeadlineRule {
  id: string;
  name: string;
  description?: string;
  type: DeadlineRuleType;
  baseDays: number;
  useBusinessDays: boolean;
  excludeHolidays: boolean;
  triggerEvent?: string;
  jurisdiction?: string;
  practiceArea?: string;
  reminderDays?: string; // JSON: [30, 7, 1]
  isActive: boolean;
  priority: number;
  createdBy?: string;
  createdAt: string;
}

// Calculated Deadline
export interface CalculatedDeadline {
  id: string;
  ruleId: string;
  rule?: DeadlineRule;
  matterId: string;
  triggerDate: string;
  dueDate: string;
  reminder30Sent: boolean;
  reminder7Sent: boolean;
  reminder1Sent: boolean;
  status: 'pending' | 'completed' | 'overdue';
  completedAt?: string;
  notes?: string;
  createdAt: string;
}

export interface ActiveTimer {
  startTime: number; // timestamp
  matterId?: string;
  description: string;
  isRunning: boolean;
  elapsed: number; // saved elapsed time if paused
  rate?: number;
  activityCode?: string;
  taskCode?: string;
  isBillable?: boolean;
}

// ==============================
// IOLTA TRUST ACCOUNTING TYPES
// ABA Model Rule 1.15 Compliant
// ==============================

// Trust Account Status
export enum TrustAccountStatus {
  ACTIVE = 'ACTIVE',
  INACTIVE = 'INACTIVE',
  CLOSED = 'CLOSED'
}

// Client Ledger Status
export enum LedgerStatus {
  ACTIVE = 'ACTIVE',
  CLOSED = 'CLOSED',
  FROZEN = 'FROZEN'
}

// Trust Transaction Status
export enum TrustTxStatus {
  PENDING = 'PENDING',
  APPROVED = 'APPROVED',
  REJECTED = 'REJECTED',
  VOIDED = 'VOIDED'
}

// Trust Transaction Type (V2)
export enum TrustTransactionTypeV2 {
  DEPOSIT = 'DEPOSIT',
  WITHDRAWAL = 'WITHDRAWAL',
  TRANSFER_IN = 'TRANSFER_IN',
  TRANSFER_OUT = 'TRANSFER_OUT',
  REFUND_TO_CLIENT = 'REFUND_TO_CLIENT',
  FEE_EARNED = 'FEE_EARNED',
  INTEREST = 'INTEREST'
}

// Trust Bank Account (IOLTA Account)
export interface TrustBankAccount {
  id: string;
  name: string;
  bankName: string;
  accountNumberEnc: string; // Encrypted
  routingNumber: string;
  jurisdiction: string; // State code
  currentBalance: number;
  status: TrustAccountStatus;
  entityId?: string;
  officeId?: string;
  createdAt: string;
  updatedAt: string;
  closedAt?: string;
  closedBy?: string;
}

// Client Trust Ledger (Subsidiary Ledger)
export interface ClientTrustLedger {
  id: string;
  clientId: string;
  client?: Client;
  matterId?: string;
  trustAccountId: string;
  trustAccount?: TrustBankAccount;
  runningBalance: number;
  status: LedgerStatus;
  entityId?: string;
  officeId?: string;
  notes?: string;
  createdAt: string;
  updatedAt: string;
  closedAt?: string;
  closedBy?: string;
  closedReason?: string;
}

// Trust Transaction V2 (IOLTA Compliant - Immutable)
export interface TrustTransactionV2 {
  id: string;
  trustAccountId: string;
  trustAccount?: TrustBankAccount;
  type: TrustTransactionTypeV2;
  amount: number;
  entityId?: string;
  officeId?: string;

  // Immutability
  isVoided: boolean;
  voidedAt?: string;
  voidedBy?: string;
  voidReason?: string;
  originalTxId?: string;

  // Required Metadata
  description: string;
  payorPayee: string;
  checkNumber?: string;
  wireReference?: string;

  // Approval Workflow
  status: TrustTxStatus;
  createdBy: string;
  approvedBy?: string;
  approvedAt?: string;
  rejectedBy?: string;
  rejectedAt?: string;
  rejectionReason?: string;

  // IOLTA Tracking
  isEarned: boolean;
  earnedDate?: string;

  // Balance Snapshots
  accountBalanceBefore: number;
  accountBalanceAfter: number;

  createdAt: string;

  // Relations
  allocations?: TrustAllocationLine[];
}

// Trust Allocation Line (Split deposits/withdrawals)
export interface TrustAllocationLine {
  id: string;
  transactionId: string;
  transaction?: TrustTransactionV2;
  ledgerId: string;
  ledger?: ClientTrustLedger;
  amount: number; // Positive = deposit, Negative = withdrawal
  description?: string;
  ledgerBalanceAfter: number;
  createdAt: string;
}

// Earned Fee Event (Trust -> Operating)
export interface EarnedFeeEvent {
  id: string;
  invoiceId: string;
  ledgerId: string;
  ledger?: ClientTrustLedger;
  trustTxId: string;
  trustTx?: TrustTransactionV2;
  amount: number;
  approvedBy: string;
  approvedAt: string;
  operatingReference?: string;
  notes?: string;
  createdAt: string;
}

// Reconciliation Record (Three-Way)
export interface ReconciliationRecord {
  id: string;
  trustAccountId: string;
  trustAccount?: TrustBankAccount;
  periodStart: string;
  periodEnd: string;

  // Three-Way Balances
  bankStatementBalance: number;
  trustLedgerBalance: number;
  clientLedgerSumBalance: number;

  // Status
  isReconciled: boolean;
  discrepancyAmount?: number;

  // Outstanding Items
  outstandingChecks?: string; // JSON
  depositsInTransit?: string; // JSON
  otherAdjustments?: string; // JSON

  exceptions?: string; // JSON
  notes?: string;

  // Prepared/Approved
  preparedBy: string;
  preparedAt: string;
  approvedBy?: string;
  approvedAt?: string;

  createdAt: string;
}

// Trust Audit Log (Immutable)
export interface TrustAuditLog {
  id: string;
  userId?: string;
  userEmail?: string;
  userRole?: string;
  ipAddress?: string;
  userAgent?: string;
  action: string;
  entityType: string;
  entityId?: string;
  previousState?: string; // JSON
  newState?: string; // JSON
  metadata?: string; // JSON
  amount?: number;
  trustAccountId?: string;
  clientLedgerId?: string;
  transactionId?: string;
  createdAt: string;
}

// Jurisdiction Config
export interface JurisdictionConfig {
  id: string;
  stateCode: string;
  stateName: string;
  interestToBar: boolean;
  interestRate?: number;
  retentionYears: number;
  reportingFreq: 'ANNUAL' | 'QUARTERLY';
  minBalanceForInterest?: number;
  specialRules?: string; // JSON
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// Trust-specific Permissions
export type TrustPermission =
  | 'trust.view'
  | 'trust.deposit'
  | 'trust.withdraw'
  | 'trust.transfer'
  | 'trust.void'
  | 'trust.reconcile'
  | 'trust.approve'
  | 'trust.close_ledger'
  | 'trust.export'
  | 'trust.admin';

// Trust Operation Result
export interface TrustOperationResult {
  success: boolean;
  error?: string;
  message?: string;
  transactionId?: string;
  ledgerId?: string;
  newBalance?: number;
}

// Reconciliation Input
export interface ReconciliationInput {
  trustAccountId: string;
  periodEnd: string;
  bankStatementBalance: number;
  outstandingChecks?: { checkNumber: string; amount: number; date: string }[];
  depositsInTransit?: { reference: string; amount: number; date: string }[];
  notes?: string;
}

// Trust Deposit Request
export interface TrustDepositRequest {
  trustAccountId: string;
  amount: number;
  payorPayee: string;
  description: string;
  checkNumber?: string;
  wireReference?: string;
  allocations: {
    ledgerId: string;
    amount: number;
    description?: string;
  }[];
}

// Trust Withdrawal Request
export interface TrustWithdrawalRequest {
  trustAccountId: string;
  ledgerId: string;
  amount: number;
  payorPayee: string;
  description: string;
  checkNumber?: string;
}

// ============================================================
// STAFF & SETTINGS ENHANCEMENT TYPES
// ============================================================



// Billing Settings Interface
export interface BillingSettings {
  // Default Rates
  defaultHourlyRate: number;
  partnerRate: number;
  associateRate: number;
  paralegalRate: number;

  // Time Entry
  billingIncrement: 6 | 10 | 15;
  minimumTimeEntry: number;
  roundingRule: 'up' | 'down' | 'nearest';

  // Invoice
  defaultPaymentTerms: number;
  invoicePrefix: string;
  defaultTaxRate: number;

  // LEDES/UTBMS
  ledesEnabled: boolean;
  utbmsCodesRequired: boolean;

  // Trust
  evergreenRetainerMinimum: number;
  trustBalanceAlerts: boolean;
}

// Security Settings Interface
export interface SecuritySettings {
  // Password Policy
  minPasswordLength: number;
  requireUppercase: boolean;
  requireNumbers: boolean;
  requireSpecialChars: boolean;
  passwordExpiryDays: number;

  // MFA
  mfaEnabled: boolean;

  // Session
  sessionTimeoutMinutes: number;

  // Audit
  auditLoggingEnabled: boolean;
}

export type IntegrationStatus = 'connected' | 'pending' | 'disabled' | 'error';

export interface IntegrationCatalogItem {
  providerKey: string;
  provider: string;
  category: 'Accounting' | 'Calendar' | 'Payments' | 'Email' | string;
  description: string;
  connectionMode?: string;
  supportsSync?: boolean;
  supportsWebhook?: boolean;
  webhookFirst?: boolean;
  fallbackPollingMinutes?: number;
  supportedActions?: string[];
  capabilities?: string[];
}

export interface IntegrationItem {
  id: string;
  providerKey?: string;
  provider: string;
  category: 'Accounting' | 'Calendar' | 'Payments' | 'Email' | string;
  status: IntegrationStatus;
  accountLabel?: string;
  accountEmail?: string;
  syncEnabled?: boolean;
  lastSyncAt?: string;
  lastWebhookAt?: string;
  lastWebhookEventId?: string;
  notes?: string;
}

export interface IntegrationCanonicalContractDescriptor {
  version: string;
  actions: string[];
  conflictPolicies: string[];
  reviewQueueOpenStatuses: string[];
  conflictQueueOpenStatuses: string[];
  eventStatuses: {
    inbox: string[];
    outbox: string[];
  };
}

export interface IntegrationCapabilityMatrixRow {
  providerKey: string;
  provider: string;
  category: string;
  connectionMode?: string;
  supportsWebhook: boolean;
  webhookFirst: boolean;
  fallbackPollingMinutes?: number | null;
  supportedActions: string[];
  capabilities: string[];
  connectionId?: string | null;
  connectionStatus?: string | null;
  syncEnabled: boolean;
  lastSyncAt?: string | null;
  lastWebhookAt?: string | null;
  mappingProfileCount: number;
  openConflictCount: number;
  openReviewCount: number;
  pendingInboxEventCount: number;
  pendingOutboxEventCount: number;
  lastRunStatus?: string | null;
  lastRunAt?: string | null;
  lastRunErrorCode?: string | null;
  gaps: string[];
}

export interface IntegrationCapabilityMatrixResponse {
  generatedAt: string;
  rows: IntegrationCapabilityMatrixRow[];
}

export interface IntegrationMappingProfile {
  id: string;
  connectionId: string;
  providerKey: string;
  profileKey: string;
  name: string;
  entityType: string;
  direction: string;
  status: string;
  conflictPolicy: string;
  isDefault: boolean;
  version: number;
  fieldMappingsJson?: string | null;
  enumMappingsJson?: string | null;
  taxMappingsJson?: string | null;
  accountMappingsJson?: string | null;
  defaultsJson?: string | null;
  metadataJson?: string | null;
  validationSummary?: string | null;
  lastValidatedAt?: string | null;
  updatedBy?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertIntegrationMappingProfileRequest {
  name: string;
  entityType: string;
  direction?: string;
  status?: string;
  conflictPolicy?: string;
  isDefault?: boolean;
  fieldMappingsJson?: string | null;
  enumMappingsJson?: string | null;
  taxMappingsJson?: string | null;
  accountMappingsJson?: string | null;
  defaultsJson?: string | null;
  metadataJson?: string | null;
  validationSummary?: string | null;
  lastValidatedAt?: string | null;
}

export interface RunCanonicalIntegrationActionRequest {
  entityType?: string;
  localEntityId?: string;
  externalEntityId?: string;
  correlationId?: string;
  idempotencyKey?: string;
  cursor?: string;
  deltaToken?: string;
  payloadJson?: string;
  dryRun?: boolean;
  requiresReview?: boolean;
}

export interface CanonicalIntegrationActionResult {
  success: boolean;
  retryable: boolean;
  action: string;
  status: string;
  message?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  nextCursor?: string | null;
  nextDeltaToken?: string | null;
  readCount: number;
  writeCount: number;
  conflictCount: number;
  reviewCount: number;
  resultJson?: string | null;
}

export interface IntegrationConflictQueueItem {
  id: string;
  connectionId?: string | null;
  runId?: string | null;
  providerKey: string;
  entityType: string;
  localEntityId?: string | null;
  externalEntityId?: string | null;
  conflictType: string;
  severity: string;
  status: string;
  mappingProfileId?: string | null;
  fingerprint?: string | null;
  assignedTo?: string | null;
  resolutionType?: string | null;
  summary?: string | null;
  localSnapshotJson?: string | null;
  externalSnapshotJson?: string | null;
  suggestedResolutionJson?: string | null;
  resolutionJson?: string | null;
  reviewNotes?: string | null;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  resolvedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ResolveIntegrationConflictRequest {
  status?: string;
  resolutionType: string;
  resolutionJson?: string | null;
  notes?: string | null;
}

export interface IntegrationReviewQueueItem {
  id: string;
  connectionId?: string | null;
  runId?: string | null;
  providerKey: string;
  itemType: string;
  sourceId?: string | null;
  sourceType?: string | null;
  conflictId?: string | null;
  status: string;
  priority: string;
  title?: string | null;
  summary?: string | null;
  contextJson?: string | null;
  suggestedActionsJson?: string | null;
  decision?: string | null;
  decisionNotes?: string | null;
  assignedTo?: string | null;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  dueAt?: string | null;
  resolvedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface DecideIntegrationReviewItemRequest {
  status?: string;
  decision: string;
  notes?: string | null;
}

export interface IntegrationInboxEventListItem {
  id: string;
  connectionId?: string | null;
  runId?: string | null;
  providerKey: string;
  externalEventId: string;
  status: string;
  signatureValidated: boolean;
  payloadHash?: string | null;
  replayCount: number;
  errorMessage?: string | null;
  receivedAt: string;
  processedAt?: string | null;
}

export interface ReplayIntegrationInboxEventResponse {
  id: string;
  providerKey: string;
  replayCount: number;
  runId?: string | null;
  success: boolean;
  status: string;
  message?: string | null;
  deduplicated?: boolean;
}

export interface IntegrationOutboxEventListItem {
  id: string;
  connectionId?: string | null;
  runId?: string | null;
  providerKey: string;
  eventType: string;
  entityType?: string | null;
  entityId?: string | null;
  idempotencyKey: string;
  status: string;
  attemptCount: number;
  nextAttemptAt?: string | null;
  dispatchedAt?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  deadLettered: boolean;
  createdAt: string;
}

export interface ReplayIntegrationOutboxEventResponse {
  id: string;
  providerKey: string;
  eventType: string;
  status: string;
  attemptCount: number;
  nextAttemptAt?: string | null;
}

export interface IntegrationRunListItem {
  id: string;
  connectionId?: string | null;
  providerKey: string;
  trigger: string;
  status: string;
  attemptCount: number;
  maxAttempts?: number | null;
  idempotencyKey?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  isDeadLetter: boolean;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface ReplayIntegrationRunResponse {
  sourceRunId: string;
  replayRunId: string;
  success: boolean;
  status: string;
  message?: string | null;
  syncedCount?: number;
  attemptCount?: number;
  isDeadLetter?: boolean;
  deduplicated?: boolean;
}

export interface IntegrationSecretStoreStatus {
  providerMode: string;
  encryptionProviderId?: string | null;
  legacyPlaintextAllowed: boolean;
  keyRingSource?: string | null;
  activeKeyId?: string | null;
  configuredKeyCount: number;
  rotationEnabled: boolean;
  rotationIntervalMinutes: number;
  entries: {
    total: number;
    byKey: Array<{
      encryptionProvider?: string | null;
      encryptionKeyId?: string | null;
      count: number;
    }>;
  };
  scopeMatrix: Array<{
    scope: string;
    read: boolean;
    write: boolean;
    delete: boolean;
    rotate: boolean;
  }>;
}

export interface RotateIntegrationSecretsResponse {
  rotated: number;
  executedAt: string;
}

export type AppDirectoryListingStatus =
  | 'draft'
  | 'changes_requested'
  | 'in_review'
  | 'approved'
  | 'published'
  | 'rejected'
  | 'suspended'
  | string;

export interface AppDirectoryManifest {
  providerKey: string;
  name: string;
  category: string;
  connectionMode: 'oauth' | 'api_key' | 'hybrid' | string;
  summary: string;
  description?: string;
  manifestVersion: string;
  websiteUrl?: string;
  documentationUrl?: string;
  supportEmail?: string;
  supportUrl?: string;
  logoUrl?: string;
  supportsWebhook: boolean;
  webhookFirst: boolean;
  fallbackPollingMinutes?: number;
  capabilities: string[];
  configurationHints?: Record<string, string>;
}

export interface AppDirectorySlaProfile {
  tier: string;
  responseHours?: number;
  resolutionHours?: number;
  uptimePercent?: number;
}

export interface AppDirectoryHarnessCheck {
  key: string;
  severity: string;
  passed: boolean;
  message: string;
}

export interface AppDirectoryHarnessResult {
  passed: boolean;
  errorCount: number;
  warningCount: number;
  summary: string;
  checks: AppDirectoryHarnessCheck[];
}

export interface AppDirectoryListing {
  id: string;
  providerKey: string;
  name: string;
  category: string;
  connectionMode: string;
  summary: string;
  description?: string;
  manifestVersion: string;
  websiteUrl?: string;
  documentationUrl?: string;
  supportEmail?: string;
  supportUrl?: string;
  logoUrl?: string;
  supportsWebhook: boolean;
  webhookFirst: boolean;
  fallbackPollingMinutes?: number;
  slaTier: string;
  slaResponseHours?: number;
  slaResolutionHours?: number;
  slaUptimePercent?: number;
  status: AppDirectoryListingStatus;
  submissionCount: number;
  lastSubmittedAt?: string;
  lastTestStatus: string;
  lastTestedAt?: string;
  lastTestSummary?: string;
  reviewNotes?: string;
  reviewedBy?: string;
  reviewedAt?: string;
  isFeatured: boolean;
  publishedAt?: string;
  updatedAt: string;
}

export interface AppDirectorySubmission {
  id: string;
  listingId: string;
  submittedBy: string;
  status: string;
  testStatus: string;
  startedAt?: string;
  completedAt?: string;
  createdAt: string;
}

export interface AppDirectoryOnboardingRequest {
  manifest: AppDirectoryManifest;
  sla?: AppDirectorySlaProfile;
}

export interface AppDirectoryOnboardingResponse {
  listing: AppDirectoryListing;
  harness: AppDirectoryHarnessResult;
}

export interface AppDirectoryReviewRequest {
  decision: 'approve' | 'reject' | 'request_changes' | 'suspend' | string;
  publish?: boolean;
  isFeatured?: boolean;
  notes?: string;
  sla?: AppDirectorySlaProfile;
}

export interface FirmEntity {
  id: string;
  name: string;
  legalName?: string;
  taxId?: string;
  email?: string;
  phone?: string;
  website?: string;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  country?: string;
  isDefault: boolean;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string;
  officeCount?: number;
}

export interface Office {
  id: string;
  entityId: string;
  name: string;
  code?: string;
  email?: string;
  phone?: string;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  country?: string;
  timeZone?: string;
  isDefault: boolean;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string;
}

// Firm Settings Interface
export interface FirmSettings {
  firmName: string;
  taxId: string;
  ledesFirmId: string;
  address: string;
  city: string;
  state: string;
  zipCode: string;
  phone: string;
  website?: string;
}

// Staff Performance Metrics
export interface StaffPerformanceMetrics {
  employeeId: string;
  yearToDateHours: number;
  annualTarget: number;
  utilizationRate: number;
  billableAmount: number;
  collectedAmount: number;
}

export interface PaymentPlan {
  id: string;
  clientId: string;
  invoiceId?: string;
  name: string;
  totalAmount: number;
  installmentAmount: number;
  frequency: string;
  startDate: string;
  nextRunDate: string;
  endDate?: string;
  remainingAmount: number;
  status: string;
  autoPayEnabled: boolean;
  autoPayMethod?: string;
  autoPayReference?: string;
  createdAt: string;
  updatedAt: string;
}

export interface EfilingWorkspaceDocument {
  id: string;
  name?: string | null;
  fileName: string;
  fileSize: number;
  mimeType?: string | null;
  category?: string | null;
  tags?: string | null;
  status?: string | null;
  updatedAt: string;
}

export interface EfilingWorkspaceDocket {
  id: string;
  providerKey: string;
  externalDocketId: string;
  docketNumber?: string | null;
  caseName?: string | null;
  court?: string | null;
  sourceUrl?: string | null;
  filedAt?: string | null;
  modifiedAt?: string | null;
  lastSeenAt: string;
}

export interface EfilingWorkspaceSubmission {
  id: string;
  providerKey: string;
  externalSubmissionId: string;
  externalDocketId?: string | null;
  referenceNumber?: string | null;
  status: string;
  submittedAt?: string | null;
  acceptedAt?: string | null;
  rejectedAt?: string | null;
  rejectionReason?: string | null;
  updatedAt: string;
}

export interface EfilingWorkspaceConnection {
  id: string;
  providerKey: string;
  provider: string;
  status: string;
  accountLabel?: string | null;
  lastSyncAt?: string | null;
  lastWebhookAt?: string | null;
}

export interface EfilingWorkspaceCourtRule {
  id: string;
  name: string;
  jurisdiction?: string | null;
  courtType?: string | null;
  triggerEvent: string;
  citation?: string | null;
  daysCount: number;
  dayType: string;
  direction: string;
  serviceDaysAdd: number;
  extendIfWeekend: boolean;
}

export interface EfilingWorkspaceResponse {
  matter: {
    id: string;
    name: string;
    caseNumber: string;
    status: string;
    courtType?: string | null;
  };
  providerKey?: string | null;
  documents: EfilingWorkspaceDocument[];
  dockets: EfilingWorkspaceDocket[];
  submissions: EfilingWorkspaceSubmission[];
  connections: EfilingWorkspaceConnection[];
  courtRules: EfilingWorkspaceCourtRule[];
  suggestedPacket?: {
    packetName?: string | null;
    suggestedFilingType?: string | null;
    suggestedDocumentIds?: string[];
  } | null;
}

export interface EfilingPrecheckIssue {
  code: string;
  message: string;
}

export interface EfilingPrecheckResponse {
  canSubmit: boolean;
  matter?: {
    id: string;
    name: string;
    caseNumber: string;
    courtType?: string | null;
    status: string;
  };
  providerKey?: string | null;
  packetName?: string | null;
  filingType?: string | null;
  documents: Array<{
    id: string;
    name?: string | null;
    fileName?: string | null;
    fileSize: number;
    mimeType?: string | null;
    category?: string | null;
    tags?: string | null;
  }>;
  matchedRules: EfilingWorkspaceCourtRule[];
  suggestedDeadlines: Array<{
    ruleId: string;
    ruleName: string;
    triggerEvent?: string | null;
    dueDateUtc: string;
    priority?: string | null;
  }>;
  errors: EfilingPrecheckIssue[];
  warnings: EfilingPrecheckIssue[];
}

export interface EfilingSubmissionTimelineResponse {
  submission: EfilingWorkspaceSubmission & {
    matterId?: string | null;
    lastSeenAt?: string | null;
  };
  timeline: Array<{
    timestampUtc: string;
    eventType: string;
    source?: string | null;
    title?: string | null;
    summary?: string | null;
    status?: string | null;
    [key: string]: unknown;
  }>;
}

export interface EfilingSubmissionTransitionResponse {
  submissionId: string;
  previousStatus?: string | null;
  currentStatus: string;
  message?: string | null;
}

export interface EfilingDocketAutomationResponse {
  processed: number;
  tasksCreated: number;
  deadlinesCreated: number;
  reviewsQueued: number;
}

// Outcome-to-Fee Planner (Phase 0/1)
export interface OutcomeFeePlan {
  id: string;
  matterId: string;
  clientId?: string | null;
  matterBillingPolicyId?: string | null;
  currentVersionId?: string | null;
  plannerMode: string;
  status: string;
  correlationId?: string | null;
  createdBy?: string | null;
  updatedBy?: string | null;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeePlanVersion {
  id: string;
  planId: string;
  versionNumber: number;
  status: string;
  plannerMode: string;
  modelVersion: string;
  assumptionSetVersion: string;
  correlationId?: string | null;
  matterBillingPolicyId?: string | null;
  rateCardId?: string | null;
  currency: string;
  generatedBy?: string | null;
  generatedAt: string;
  createdAt: string;
  updatedAt: string;
  sourceSignalsJson?: string | null;
  inputSnapshotJson?: string | null;
  summaryJson?: string | null;
  metadataJson?: string | null;
}

export interface OutcomeFeeScenario {
  id: string;
  planVersionId: string;
  scenarioKey: string;
  name: string;
  probability: number;
  currency: string;
  budgetTotal: number;
  expectedCollected: number;
  expectedCost: number;
  expectedMargin: number;
  confidenceScore?: number | null;
  status: string;
  driverSummary?: string | null;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeePhaseForecast {
  id: string;
  scenarioId: string;
  phaseOrder: number;
  phaseCode: string;
  name: string;
  hoursExpected: number;
  feeExpected: number;
  expenseExpected: number;
  durationDaysExpected: number;
  status: string;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeeStaffingLine {
  id: string;
  scenarioId: string;
  phaseForecastId?: string | null;
  role: string;
  hoursExpected: number;
  billRate: number;
  costRate: number;
  feeExpected: number;
  costExpected: number;
  utilizationRiskScore?: number | null;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeeAssumption {
  id: string;
  planVersionId: string;
  category: string;
  key: string;
  valueType: string;
  valueJson?: string | null;
  sourceType: string;
  sourceRef?: string | null;
  notes?: string | null;
  metadataJson?: string | null;
  createdAt: string;
}

export interface OutcomeFeeCollectionsForecast {
  id: string;
  scenarioId: string;
  payorSegment: string;
  bucketDays: number;
  expectedAmount: number;
  collectionProbability: number;
  status: string;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeePlanDetailResult {
  plan?: OutcomeFeePlan | null;
  currentVersion?: OutcomeFeePlanVersion | null;
  versions: OutcomeFeePlanVersion[];
  scenarios: OutcomeFeeScenario[];
  phaseForecasts: OutcomeFeePhaseForecast[];
  staffingLines: OutcomeFeeStaffingLine[];
  assumptions: OutcomeFeeAssumption[];
  collectionsForecasts: OutcomeFeeCollectionsForecast[];
}

export interface OutcomeFeePlanVersionCompareResult {
  planId: string;
  matterId?: string | null;
  fromVersionId?: string | null;
  toVersionId?: string | null;
  fromVersionNumber?: number | null;
  toVersionNumber?: number | null;
  comparedAtUtc: string;
  actuals?: Record<string, unknown> | null;
  driftSummary?: Record<string, unknown> | null;
  scenarioDeltas: Array<Record<string, unknown>>;
  phaseDeltas: Array<Record<string, unknown>>;
}

export interface OutcomeFeePlanTriggerResult {
  triggerAccepted: boolean;
  recomputed: boolean;
  driftDetected: boolean;
  reviewItemsQueued: number;
  notificationsQueued: number;
  planId?: string | null;
  matterId?: string | null;
  previousVersionId?: string | null;
  currentVersionId?: string | null;
  triggerType?: string | null;
  triggerEntityType?: string | null;
  triggerEntityId?: string | null;
  driftSummary?: Record<string, unknown> | null;
  compare?: OutcomeFeePlanVersionCompareResult | null;
}

export interface OutcomeFeePlanPortfolioMetricsResult {
  days: number;
  plansObserved: number;
  comparesUsed: number;
  dataQuality?: string | null;
  metrics?: {
    forecastAccuracy?: number;
    collectionsForecastError?: number;
    marginForecastError?: number;
    staffingVariance?: number;
    avgHoursDriftRatio?: number;
    avgCollectionsDriftRatio?: number;
    avgMarginCompressionRatio?: number;
    driftHighCount?: number;
    driftMediumCount?: number;
    collectionsRiskWorsenedCount?: number;
  } | null;
}

export interface OutcomeFeeCalibrationSnapshotRecord {
  id: string;
  cohortKey: string;
  jurisdictionCode?: string | null;
  practiceArea?: string | null;
  arrangementType?: string | null;
  asOfDate: string;
  status: string;
  sampleSize: number;
  metricsJson?: string | null;
  payloadJson?: string | null;
  metadataJson?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OutcomeFeeCalibrationSnapshotEnvelope {
  snapshot: OutcomeFeeCalibrationSnapshotRecord;
  metrics?: Record<string, unknown> | null;
  payload?: Record<string, unknown> | null;
  metadata?: Record<string, unknown> | null;
}

export interface OutcomeFeeCalibrationSnapshotsListResult {
  count: number;
  items: OutcomeFeeCalibrationSnapshotEnvelope[];
}

export interface OutcomeFeeCalibrationEffectiveResult {
  matterId: string;
  hasCalibration: boolean;
  active?: OutcomeFeeCalibrationSnapshotEnvelope | null;
  shadow?: OutcomeFeeCalibrationSnapshotEnvelope | null;
  candidateCohorts?: Array<{ scope?: string; cohortKey?: string }>;
}

export interface OutcomeFeeCalibrationJobRunResult {
  days: number;
  minSampleSize: number;
  shadowMode: boolean;
  autoActivateHighConfidence?: boolean;
  autoActivateConfidenceThreshold?: number;
  cohortScopes?: string[];
  created: number;
  skipped: number;
  autoActivated?: number;
  snapshots: Array<Record<string, unknown>>;
  notes?: string;
}

export interface OutcomeFeeOutcomeFeedbackResult {
  eventId: string;
  planId: string;
  latestVersionId?: string | null;
  createdAtUtc: string;
}
