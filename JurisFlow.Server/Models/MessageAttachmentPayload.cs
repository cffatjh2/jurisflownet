namespace JurisFlow.Server.Models
{
    public class MessageAttachmentPayload
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
