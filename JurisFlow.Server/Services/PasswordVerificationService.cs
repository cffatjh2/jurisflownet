using System.Data;
using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class PasswordVerificationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<PasswordVerificationService> _logger;

        public PasswordVerificationService(
            JurisFlowDbContext context,
            ILogger<PasswordVerificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> VerifyAsync(string password, string? storedHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            {
                return false;
            }

            try
            {
                if (BCrypt.Net.BCrypt.Verify(password, storedHash))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "BCrypt verification threw for stored password hash.");
            }

            if (!_context.Database.IsNpgsql())
            {
                return false;
            }

            try
            {
                return await VerifyWithDatabaseCryptAsync(password, storedHash, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback database crypt verification failed.");
                return false;
            }
        }

        private async Task<bool> VerifyWithDatabaseCryptAsync(string password, string storedHash, CancellationToken cancellationToken)
        {
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "select crypt(@password, @hash) = @hash";

                var passwordParameter = command.CreateParameter();
                passwordParameter.ParameterName = "@password";
                passwordParameter.Value = password;
                command.Parameters.Add(passwordParameter);

                var hashParameter = command.CreateParameter();
                hashParameter.ParameterName = "@hash";
                hashParameter.Value = storedHash;
                command.Parameters.Add(hashParameter);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result switch
                {
                    bool boolResult => boolResult,
                    string stringResult when bool.TryParse(stringResult, out var parsed) => parsed,
                    _ => false
                };
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
