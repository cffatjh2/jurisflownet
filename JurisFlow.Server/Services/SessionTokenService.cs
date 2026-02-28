using System.Security.Cryptography;
using System.Text;

namespace JurisFlow.Server.Services
{
    public class SessionTokenService
    {
        public string GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        public string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public bool VerifyToken(string token, string? hash)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(hash)) return false;
            var computed = HashToken(token);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(hash));
        }
    }
}
