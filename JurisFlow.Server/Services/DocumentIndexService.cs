using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public class DocumentIndexService
    {
        private const int MaxIndexLength = 200000;
        private const int MaxTokenCount = 500;
        private const int MinTokenLength = 3;
        private readonly JurisFlowDbContext _context;
        private readonly IAppFileStorage _fileStorage;
        private readonly DocumentEncryptionService _documentEncryptionService;
        private readonly DocumentTextExtractor _textExtractor;

        public DocumentIndexService(
            JurisFlowDbContext context,
            IAppFileStorage fileStorage,
            DocumentEncryptionService documentEncryptionService,
            DocumentTextExtractor textExtractor)
        {
            _context = context;
            _fileStorage = fileStorage;
            _documentEncryptionService = documentEncryptionService;
            _textExtractor = textExtractor;
        }

        public async Task<DocumentContentIndex?> UpsertIndexAsync(Document document)
        {
            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                return null;
            }

            try
            {
                var content = await LoadContentAsync(document);
                return await UpsertIndexAsync(document, content);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        public async Task<DocumentContentIndex?> UpsertIndexAsync(Document document, byte[] plaintextContent)
        {
            var content = await _textExtractor.ExtractTextAsync(plaintextContent, document.FileName);
            return await UpsertIndexAsync(document, content);
        }

        private async Task<DocumentContentIndex?> UpsertIndexAsync(Document document, string content)
        {
            var normalized = NormalizeContent(content);
            var contentHash = ComputeSha256(normalized);
            var truncated = normalized.Length > MaxIndexLength ? normalized[..MaxIndexLength] : normalized;
            var tokens = Tokenize(normalized);

            var index = await _context.DocumentContentIndexes.FindAsync(document.Id);
            if (index == null)
            {
                index = new DocumentContentIndex
                {
                    DocumentId = document.Id
                };
                _context.DocumentContentIndexes.Add(index);
            }

            var existingTokenCount = await _context.DocumentContentTokens
                .Where(t => t.DocumentId == document.Id)
                .CountAsync();

            var contentChanged = index.ContentHash != contentHash;
            if (contentChanged)
            {
                index.Content = truncated;
                index.NormalizedContent = truncated;
                index.ContentHash = contentHash;
                index.ContentLength = normalized.Length;
                index.IndexedAt = DateTime.UtcNow;
            }

            if (contentChanged || existingTokenCount == 0)
            {
                var existingTokens = await _context.DocumentContentTokens
                    .Where(t => t.DocumentId == document.Id)
                    .ToListAsync();
                if (existingTokens.Count > 0)
                {
                    _context.DocumentContentTokens.RemoveRange(existingTokens);
                }

                if (tokens.Count > 0)
                {
                    var tokenEntities = tokens.Select(token => new DocumentContentToken
                    {
                        DocumentId = document.Id,
                        Token = token
                    });
                    await _context.DocumentContentTokens.AddRangeAsync(tokenEntities);
                }
            }

            await _context.SaveChangesAsync();
            return index;
        }

        public Task<int> ReindexAllAsync(int limit = 200, bool force = false)
        {
            return ReindexAllAsync(null, limit, force);
        }

        public async Task<int> ReindexAllAsync(string? tenantId, int limit = 200, bool force = false)
        {
            var query = _context.Documents.AsQueryable();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(d => EF.Property<string>(d, "TenantId") == tenantId);
            }
            if (!force)
            {
                query = query.Where(d => !_context.DocumentContentIndexes.Any(i => i.DocumentId == d.Id)
                    || !_context.DocumentContentTokens.Any(t => t.DocumentId == d.Id));
            }

            var docs = await query
                .OrderByDescending(d => d.UpdatedAt)
                .Take(limit)
                .ToListAsync();

            var count = 0;
            foreach (var doc in docs)
            {
                if (string.IsNullOrWhiteSpace(doc.FilePath))
                {
                    continue;
                }
                if (!IsTenantFilePath(doc.FilePath, tenantId))
                {
                    continue;
                }

                var indexed = await UpsertIndexAsync(doc);
                if (indexed != null)
                {
                    count += 1;
                }
            }

            return count;
        }

        private bool IsTenantFilePath(string relativePath, string? tenantId)
        {
            var normalizedRelativePath = _fileStorage.NormalizeRelativePath(relativePath);

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                var expectedPrefix = $"uploads/{tenantId}/";
                return normalizedRelativePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        public static List<string> TokenizeQuery(string input, int maxTokens = 8)
        {
            return Tokenize(NormalizeContent(input), maxTokens);
        }

        private static string NormalizeContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            return content.ToLowerInvariant();
        }

        private static List<string> Tokenize(string normalized, int maxTokens = MaxTokenCount)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return new List<string>();
            var matches = Regex.Matches(normalized, "[a-z0-9]+");
            var tokens = new HashSet<string>();
            foreach (Match match in matches)
            {
                var value = match.Value;
                if (value.Length < MinTokenLength) continue;
                if (tokens.Add(value) && tokens.Count >= maxTokens)
                {
                    break;
                }
            }
            return tokens.ToList();
        }

        private async Task<string> LoadContentAsync(Document document)
        {
            try
            {
                var storedBytes = await _fileStorage.ReadBytesAsync(document.FilePath);
                if (document.IsEncrypted)
                {
                    if (string.IsNullOrWhiteSpace(document.EncryptionIv) || string.IsNullOrWhiteSpace(document.EncryptionTag))
                    {
                        return string.Empty;
                    }

                    var plaintext = _documentEncryptionService.DecryptBytes(storedBytes, document.EncryptionIv, document.EncryptionTag);
                    return await _textExtractor.ExtractTextAsync(plaintext, document.FileName);
                }

                return await _textExtractor.ExtractTextAsync(storedBytes, document.FileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
