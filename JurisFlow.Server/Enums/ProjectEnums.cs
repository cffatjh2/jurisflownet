namespace JurisFlow.Server.Enums
{
    public enum EmployeeRole
    {
        Partner,
        Associate,
        OfCounsel,
        Paralegal,
        LegalSecretary,
        LegalAssistant,
        OfficeManager,
        Receptionist,
        Accountant
    }

    public enum EmployeeStatus
    {
        Active,
        OnLeave,
        Terminated
    }

    public enum InvoiceStatus
    {
        Draft,
        PendingApproval,
        Approved,
        Sent,
        PartiallyPaid,
        Paid,
        Overdue,
        WrittenOff,
        Cancelled
    }

    // US Legal Practice Areas
    public enum PracticeArea
    {
        PersonalInjury, AutoAccident, MedicalMalpractice, ProductLiability,
        WorkersCompensation, WrongfulDeath, CivilLitigation, CommercialLitigation,
        ContractDisputes, CriminalDefense, WhiteCollarCrime, DUI, FamilyLaw,
        Divorce, ChildCustody, ChildSupport, Adoption, Corporate, BusinessFormation,
        MergersAcquisitions, Securities, RealEstate, CommercialRealEstate,
        LandlordTenant, IntellectualProperty, Patent, Trademark, Copyright,
        EstatePlanning, Probate, TrustAdministration, Bankruptcy, Immigration,
        Employment, Tax, EnvironmentalLaw, HealthcareLaw
    }

    // US Court Types
    public enum CourtType
    {
        USSupremeCourt, USCourtOfAppeals, USDistrictCourt, USBankruptcyCourt,
        USMagistrateJudge, StateSupremeCourt, StateAppellateCourt, StateSuperiorCourt,
        StateCircuitCourt, StateDistrictCourt, CountyCourt, MunicipalCourt,
        FamilyCourt, ProbateCourt, SmallClaimsCourt, TrafficCourt,
        Arbitration, Mediation, AdminHearing
    }

    // Lead Pipeline Status
    public enum LeadStatus
    {
        New, Contacted, Scheduled, Consulted, Proposal, Retained, Lost
    }

    // Lead Source Tracking
    public enum LeadSource
    {
        Referral, AttorneyReferral, Website, GoogleAds, SocialMedia,
        Avvo, BarReferral, WalkIn, ReturningClient, Other
    }

    // Task Types
    public enum TaskType
    {
        CourtDeadline, StatuteOfLimitations, Filing, Hearing, Trial,
        Deposition, Mediation, ClientMeeting, ClientCall, Research,
        Drafting, DocumentReview, InternalMeeting, FollowUp, Administrative
    }

    // Document Categories (Legal DMS Standard)
    public enum DocumentCategory
    {
        Pleading, Motion, Brief, Contract, Correspondence, Discovery,
        Evidence, CourtOrder, Deposition, ExpertReport, ClientDocument,
        InternalMemo, Template, Other
    }

    // Document Status
    public enum DocumentStatus
    {
        Draft, UnderReview, Final, Executed, Filed, OnLegalHold
    }

    // UTBMS Activity Codes (ABA Standard)
    public enum ActivityCode
    {
        A101, A102, A103, A104, A105, A106, A107, A108, A109
    }

    // UTBMS Expense Codes
    public enum ExpenseCode
    {
        E101, E102, E105, E106, E107, E108, E109, E110, E111,
        E112, E113, E114, E115, E116, E117, E118
    }

    // Trust Transaction Types
    public enum TrustTransactionType
    {
        Deposit, Withdrawal, Transfer, EarnedFees, RefundToClient
    }

    // Retainer Type
    public enum RetainerType
    {
        Standard, Evergreen, Flat
    }

    // Bar License Status
    public enum BarLicenseStatus
    {
        Active, Inactive, Suspended, Pending
    }
}
