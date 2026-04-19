using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Contracts
{
    public abstract class RejectUnknownFieldsRequestBase : IValidatableObject
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (AdditionalProperties is { Count: > 0 })
            {
                var keys = string.Join(", ", AdditionalProperties.Keys.OrderBy(static key => key, StringComparer.Ordinal));
                yield return new ValidationResult(
                    $"Unsupported fields were supplied: {keys}.",
                    new[] { nameof(AdditionalProperties) });
            }
        }
    }
}
