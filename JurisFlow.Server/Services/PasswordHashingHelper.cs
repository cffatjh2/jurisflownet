namespace JurisFlow.Server.Services
{
    public static class PasswordHashingHelper
    {
        private const int DefaultWorkFactor = 12;
        private const int MinWorkFactor = 10;
        private const int MaxWorkFactor = 16;

        public static string HashPassword(string password, IConfiguration configuration)
        {
            var configuredWorkFactor = configuration.GetValue("Security:BcryptWorkFactor", DefaultWorkFactor);
            var workFactor = Math.Clamp(configuredWorkFactor, MinWorkFactor, MaxWorkFactor);
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: workFactor);
        }
    }
}
