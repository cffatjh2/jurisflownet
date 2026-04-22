using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public sealed class MessageAttachmentIntakeService
    {
        private const int MaxAttachmentCount = 10;
        private const int MaxAttachmentSizeBytes = 10 * 1024 * 1024;
        private const int MaxTotalAttachmentSizeBytes = 25 * 1024 * 1024;

        private static readonly IReadOnlyDictionary<string, string> AllowedMimeToExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.ms-excel"] = ".xls",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.ms-powerpoint"] = ".ppt",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["text/plain"] = ".txt"
        };

        private readonly IAppFileStorage _fileStorage;
        private readonly TenantContext _tenantContext;

        public MessageAttachmentIntakeService(IAppFileStorage fileStorage, TenantContext tenantContext)
        {
            _fileStorage = fileStorage;
            _tenantContext = tenantContext;
        }

        public async Task<List<MessageAttachmentPayload>> SaveAsync(List<AttachmentDto>? attachments, CancellationToken cancellationToken = default)
        {
            var result = new List<MessageAttachmentPayload>();
            if (attachments == null || attachments.Count == 0)
            {
                return result;
            }

            if (attachments.Count > MaxAttachmentCount)
            {
                throw new InvalidOperationException($"A maximum of {MaxAttachmentCount} attachments is allowed per message.");
            }

            long totalBytes = 0;
            foreach (var attachment in attachments)
            {
                var (mimeType, base64Payload) = ParseAttachmentData(attachment);
                if (!AllowedMimeToExtension.TryGetValue(mimeType, out var extension))
                {
                    throw new InvalidOperationException($"Attachment MIME type '{mimeType}' is not allowed.");
                }

                var bytes = DecodeBase64(base64Payload, MaxAttachmentSizeBytes);
                ValidateAttachmentSignature(mimeType, bytes);
                totalBytes += bytes.Length;
                if (totalBytes > MaxTotalAttachmentSizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Total attachment payload exceeds the {(MaxTotalAttachmentSizeBytes / (1024 * 1024)).ToString()} MB limit.");
                }

                var storedFileName = $"{Guid.NewGuid():N}{extension}";
                await _fileStorage.SaveBytesAsync(GetMessageAttachmentPath(storedFileName), bytes, mimeType, cancellationToken);

                result.Add(new MessageAttachmentPayload
                {
                    FileName = NormalizeDisplayFileName(attachment.FileName ?? attachment.Name, extension),
                    FilePath = $"/api/files/messages/{storedFileName}",
                    MimeType = mimeType,
                    Size = bytes.Length
                });
            }

            return result;
        }

        private string GetMessageAttachmentPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return $"uploads/{_tenantContext.TenantId}/message-attachments/{fileName}";
        }

        private static (string MimeType, string Base64Payload) ParseAttachmentData(AttachmentDto attachment)
        {
            if (string.IsNullOrWhiteSpace(attachment.Data))
            {
                throw new InvalidOperationException("Attachment data is required.");
            }

            var rawData = attachment.Data.Trim();
            if (!rawData.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment payload must be a base64 data URL.");
            }

            var commaIndex = rawData.IndexOf(',');
            if (commaIndex <= 5 || commaIndex >= rawData.Length - 1)
            {
                throw new InvalidOperationException("Attachment payload is malformed.");
            }

            var header = rawData.Substring(5, commaIndex - 5);
            var base64Payload = rawData[(commaIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(base64Payload))
            {
                throw new InvalidOperationException("Attachment payload is empty.");
            }

            var headerParts = header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (headerParts.Length == 0 || string.IsNullOrWhiteSpace(headerParts[0]))
            {
                throw new InvalidOperationException("Attachment MIME type is required.");
            }

            if (!headerParts.Any(part => string.Equals(part, "base64", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Attachment payload must use base64 encoding.");
            }

            var mimeType = headerParts[0].Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(attachment.Type) &&
                !string.Equals(attachment.Type.Trim(), mimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment type metadata does not match payload MIME type.");
            }

            return (mimeType, base64Payload);
        }

        private static byte[] DecodeBase64(string base64Payload, int maxBytes)
        {
            if (base64Payload.Length % 4 != 0)
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            var padding = base64Payload.EndsWith("==", StringComparison.Ordinal)
                ? 2
                : base64Payload.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;

            var expectedBytes = ((long)base64Payload.Length / 4L) * 3L - padding;
            if (expectedBytes <= 0 || expectedBytes > int.MaxValue)
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            if (expectedBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment exceeds the {(maxBytes / (1024 * 1024)).ToString()} MB per-file limit.");
            }

            var buffer = new byte[(int)expectedBytes];
            if (!Convert.TryFromBase64String(base64Payload, buffer, out var bytesWritten))
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            if (bytesWritten <= 0 || bytesWritten > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment exceeds the {(maxBytes / (1024 * 1024)).ToString()} MB per-file limit.");
            }

            return bytesWritten == buffer.Length ? buffer : buffer[..bytesWritten];
        }

        private static string NormalizeDisplayFileName(string? fileName, string extension)
        {
            var candidate = Path.GetFileName(fileName ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return $"attachment{extension}";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(baseName.Where(ch => !invalidChars.Contains(ch)).ToArray());
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "attachment";
            }

            if (sanitized.Length > 80)
            {
                sanitized = sanitized[..80];
            }

            return $"{sanitized}{extension}";
        }

        private static void ValidateAttachmentSignature(string mimeType, byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                throw new InvalidOperationException("Attachment payload is empty.");
            }

            if (!FileSignatureValidator.IsValidMessageAttachment(mimeType, bytes))
            {
                throw new InvalidOperationException("Attachment content does not match the declared MIME type.");
            }
        }
    }
}
