using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

/// <summary>
/// User's 2FA status and overview.
/// </summary>
public class TwoFactorUserStatus
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("methodCount")]
    public int MethodCount { get; set; }

    [JsonPropertyName("availableMethods")]
    public List<string> AvailableMethods { get; set; } = new();

    [JsonPropertyName("primaryMethod")]
    public string? PrimaryMethod { get; set; }

    [JsonPropertyName("recoveryCodesRemaining")]
    public int RecoveryCodesRemaining { get; set; }

    [JsonPropertyName("trustedDevicesCount")]
    public int TrustedDevicesCount { get; set; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// A 2FA credential registered by the user.
/// </summary>
public class TwoFactorCredential
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("providerType")]
    public string ProviderType { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }

    [JsonPropertyName("maskedEmail")]
    public string? MaskedEmail { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Result of email 2FA enrollment initiation.
/// </summary>
public class EmailEnrollmentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("maskedEmail")]
    public string MaskedEmail { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Result of authenticator app enrollment initiation.
/// </summary>
public class AuthenticatorEnrollmentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;

    [JsonPropertyName("qrCodeDataUrl")]
    public string QrCodeDataUrl { get; set; } = string.Empty;

    [JsonPropertyName("manualEntryKey")]
    public string ManualEntryKey { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("accountName")]
    public string? AccountName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Information about the user's recovery codes.
/// </summary>
public class RecoveryCodeInfo
{
    [JsonPropertyName("totalGenerated")]
    public int TotalGenerated { get; set; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTime? GeneratedAt { get; set; }
}

/// <summary>
/// Result of regenerating recovery codes.
/// </summary>
public class RegenerateRecoveryCodesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("codes")]
    public List<string> Codes { get; set; } = new();

    [JsonPropertyName("totalCodes")]
    public int TotalCodes { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// A trusted device registered by the user.
/// </summary>
public class TrustedDevice
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("isExpired")]
    public bool IsExpired { get; set; }
}
