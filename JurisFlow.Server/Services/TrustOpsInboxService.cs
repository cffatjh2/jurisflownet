using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class TrustOpsInboxService
    {
        private readonly JurisFlowDbContext _context;
        private readonly TrustComplianceService _trustComplianceService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrustOpsInboxService(
            JurisFlowDbContext context,
            TrustComplianceService trustComplianceService,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _trustComplianceService = trustComplianceService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TrustOpsInboxSummaryDto> SyncInboxAsync(bool syncAlerts = true, CancellationToken ct = default)
        {
            if (syncAlerts)
            {
                await _trustComplianceService.SyncOperationalAlertsAsync(ct);
            }

            var alerts = await _context.TrustOperationalAlerts
                .AsNoTracking()
                .Where(a => a.WorkflowStatus != "resolved")
                .ToListAsync(ct);

            var accountIds = alerts
                .Select(a => a.TrustAccountId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var accountMap = await _context.TrustBankAccounts
                .AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);
            var existing = await _context.TrustOpsInboxItems.ToListAsync(ct);
            var existingByAlertId = existing
                .Where(i => !string.IsNullOrWhiteSpace(i.TrustOperationalAlertId))
                .ToDictionary(i => i.TrustOperationalAlertId!, StringComparer.Ordinal);

            var now = DateTime.UtcNow;
            foreach (var alert in alerts)
            {
                accountMap.TryGetValue(alert.TrustAccountId ?? string.Empty, out var account);
                if (!existingByAlertId.TryGetValue(alert.Id, out var item))
                {
                    item = new TrustOpsInboxItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustOperationalAlertId = alert.Id,
                        CreatedAt = now,
                        OpenedAt = alert.OpenedAt
                    };
                    ApplyAlertToInbox(item, alert, account, now);
                    _context.TrustOpsInboxItems.Add(item);
                    _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustOpsInboxItemId = item.Id,
                        EventType = "detected",
                        Notes = "Ops inbox item created from operational alert.",
                        MetadataJson = JsonSerializer.Serialize(new { alertId = alert.Id, alertType = alert.AlertType }),
                        CreatedAt = now
                    });
                    existingByAlertId[alert.Id] = item;
                }
                else
                {
                    ApplyAlertToInbox(item, alert, account, now);
                }
            }

            var activeAlertIds = alerts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleItem in existing.Where(i =>
                         !string.IsNullOrWhiteSpace(i.TrustOperationalAlertId) &&
                         !activeAlertIds.Contains(i.TrustOperationalAlertId!) &&
                         !string.Equals(i.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase)))
            {
                staleItem.WorkflowStatus = "resolved";
                staleItem.LastActionAt = now;
                staleItem.UpdatedAt = now;
                _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustOpsInboxItemId = staleItem.Id,
                    EventType = "auto_resolved",
                    Notes = "Linked operational alert resolved or disappeared from the active set.",
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync(ct);
            return await GetInboxAsync(ct: ct);
        }

        public async Task<TrustOpsInboxSummaryDto> GetInboxAsync(
            string? assignedUserId = null,
            string? officeId = null,
            string? jurisdiction = null,
            string? severity = null,
            string? blockerGroup = null,
            string? workflowStatus = null,
            bool breachedOnly = false,
            CancellationToken ct = default)
        {
            var query = _context.TrustOpsInboxItems
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(assignedUserId))
            {
                query = query.Where(i => i.AssignedUserId == assignedUserId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(i => i.OfficeId == officeId);
            }

            if (!string.IsNullOrWhiteSpace(jurisdiction))
            {
                query = query.Where(i => i.Jurisdiction == jurisdiction);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                var normalizedSeverity = severity.Trim().ToLowerInvariant();
                query = query.Where(i => i.Severity == normalizedSeverity);
            }

            if (!string.IsNullOrWhiteSpace(blockerGroup))
            {
                var normalizedGroup = blockerGroup.Trim().ToLowerInvariant();
                query = query.Where(i => i.BlockerGroup == normalizedGroup);
            }

            if (!string.IsNullOrWhiteSpace(workflowStatus))
            {
                var normalizedWorkflow = workflowStatus.Trim().ToLowerInvariant();
                query = query.Where(i => i.WorkflowStatus == normalizedWorkflow);
            }

            var items = await query
                .OrderBy(i => i.DueAt ?? DateTime.MaxValue)
                .ThenByDescending(i => i.LastDetectedAt)
                .Take(250)
                .ToListAsync(ct);

            var alertIds = items
                .Where(i => !string.IsNullOrWhiteSpace(i.TrustOperationalAlertId))
                .Select(i => i.TrustOperationalAlertId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var linkedAlerts = alertIds.Count == 0
                ? new Dictionary<string, TrustOperationalAlert>(StringComparer.Ordinal)
                : await _context.TrustOperationalAlerts.AsNoTracking()
                    .Where(a => alertIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, ct);
            var accountIds = items
                .Where(i => !string.IsNullOrWhiteSpace(i.TrustAccountId))
                .Select(i => i.TrustAccountId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var accountNameMap = accountIds.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _context.TrustBankAccounts.AsNoTracking()
                    .Where(a => accountIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

            var dtos = items
                .Select(i => ToDto(i, accountNameMap, linkedAlerts))
                .Where(dto => !breachedOnly || dto.IsSlaBreached)
                .OrderByDescending(dto => dto.IsSlaBreached)
                .ThenBy(dto => dto.DueAt ?? DateTime.MaxValue)
                .ThenByDescending(dto => dto.AgeDays)
                .ToList();

            return new TrustOpsInboxSummaryDto
            {
                GeneratedAtUtc = DateTime.UtcNow,
                TotalCount = dtos.Count,
                BreachedCount = dtos.Count(i => i.IsSlaBreached),
                CloseBlockerCount = dtos.Count(i => i.BlockerGroup == "close_blocker"),
                StatementBlockerCount = dtos.Count(i => i.BlockerGroup == "statement_blocker"),
                ExceptionBlockerCount = dtos.Count(i => i.BlockerGroup == "exception_blocker"),
                ApprovalBlockerCount = dtos.Count(i => i.BlockerGroup == "approval_blocker"),
                Items = dtos
            };
        }

        public async Task<IReadOnlyList<TrustOpsInboxEventDto>> GetInboxHistoryAsync(string id, CancellationToken ct = default)
        {
            var exists = await _context.TrustOpsInboxItems.AsNoTracking().AnyAsync(i => i.Id == id, ct);
            if (!exists)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust ops inbox item not found.");
            }

            var rows = await _context.TrustOpsInboxEvents.AsNoTracking()
                .Where(e => e.TrustOpsInboxItemId == id)
                .OrderByDescending(e => e.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            return rows.Select(e => new TrustOpsInboxEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                ActorUserId = e.ActorUserId,
                Notes = e.Notes,
                MetadataJson = e.MetadataJson,
                CreatedAt = e.CreatedAt
            }).ToList();
        }

        public async Task<TrustOpsInboxItemDto> ClaimAsync(string id, string? notes, CancellationToken ct = default)
        {
            var item = await FindItemAsync(id, ct);
            var actorUserId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                throw new TrustCommandException(StatusCodes.Status401Unauthorized, "A signed-in user is required to claim inbox work.");
            }

            if (!string.IsNullOrWhiteSpace(item.TrustOperationalAlertId))
            {
                await _trustComplianceService.AssignOperationalAlertAsync(item.TrustOperationalAlertId, new TrustOperationalAlertAssignDto
                {
                    AssigneeUserId = actorUserId,
                    Notes = notes
                }, ct);
            }

            item.AssignedUserId = actorUserId;
            item.WorkflowStatus = "claimed";
            item.LastActionAt = DateTime.UtcNow;
            item.UpdatedAt = item.LastActionAt.Value;
            AddInboxEvent(item.Id, "claimed", actorUserId, notes, new { actorUserId });
            await _context.SaveChangesAsync(ct);
            return await GetItemDtoAsync(item.Id, ct);
        }

        public async Task<TrustOpsInboxItemDto> AssignAsync(string id, TrustOpsInboxAssignDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.AssigneeUserId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Assignee user id is required.");
            }

            var item = await FindItemAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(item.TrustOperationalAlertId))
            {
                await _trustComplianceService.AssignOperationalAlertAsync(item.TrustOperationalAlertId, new TrustOperationalAlertAssignDto
                {
                    AssigneeUserId = dto.AssigneeUserId.Trim(),
                    Notes = dto.Notes
                }, ct);
            }

            item.AssignedUserId = dto.AssigneeUserId.Trim();
            item.WorkflowStatus = "assigned";
            item.LastActionAt = DateTime.UtcNow;
            item.UpdatedAt = item.LastActionAt.Value;
            AddInboxEvent(item.Id, "assigned", GetCurrentUserId(), dto.Notes, new { assigneeUserId = item.AssignedUserId });
            await _context.SaveChangesAsync(ct);
            return await GetItemDtoAsync(item.Id, ct);
        }

        public async Task<TrustOpsInboxItemDto> DeferAsync(string id, TrustOpsInboxDeferDto dto, CancellationToken ct = default)
        {
            if (dto.DeferredUntilUtc <= DateTime.UtcNow)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Deferred-until timestamp must be in the future.");
            }

            var item = await FindItemAsync(id, ct);
            item.DeferredUntil = dto.DeferredUntilUtc.ToUniversalTime();
            item.WorkflowStatus = "deferred";
            item.LastActionAt = DateTime.UtcNow;
            item.UpdatedAt = item.LastActionAt.Value;
            AddInboxEvent(item.Id, "deferred", GetCurrentUserId(), dto.Notes, new { deferredUntil = item.DeferredUntil });
            await _context.SaveChangesAsync(ct);
            return await GetItemDtoAsync(item.Id, ct);
        }

        public async Task<TrustOpsInboxItemDto> EscalateAsync(string id, string? notes, CancellationToken ct = default)
        {
            var item = await FindItemAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(item.TrustOperationalAlertId))
            {
                await _trustComplianceService.EscalateOperationalAlertAsync(item.TrustOperationalAlertId, new TrustOperationalAlertActionDto
                {
                    Notes = notes
                }, ct);
            }

            item.WorkflowStatus = "escalated";
            item.LastActionAt = DateTime.UtcNow;
            item.UpdatedAt = item.LastActionAt.Value;
            AddInboxEvent(item.Id, "escalated", GetCurrentUserId(), notes);
            await _context.SaveChangesAsync(ct);
            return await GetItemDtoAsync(item.Id, ct);
        }

        public async Task<TrustOpsInboxItemDto> ResolveAsync(string id, string? notes, CancellationToken ct = default)
        {
            var item = await FindItemAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(item.TrustOperationalAlertId))
            {
                await _trustComplianceService.ResolveOperationalAlertAsync(item.TrustOperationalAlertId, new TrustOperationalAlertActionDto
                {
                    Notes = notes
                }, ct);
            }

            item.WorkflowStatus = "resolved";
            item.LastActionAt = DateTime.UtcNow;
            item.UpdatedAt = item.LastActionAt.Value;
            AddInboxEvent(item.Id, "resolved", GetCurrentUserId(), notes);
            await _context.SaveChangesAsync(ct);
            return await GetItemDtoAsync(item.Id, ct);
        }

        private async Task<TrustOpsInboxItem> FindItemAsync(string id, CancellationToken ct)
        {
            var item = await _context.TrustOpsInboxItems.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust ops inbox item not found.");
            }

            return item;
        }

        private async Task<TrustOpsInboxItemDto> GetItemDtoAsync(string id, CancellationToken ct)
        {
            var item = await _context.TrustOpsInboxItems.AsNoTracking().FirstAsync(i => i.Id == id, ct);
            var accountNameMap = string.IsNullOrWhiteSpace(item.TrustAccountId)
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _context.TrustBankAccounts.AsNoTracking()
                    .Where(a => a.Id == item.TrustAccountId)
                    .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
            var linkedAlerts = string.IsNullOrWhiteSpace(item.TrustOperationalAlertId)
                ? new Dictionary<string, TrustOperationalAlert>(StringComparer.Ordinal)
                : await _context.TrustOperationalAlerts.AsNoTracking()
                    .Where(a => a.Id == item.TrustOperationalAlertId)
                    .ToDictionaryAsync(a => a.Id, ct);
            return ToDto(item, accountNameMap, linkedAlerts);
        }

        private void ApplyAlertToInbox(TrustOpsInboxItem item, TrustOperationalAlert alert, TrustBankAccount? account, DateTime now)
        {
            item.TrustOperationalAlertId = alert.Id;
            item.ItemType = "operational_alert";
            item.BlockerGroup = ResolveBlockerGroup(alert.AlertType);
            item.Severity = alert.Severity;
            item.TrustAccountId = alert.TrustAccountId;
            item.Jurisdiction = account?.Jurisdiction;
            item.OfficeId = account?.OfficeId;
            item.AssignedUserId ??= alert.AssignedUserId;
            item.WorkflowStatus = NormalizeInboxWorkflow(item.WorkflowStatus, alert.WorkflowStatus);
            item.DueAt = ResolveDueAt(alert, account);
            item.Title = alert.Title;
            item.Summary = alert.Summary;
            item.ActionHint = alert.ActionHint;
            item.SuggestedExportType = ResolveSuggestedExportType(item.BlockerGroup);
            item.SuggestedRoute = ResolveSuggestedRoute(item.BlockerGroup, alert);
            item.LastDetectedAt = alert.LastDetectedAt;
            item.OpenedAt = alert.OpenedAt;
            item.MetadataJson = JsonSerializer.Serialize(new
            {
                alertType = alert.AlertType,
                alertWorkflowStatus = alert.WorkflowStatus,
                alertRelatedEntityType = alert.RelatedEntityType,
                alertRelatedEntityId = alert.RelatedEntityId,
                periodEnd = alert.PeriodEnd
            });
            item.UpdatedAt = now;
        }

        private void AddInboxEvent(string itemId, string eventType, string? actorUserId, string? notes, object? metadata = null)
        {
            _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                TrustOpsInboxItemId = itemId,
                EventType = eventType,
                ActorUserId = actorUserId,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                MetadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata),
                CreatedAt = DateTime.UtcNow
            });
        }

        private static TrustOpsInboxItemDto ToDto(
            TrustOpsInboxItem item,
            IReadOnlyDictionary<string, string> accountNameMap,
            IReadOnlyDictionary<string, TrustOperationalAlert> linkedAlerts)
        {
            linkedAlerts.TryGetValue(item.TrustOperationalAlertId ?? string.Empty, out var alert);
            var isBreached = item.DueAt.HasValue &&
                             item.DueAt.Value <= DateTime.UtcNow &&
                             !string.Equals(item.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase);
            return new TrustOpsInboxItemDto
            {
                Id = item.Id,
                TrustOperationalAlertId = item.TrustOperationalAlertId,
                TrustCloseForecastSnapshotId = item.TrustCloseForecastSnapshotId,
                ItemType = item.ItemType,
                BlockerGroup = item.BlockerGroup,
                Severity = item.Severity,
                TrustAccountId = item.TrustAccountId,
                TrustAccountName = item.TrustAccountId != null && accountNameMap.TryGetValue(item.TrustAccountId, out var accountName) ? accountName : null,
                Jurisdiction = item.Jurisdiction,
                OfficeId = item.OfficeId,
                AssignedUserId = item.AssignedUserId,
                WorkflowStatus = item.WorkflowStatus,
                OpenedAt = item.OpenedAt,
                LastDetectedAt = item.LastDetectedAt,
                DueAt = item.DueAt,
                DeferredUntil = item.DeferredUntil,
                IsSlaBreached = isBreached,
                Title = item.Title,
                Summary = item.Summary,
                ActionHint = item.ActionHint,
                SuggestedExportType = item.SuggestedExportType,
                SuggestedRoute = item.SuggestedRoute,
                AgeDays = Math.Max(0, (DateTime.UtcNow.Date - item.OpenedAt.Date).Days),
                LinkedAlertWorkflowStatus = alert?.WorkflowStatus
            };
        }

        private static string ResolveBlockerGroup(string alertType)
        {
            var normalized = alertType.Trim().ToLowerInvariant();
            return normalized switch
            {
                "missing_month_close" or "unsigned_month_close" => "close_blocker",
                "duplicate_statement_import" or "uncleared_funds_aging" => "statement_blocker",
                "outstanding_item_aging" => "exception_blocker",
                _ => "general_blocker"
            };
        }

        private static string ResolveSuggestedExportType(string blockerGroup) => blockerGroup switch
        {
            "close_blocker" => "month_close_packet",
            "statement_blocker" => "account_journal",
            "approval_blocker" => "approval_register",
            "exception_blocker" => "account_journal",
            _ => "month_close_packet"
        };

        private static string ResolveSuggestedRoute(string blockerGroup, TrustOperationalAlert alert) => blockerGroup switch
        {
            "close_blocker" => $"trust/reconciliation/{alert.RelatedEntityId ?? alert.TrustAccountId}",
            "statement_blocker" => $"trust/statements/{alert.RelatedEntityId ?? alert.TrustAccountId}",
            "approval_blocker" => $"trust/approvals/{alert.RelatedEntityId ?? alert.TrustAccountId}",
            "exception_blocker" => $"trust/exceptions/{alert.RelatedEntityId ?? alert.TrustAccountId}",
            _ => $"trust/alerts/{alert.Id}"
        };

        private static DateTime ResolveDueAt(TrustOperationalAlert alert, TrustBankAccount? account)
        {
            if (alert.AlertType == "missing_month_close" || alert.AlertType == "unsigned_month_close")
            {
                if (alert.PeriodEnd.HasValue)
                {
                    return alert.PeriodEnd.Value.Date.AddDays(5);
                }

                return alert.OpenedAt.Date.AddDays(5);
            }

            var cadenceHours = string.Equals(account?.StatementCadence, "weekly", StringComparison.OrdinalIgnoreCase)
                ? 24
                : 72;
            return alert.OpenedAt.AddHours(alert.Severity == "critical" ? 24 : cadenceHours);
        }

        private static string NormalizeInboxWorkflow(string currentWorkflowStatus, string alertWorkflowStatus)
        {
            if (string.Equals(currentWorkflowStatus, "deferred", StringComparison.OrdinalIgnoreCase))
            {
                return currentWorkflowStatus;
            }

            if (string.Equals(alertWorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                return "resolved";
            }

            return currentWorkflowStatus switch
            {
                "claimed" or "assigned" or "escalated" => currentWorkflowStatus,
                _ => "open"
            };
        }

        private string? GetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user?.FindFirst("sub")?.Value
                   ?? user?.FindFirst("userId")?.Value
                   ?? user?.Identity?.Name;
        }
    }
}
