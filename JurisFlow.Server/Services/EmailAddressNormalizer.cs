namespace JurisFlow.Server.Services
{
    public static class EmailAddressNormalizer
    {
        public static string Normalize(string? email)
        {
            return string.IsNullOrWhiteSpace(email)
                ? string.Empty
                : email.Trim().ToLowerInvariant();
        }
    }
}
