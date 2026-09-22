namespace WildwoodComponents.Shared.Models;

// Port of RegistrationTokenAppGrant / RegistrationTokenDetails in
// packages/wildwood-core/src/auth/types.ts — what
// GET api/registrationtokens/validate-detailed/{token} answers.

/// <summary>
/// One app's plan carried by a registration token: the tier, its pricing, the packs and the
/// features the token grants, with the server's display names where it sent them.
/// </summary>
public class RegistrationTokenAppGrant
{
    public string AppId { get; set; } = string.Empty;
    public string? AppName { get; set; }
    public string AppTierId { get; set; } = string.Empty;
    public string? AppTierName { get; set; }
    public string? AppTierPricingId { get; set; }
    public string? PricingName { get; set; }
    public List<string> AddOnIds { get; set; } = new();
    public List<string>? AddOnNames { get; set; }
    public List<string> FeatureCodes { get; set; } = new();
    public List<string>? FeatureNames { get; set; }
}

/// <summary>
/// What a registration token grants, from the server's detailed token validation.
/// </summary>
public class RegistrationTokenDetails
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Per-app plans the token carries. Empty when the token only grants app access.
    /// </summary>
    public List<RegistrationTokenAppGrant> AppGrants { get; set; } = new();
}
