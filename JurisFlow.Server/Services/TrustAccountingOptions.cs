namespace JurisFlow.Server.Services
{
    public static class TrustActionKeys
    {
        public const string ApproveTransaction = "approve_transaction";
        public const string RejectTransaction = "reject_transaction";
        public const string VoidTransaction = "void_transaction";
        public const string ClearDeposit = "clear_deposit";
        public const string ReturnDeposit = "return_deposit";
        public const string EarnedFeeTransfer = "earned_fee_transfer";
        public const string ImportStatement = "import_statement";
        public const string ManageOutstandingItems = "manage_outstanding_items";
        public const string PrepareReconciliationPacket = "prepare_reconciliation_packet";
        public const string SignoffReconciliationPacket = "signoff_reconciliation_packet";
        public const string RebuildProjections = "rebuild_projections";
        public const string ManageGovernance = "manage_governance";
        public const string ManagePolicies = "manage_policies";
        public const string OverrideTransaction = "override_transaction";
        public const string ExportData = "export_data";
    }

    public sealed class TrustAccountingOptions
    {
        public TrustAccountingRoleMatrixOptions RoleMatrix { get; set; } = new();
        public bool AutoClearDepositsWithoutCheckNumber { get; set; } = true;
    }

    public sealed class TrustAccountingRoleMatrixOptions
    {
        public string[] ApproveTransaction { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] RejectTransaction { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] VoidTransaction { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ClearDeposit { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ReturnDeposit { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] EarnedFeeTransfer { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ImportStatement { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ManageOutstandingItems { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] PrepareReconciliationPacket { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] SignoffReconciliationPacket { get; set; } = ["Admin", "Partner"];
        public string[] RebuildProjections { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ManageGovernance { get; set; } = ["Admin", "Partner", "Accountant"];
        public string[] ManagePolicies { get; set; } = ["Admin", "Partner"];
        public string[] OverrideTransaction { get; set; } = ["Admin", "Partner"];
        public string[] ExportData { get; set; } = ["Admin", "Partner", "Accountant"];
    }
}
