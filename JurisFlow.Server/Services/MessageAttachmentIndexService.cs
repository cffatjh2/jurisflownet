using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class MessageAttachmentIndexService
    {
        private readonly JurisFlowDbContext _context;

        public MessageAttachmentIndexService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public Task IndexClientMessageAsync(ClientMessage message, IReadOnlyCollection<MessageAttachmentPayload> attachments, CancellationToken cancellationToken = default)
        {
            if (attachments.Count == 0)
            {
                return Task.CompletedTask;
            }

            var records = attachments
                .Select(attachment => CreateRecord(
                    messageType: "client",
                    messageId: message.Id,
                    attachment: attachment,
                    clientId: message.ClientId,
                    messageEmployeeId: message.EmployeeId,
                    senderUserId: message.SenderUserId,
                    senderEmployeeId: null,
                    recipientEmployeeId: null))
                .ToList();

            _context.MessageAttachments.AddRange(records);
            return Task.CompletedTask;
        }

        public Task IndexStaffMessageAsync(StaffMessage message, IReadOnlyCollection<MessageAttachmentPayload> attachments, CancellationToken cancellationToken = default)
        {
            if (attachments.Count == 0)
            {
                return Task.CompletedTask;
            }

            var records = attachments
                .Select(attachment => CreateRecord(
                    messageType: "staff",
                    messageId: message.Id,
                    attachment: attachment,
                    clientId: null,
                    messageEmployeeId: null,
                    senderUserId: null,
                    senderEmployeeId: message.SenderId,
                    recipientEmployeeId: message.RecipientId))
                .ToList();

            _context.MessageAttachments.AddRange(records);
            return Task.CompletedTask;
        }

        private static MessageAttachment CreateRecord(
            string messageType,
            string messageId,
            MessageAttachmentPayload attachment,
            string? clientId,
            string? messageEmployeeId,
            string? senderUserId,
            string? senderEmployeeId,
            string? recipientEmployeeId)
        {
            return new MessageAttachment
            {
                MessageType = messageType,
                MessageId = messageId,
                StoredFileName = ExtractStoredFileName(attachment.FilePath) ?? string.Empty,
                FileName = attachment.FileName,
                FilePath = attachment.FilePath,
                MimeType = attachment.MimeType,
                Size = attachment.Size,
                ClientId = clientId,
                MessageEmployeeId = messageEmployeeId,
                SenderUserId = senderUserId,
                SenderEmployeeId = senderEmployeeId,
                RecipientEmployeeId = recipientEmployeeId,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static string? ExtractStoredFileName(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var value = filePath.Trim().Replace('\\', '/');
            var queryIndex = value.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
            {
                value = value[..queryIndex];
            }

            return Path.GetFileName(value);
        }
    }
}
