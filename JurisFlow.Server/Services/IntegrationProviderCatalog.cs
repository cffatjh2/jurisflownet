using System;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationProviderCatalogItem
    {
        public string ProviderKey { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string ConnectionMode { get; init; } = "oauth";
        public bool SupportsSync { get; init; } = true;
        public bool SupportsWebhook { get; init; }
        public bool WebhookFirst { get; init; }
        public int FallbackPollingMinutes { get; init; } = 360;
        public IReadOnlyList<string> SupportedActions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    }

    public static class IntegrationProviderCatalog
    {
        public static readonly IReadOnlyList<IntegrationProviderCatalogItem> Items = new List<IntegrationProviderCatalogItem>
        {
            new()
            {
                ProviderKey = "quickbooks-online",
                Provider = "QuickBooks Online",
                Category = "Accounting",
                Description = "Sync invoices, payments, and chart of accounts.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "invoice.read", "invoice.write",
                    "payment.read", "payment.write",
                    "customer.read", "customer.write",
                    "account.map", "tax.map",
                    "payment.webhook", "reconciliation.queue"
                }
            },
            new()
            {
                ProviderKey = "xero",
                Provider = "Xero",
                Category = "Accounting",
                Description = "Connect billing data to Xero for reconciliation.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "invoice.read", "invoice.write",
                    "payment.read", "payment.write",
                    "contact.read", "contact.write",
                    "account.map", "tax.map",
                    "payment.webhook", "reconciliation.queue"
                }
            },
            new()
            {
                ProviderKey = "stripe",
                Provider = "Stripe",
                Category = "Payments",
                Description = "Accept card payments and reconcile settlements.",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = false,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "payment.read", "payment.webhook", "refund.read", "dispute.read",
                    "payout.read", "reconciliation.queue"
                }
            },
            new()
            {
                ProviderKey = "google-calendar",
                Provider = "Google Calendar",
                Category = "Calendar",
                Description = "Two-way sync for hearings and appointments.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Backfill
                },
                Capabilities = new[]
                {
                    "calendar_event.read", "calendar_event.write", "availability.read"
                }
            },
            new()
            {
                ProviderKey = "microsoft-outlook-calendar",
                Provider = "Microsoft Outlook Calendar",
                Category = "Calendar",
                Description = "Sync firm calendars and deadline reminders.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Backfill
                },
                Capabilities = new[]
                {
                    "calendar_event.read", "calendar_event.write", "availability.read"
                }
            },
            new()
            {
                ProviderKey = "google-gmail",
                Provider = "Google Gmail",
                Category = "Email",
                Description = "Matter-linked email filing and mailbox sync.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill
                },
                Capabilities = new[]
                {
                    "message.read", "message.write",
                    "attachment.download", "attachment.upload",
                    "matter.link", "label.sync"
                }
            },
            new()
            {
                ProviderKey = "microsoft-outlook-mail",
                Provider = "Microsoft Outlook Mail",
                Category = "Email",
                Description = "Matter-linked Outlook mailbox sync and filing.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill
                },
                Capabilities = new[]
                {
                    "message.read", "message.write",
                    "attachment.download", "attachment.upload",
                    "matter.link", "folder.sync"
                }
            },
            new()
            {
                ProviderKey = "microsoft-sharepoint-onedrive",
                Provider = "Microsoft SharePoint / OneDrive",
                Category = "Document Management",
                Description = "Matter-linked document sync with metadata, foldering, and webhook-first ingest.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 180,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "document.read", "document.write", "document.metadata.sync",
                    "folder.sync", "webhook.ingest", "hash.dedupe", "matter.link"
                }
            },
            new()
            {
                ProviderKey = "google-drive",
                Provider = "Google Drive",
                Category = "Document Management",
                Description = "Sync Drive files to matter folders with metadata and dedupe workflows.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 180,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "document.read", "document.write", "document.metadata.sync",
                    "folder.sync", "webhook.ingest", "hash.dedupe", "matter.link"
                }
            },
            new()
            {
                ProviderKey = "netdocuments",
                Provider = "NetDocuments",
                Category = "Legal DMS",
                Description = "Legal DMS integration for matter workspaces, profile metadata, and filing packets.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = false,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "workspace.read", "workspace.write",
                    "document.read", "document.write", "document.profile.sync",
                    "matter.link", "hash.dedupe"
                }
            },
            new()
            {
                ProviderKey = "imanage",
                Provider = "iManage",
                Category = "Legal DMS",
                Description = "Work-product and matter-centric document sync for iManage libraries/workspaces.",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = false,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "workspace.read", "workspace.write",
                    "document.read", "document.write", "document.profile.sync",
                    "matter.link", "privilege.tagging"
                }
            },
            new()
            {
                ProviderKey = "microsoft-business-central",
                Provider = "Microsoft Business Central",
                Category = "ERP",
                Description = "ERP sync for customer, invoice, payment posting, and GL reconciliation (phase 2).",
                ConnectionMode = "oauth",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "customer.read", "customer.write",
                    "invoice.read", "invoice.write",
                    "payment.read", "payment.write",
                    "account.map", "tax.map", "reconciliation.queue"
                }
            },
            new()
            {
                ProviderKey = "netsuite",
                Provider = "NetSuite",
                Category = "ERP",
                Description = "ERP synchronization for finance master data and downstream billing reconciliation (phase 2).",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                FallbackPollingMinutes = 360,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "customer.read", "customer.write",
                    "invoice.read", "invoice.write",
                    "payment.read", "payment.write",
                    "account.map", "tax.map", "reconciliation.queue"
                }
            },
            new()
            {
                ProviderKey = "courtlistener-dockets",
                Provider = "CourtListener Dockets",
                Category = "Court Docket",
                Description = "Monitor U.S. docket updates via CourtListener API.",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "docket.read", "docket.backfill", "docket_event.link", "court_rule.enrich"
                }
            },
            new()
            {
                ProviderKey = "courtlistener-recap",
                Provider = "CourtListener RECAP",
                Category = "E-Filing",
                Description = "Track RECAP filing fetch requests for U.S. federal courts.",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Backfill,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "filing_status.read", "docket_document.read", "filing_queue.reconcile"
                }
            },
            new()
            {
                ProviderKey = "one-legal-efile",
                Provider = "One Legal E-Filing",
                Category = "E-Filing",
                Description = "Sync filing statuses from One Legal partner API.",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "efile.submit", "efile.status.read", "efile.status.webhook", "filing_queue.reconcile"
                }
            },
            new()
            {
                ProviderKey = "fileandservexpress-efile",
                Provider = "File & ServeXpress",
                Category = "E-Filing",
                Description = "Sync filing queue and acceptance updates.",
                ConnectionMode = "api_key",
                SupportsSync = true,
                SupportsWebhook = true,
                WebhookFirst = true,
                SupportedActions = new[]
                {
                    IntegrationCanonicalActions.Validate,
                    IntegrationCanonicalActions.Pull,
                    IntegrationCanonicalActions.Push,
                    IntegrationCanonicalActions.Webhook,
                    IntegrationCanonicalActions.Reconcile
                },
                Capabilities = new[]
                {
                    "efile.submit", "efile.status.read", "efile.status.webhook", "filing_queue.reconcile"
                }
            }
        };

        public static IntegrationProviderCatalogItem? Find(string? providerKey)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
            {
                return null;
            }

            return Items.FirstOrDefault(i =>
                string.Equals(i.ProviderKey, providerKey.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsWebhookFirst(string? providerKey)
        {
            return Find(providerKey)?.WebhookFirst == true;
        }

        public static bool SupportsWebhook(string? providerKey)
        {
            return Find(providerKey)?.SupportsWebhook == true;
        }

        public static IReadOnlyList<string> GetSupportedActions(string? providerKey)
        {
            return Find(providerKey)?.SupportedActions ?? Array.Empty<string>();
        }

        public static IReadOnlyList<string> GetCapabilities(string? providerKey)
        {
            return Find(providerKey)?.Capabilities ?? Array.Empty<string>();
        }

        public static int ResolvePollingIntervalMinutes(string? providerKey, int defaultPollingIntervalMinutes)
        {
            var defaultInterval = Math.Clamp(defaultPollingIntervalMinutes, 5, 24 * 60);
            var item = Find(providerKey);
            if (item == null || !item.WebhookFirst)
            {
                return defaultInterval;
            }

            return Math.Clamp(item.FallbackPollingMinutes, defaultInterval, 24 * 60);
        }
    }
}
