using System.Text.RegularExpressions;

namespace JurisFlow.Server.Services
{
    public class PasswordPolicyService
    {
        private readonly int _minLength;
        private readonly bool _requireUpper;
        private readonly bool _requireLower;
        private readonly bool _requireDigit;
        private readonly bool _requireSymbol;
        private readonly HashSet<string> _commonPasswords;

        public PasswordPolicyService(IConfiguration configuration)
        {
            _minLength = configuration.GetValue("Security:PasswordMinLength", 12);
            _requireUpper = configuration.GetValue("Security:PasswordRequireUpper", true);
            _requireLower = configuration.GetValue("Security:PasswordRequireLower", true);
            _requireDigit = configuration.GetValue("Security:PasswordRequireDigit", true);
            _requireSymbol = configuration.GetValue("Security:PasswordRequireSymbol", true);

            _commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password",
                "password123",
                "password123!",
                "jurisflow",
                "jurisflow123",
                "admin123",
                "welcome",
                "welcome123",
                "qwerty",
                "qwerty123",
                "letmein",
                "changeme",
                "test1234",
                "iloveyou",
                "123456",
                "12345678",
                "abcdefg",
                "abc123",
                "1q2w3e4r",
                "summer2024",
                "winter2024"
            };
        }

        public PasswordValidationResult Validate(string? password, string? email = null, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return PasswordValidationResult.Invalid("Password is required.");
            }

            if (password.Length < _minLength)
            {
                return PasswordValidationResult.Invalid($"Password must be at least {_minLength} characters.");
            }

            if (Regex.IsMatch(password, "\\s"))
            {
                return PasswordValidationResult.Invalid("Password cannot contain whitespace.");
            }

            if (_requireUpper && !password.Any(char.IsUpper))
            {
                return PasswordValidationResult.Invalid("Password must include at least one uppercase letter.");
            }

            if (_requireLower && !password.Any(char.IsLower))
            {
                return PasswordValidationResult.Invalid("Password must include at least one lowercase letter.");
            }

            if (_requireDigit && !password.Any(char.IsDigit))
            {
                return PasswordValidationResult.Invalid("Password must include at least one number.");
            }

            if (_requireSymbol && !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return PasswordValidationResult.Invalid("Password must include at least one symbol.");
            }

            var normalized = password.Trim().ToLowerInvariant();
            if (_commonPasswords.Contains(normalized))
            {
                return PasswordValidationResult.Invalid("Password is too common. Choose a stronger password.");
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var local = email.Split('@')[0].Trim().ToLowerInvariant();
                if (local.Length >= 3 && normalized.Contains(local))
                {
                    return PasswordValidationResult.Invalid("Password should not include your email.");
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in tokens)
                {
                    if (token.Length >= 3 && normalized.Contains(token.ToLowerInvariant()))
                    {
                        return PasswordValidationResult.Invalid("Password should not include your name.");
                    }
                }
            }

            return PasswordValidationResult.Valid();
        }
    }

    public class PasswordValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }

        private PasswordValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        public static PasswordValidationResult Valid()
        {
            return new PasswordValidationResult(true, string.Empty);
        }

        public static PasswordValidationResult Invalid(string message)
        {
            return new PasswordValidationResult(false, message);
        }
    }
}
