using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

/// <summary>
/// A passkey registered to the signed-in user (api/webauthn/credentials). Mirrors the backend UserPasskeyDto.
/// </summary>
public class UserPasskey
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; set; }

    [JsonPropertyName("deviceInfo")]
    public string? DeviceInfo { get; set; }

    [JsonPropertyName("isPlatformAuthenticator")]
    public bool IsPlatformAuthenticator { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>The name to show: the user's own name for it, else the kind of authenticator.</summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(FriendlyName)
        ? FriendlyName!
        : IsPlatformAuthenticator ? "This device (built-in)" : "Security key";
}

/// <summary>Raised by passkey management calls; the message is safe to show to the user.</summary>
public class PasskeyManagementException : Exception
{
    public PasskeyManagementException(string message, Exception? inner = null) : base(message, inner) { }
}
