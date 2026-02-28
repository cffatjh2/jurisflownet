using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationSecret
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ConnectionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        public string SecretJson { get; set; } = "{}";

        [Required]
        [MaxLength(64)]
        public string EncryptionProvider { get; set; } = "aes256-gcm";

        [Required]
        [MaxLength(128)]
        public string EncryptionKeyId { get; set; } = "v1";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
