namespace JurisFlow.Server.DTOs
{
    public class AttachmentDto
    {
        public string? FileName { get; set; }
        public string? Name { get; set; }
        public long Size { get; set; }
        public string? Type { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
