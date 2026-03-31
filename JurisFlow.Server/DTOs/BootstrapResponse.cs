namespace JurisFlow.Server.DTOs
{
    public class BootstrapResponse
    {
        public object? Matters { get; set; }
        public object? Tasks { get; set; }
        public object? TimeEntries { get; set; }
        public object? Expenses { get; set; }
        public object? Clients { get; set; }
        public object? Leads { get; set; }
        public object? Events { get; set; }
        public object? Invoices { get; set; }
        public object? Notifications { get; set; }
        public object? Documents { get; set; }
        public object? TaskTemplates { get; set; }
    }
}
