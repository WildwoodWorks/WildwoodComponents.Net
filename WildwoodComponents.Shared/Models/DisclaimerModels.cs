namespace WildwoodComponents.Shared.Models;

public class PendingDisclaimerModel
{
    public string DisclaimerId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DisclaimerType { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentFormat { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int? PreviouslyAcceptedVersion { get; set; }
    public string? ChangeNotes { get; set; }

    /// <summary>
    /// Local UI state: whether the user has checked the acceptance checkbox
    /// </summary>
    public bool IsAccepted { get; set; }
}

public class DisclaimerAcceptanceResult
{
    public string CompanyDisclaimerId { get; set; } = string.Empty;
    public string CompanyDisclaimerVersionId { get; set; } = string.Empty;
}

public class PendingDisclaimersResponse
{
    public bool HasPendingDisclaimers { get; set; }
    public List<PendingDisclaimerModel> Disclaimers { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class DisclaimerAcceptanceResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
