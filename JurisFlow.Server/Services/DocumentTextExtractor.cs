using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace JurisFlow.Server.Services
{
    public class DocumentTextExtractor
    {
        private const long MaxFileSizeBytes = 25 * 1024 * 1024;

        public async Task<string> ExtractTextAsync(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length > MaxFileSizeBytes)
                {
                    return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".txt" || ext == ".md")
            {
                return await File.ReadAllTextAsync(filePath);
            }
            if (ext == ".docx")
            {
                return ExtractDocxText(filePath);
            }
            if (ext == ".pdf")
            {
                return ExtractPdfText(filePath);
            }

            return string.Empty;
        }

        public async Task<string> ExtractTextAsync(byte[] content, string? fileName)
        {
            if (content.Length > MaxFileSizeBytes)
            {
                return string.Empty;
            }

            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            if (ext == ".txt" || ext == ".md")
            {
                return Encoding.UTF8.GetString(content);
            }
            if (ext == ".docx")
            {
                return ExtractDocxText(content);
            }
            if (ext == ".pdf")
            {
                return ExtractPdfText(content);
            }

            return string.Empty;
        }

        private static string ExtractDocxText(string filePath)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;
                return body.InnerText ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractDocxText(byte[] content)
        {
            try
            {
                using var stream = new MemoryStream(content);
                using var doc = WordprocessingDocument.Open(stream, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return string.Empty;
                return body.InnerText ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractPdfText(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                using var doc = PdfDocument.Open(filePath);
                foreach (Page page in doc.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractPdfText(byte[] content)
        {
            try
            {
                var sb = new StringBuilder();
                using var stream = new MemoryStream(content);
                using var doc = PdfDocument.Open(stream);
                foreach (Page page in doc.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
