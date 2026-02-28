using JurisFlow.Server.Models;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public static class IntegrationProviderKeys
    {
        public const string Stripe = "stripe";
        public const string GoogleCalendar = "google-calendar";
        public const string QuickBooksOnline = "quickbooks-online";
        public const string Xero = "xero";
        public const string MicrosoftOutlookCalendar = "microsoft-outlook-calendar";
        public const string GoogleGmail = "google-gmail";
        public const string MicrosoftOutlookMail = "microsoft-outlook-mail";
        public const string MicrosoftSharePointOneDrive = "microsoft-sharepoint-onedrive";
        public const string GoogleDrive = "google-drive";
        public const string NetDocuments = "netdocuments";
        public const string IManage = "imanage";
        public const string MicrosoftBusinessCentral = "microsoft-business-central";
        public const string NetSuite = "netsuite";
        public const string CourtListenerDockets = "courtlistener-dockets";
        public const string CourtListenerRecap = "courtlistener-recap";
        public const string OneLegalEfile = "one-legal-efile";
        public const string FileAndServeXpressEfile = "fileandservexpress-efile";
    }

    public sealed class StripeIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public StripeIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.Stripe, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncStripeConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.Stripe);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class GoogleCalendarIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public GoogleCalendarIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.GoogleCalendar, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncGoogleCalendarConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class QuickBooksIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public QuickBooksIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.QuickBooksOnline, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncQuickBooksConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.QuickBooksOnline);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class XeroIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public XeroIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.Xero, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncXeroConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.Xero);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class OutlookCalendarIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public OutlookCalendarIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.MicrosoftOutlookCalendar, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncOutlookCalendarConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class CourtListenerDocketsIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public CourtListenerDocketsIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.CourtListenerDockets, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncCourtListenerDocketsConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class CourtListenerRecapIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public CourtListenerRecapIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.CourtListenerRecap, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncCourtListenerRecapConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class GoogleGmailIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public GoogleGmailIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.GoogleGmail, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncGoogleGmailConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.GoogleGmail);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class OutlookMailIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public OutlookMailIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.MicrosoftOutlookMail, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncOutlookMailConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.MicrosoftOutlookMail);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class SharePointOneDriveIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public SharePointOneDriveIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.MicrosoftSharePointOneDrive, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncSharePointOneDriveConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.MicrosoftSharePointOneDrive);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class GoogleDriveIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public GoogleDriveIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.GoogleDrive, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncGoogleDriveConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.GoogleDrive);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class NetDocumentsIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public NetDocumentsIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.NetDocuments, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncNetDocumentsConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.NetDocuments);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class IManageIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public IManageIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.IManage, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncIManageConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.IManage);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class BusinessCentralIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public BusinessCentralIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.MicrosoftBusinessCentral, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncBusinessCentralConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.MicrosoftBusinessCentral);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class NetSuiteIntegrationConnector : IIntegrationConnector, IIntegrationActionConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public NetSuiteIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.NetSuite, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncNetSuiteConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }

        public IReadOnlyCollection<string> SupportedActions => IntegrationProviderCatalog.GetSupportedActions(IntegrationProviderKeys.NetSuite);

        public Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            return _integrationConnectorService.ExecuteCanonicalActionAsync(connection, request, cancellationToken);
        }
    }

    public sealed class OneLegalEfileIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public OneLegalEfileIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.OneLegalEfile, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncOneLegalEfileConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class FileAndServeXpressEfileIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public FileAndServeXpressEfileIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return string.Equals(providerKey?.Trim(), IntegrationProviderKeys.FileAndServeXpressEfile, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncFileAndServeXpressEfileConnectionAsync(
                connection.Id,
                connection.MetadataJson,
                cancellationToken);
        }
    }
}
