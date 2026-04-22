namespace JurisFlow.Server.DTOs
{
    public class OAuthCodeDto
    {
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public class OAuthStateRequestDto
    {
        public string Provider { get; set; } = string.Empty;
        public string? Target { get; set; }
        public string? ReturnPath { get; set; }
    }

    public class OAuthRefreshDto
    {
        public string? RefreshToken { get; set; }
        public string? Target { get; set; }
    }
}
