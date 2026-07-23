using System;
using System.Collections.Generic;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>Outcome of running a single seed task.</summary>
    public enum SeederTaskStatus
    {
        /// <summary>The task created at least one resource.</summary>
        Created,
        /// <summary>Everything was already in the desired state — a pure no-op.</summary>
        AlreadyPresent,
        /// <summary>Existing resources were reconciled/updated (no creations).</summary>
        Updated,
        /// <summary>The task was skipped (already seeded at this version).</summary>
        Skipped,
        /// <summary>The task failed.</summary>
        Failed
    }

    /// <summary>A concrete thing a task installed — recorded in history for traceability.</summary>
    public sealed record SeededArtifact(string EntityType, string EntityId, string Description);

    /// <summary>Result of a seed task run.</summary>
    public sealed record SeederTaskResult(
        SeederTaskStatus Status,
        string Message,
        IReadOnlyList<SeededArtifact>? Artifacts = null)
    {
        public static SeederTaskResult Created(string message, IReadOnlyList<SeededArtifact>? artifacts = null) => new(SeederTaskStatus.Created, message, artifacts);
        public static SeederTaskResult AlreadyPresent(string message) => new(SeederTaskStatus.AlreadyPresent, message);
        public static SeederTaskResult Updated(string message, IReadOnlyList<SeededArtifact>? artifacts = null) => new(SeederTaskStatus.Updated, message, artifacts);
        public static SeederTaskResult Skipped(string message) => new(SeederTaskStatus.Skipped, message);
        public static SeederTaskResult Failed(string message, IReadOnlyList<SeededArtifact>? artifacts = null) => new(SeederTaskStatus.Failed, message, artifacts);

        /// <summary>True when the task changed something (Created or Updated).</summary>
        public bool WroteChanges => Status is SeederTaskStatus.Created or SeederTaskStatus.Updated;
    }

    // ===== Login DTOs (POST api/auth/login) =====

    public sealed class SeederLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
    }

    public sealed class SeederLoginResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string JwtToken { get; set; } = string.Empty;
        public bool RequiresTwoFactor { get; set; }
        public bool RequiresPasswordReset { get; set; }
    }

    // ===== Ledger / history / config DTOs (mirror WildwoodAPI DTOs.Seeder) =====

    public sealed class SeedTaskLedgerDto
    {
        public string Id { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Environment { get; set; } = "Default";
        public string TaskKey { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public int InstalledVersion { get; set; }
        public string Status { get; set; } = "Success";
        public DateTime LastRunAt { get; set; }
        public string? LastRunBy { get; set; }
        public string? CurrentHistoryId { get; set; }
    }

    public sealed class UpsertSeedLedgerRequest
    {
        public string Environment { get; set; } = "Default";
        public string TaskKey { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public int InstalledVersion { get; set; }
        public string Status { get; set; } = "Success";
        public string? LastRunBy { get; set; }
        public string? CurrentHistoryId { get; set; }
    }

    public sealed class SeedRunHistoryDto
    {
        public string Id { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Environment { get; set; } = "Default";
        public string TaskKey { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Status { get; set; } = "Created";
        public string Message { get; set; } = string.Empty;
        public string? ArtifactsJson { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string RunBy { get; set; } = "system:auto";
        public string? CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class RecordSeedRunRequest
    {
        public string Environment { get; set; } = "Default";
        public string TaskKey { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Status { get; set; } = "Created";
        public string Message { get; set; } = string.Empty;
        public string? ArtifactsJson { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string RunBy { get; set; } = "system:auto";
        public string? CorrelationId { get; set; }
    }

    public sealed class SeederConfigurationDto
    {
        public string Id { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        // False like the server-side defaults: one failed task must not silently dark the rest.
        public bool StopOnFirstFailure { get; set; } = false;
        public int MaxAttempts { get; set; } = 5;
        public int RetryDelaySeconds { get; set; } = 20;
    }
}
