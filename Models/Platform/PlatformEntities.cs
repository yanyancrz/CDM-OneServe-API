using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.Platform;

// =========================================================
// ADMIN / SUPER ADMIN PLATFORM TABLES
//
// Everything here is additive. The existing `users`, module tables and
// `admin_activity_logs` keep working exactly as before; these tables only
// hold the central admin concerns that had nowhere to live:
//
//   role_permissions          - what a role may do in the admin portal
//   user_permission_overrides - one-off grants/removals for one account
//   security_events           - login failures, resets, revocations
//   system_settings           - platform-wide configuration
//   system_health_checks      - history behind the monitoring page
//   database_backup_records   - backup requests and their real status
//   database_restore_requests - two-person restore workflow
//   clinic_integration_logs   - Clinic CMS calls, secrets never stored
// =========================================================

/// <summary>
/// One capability granted to a role at a given level.
///
/// A missing row means "use the built-in default" (see
/// <see cref="PermissionCatalog"/>), so the table stays small: only the
/// capabilities an admin has actually changed are stored.
/// </summary>
[Table("role_permissions")]
public class RolePermission
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Role { get; set; } = "";

    /// <summary>One of <see cref="PermissionLevels"/>.</summary>
    [MaxLength(40)]
    public string PermissionKey { get; set; } = "";

    /// <summary>"none", "view", "manage" or "full_control".</summary>
    [MaxLength(20)]
    public string Level { get; set; } = PermissionLevels.None;

    public int? UpdatedByUserId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A capability granted to, or removed from, a single account.
/// Wins over the role row. Used for "special permissions assigned to
/// individual users" (Part 4).
/// </summary>
[Table("user_permission_overrides")]
public class UserPermissionOverride
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(40)]
    public string PermissionKey { get; set; } = "";

    /// <summary>"none" removes the capability; anything else grants it.</summary>
    [MaxLength(20)]
    public string Level { get; set; } = PermissionLevels.None;

    /// <summary>Why the override exists. Required by the API.</summary>
    [MaxLength(500)]
    public string Reason { get; set; } = "";

    public int? GrantedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional automatic expiry. Null means "until removed".</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// A security-relevant event. Append-only, like the audit log, and
/// deliberately free of passwords, tokens and message contents.
/// </summary>
[Table("security_events")]
public class SecurityEvent
{
    public long Id { get; set; }

    /// <summary>Null for a failed login against an address that has no account.</summary>
    public int? UserId { get; set; }

    [MaxLength(50)]
    public string? IdNumber { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>See <see cref="SecurityEventTypes"/>.</summary>
    [MaxLength(60)]
    public string EventType { get; set; } = "";

    /// <summary>"info", "warning" or "critical".</summary>
    [MaxLength(20)]
    public string Severity { get; set; } = SecuritySeverities.Info;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(120)]
    public string? DeviceLabel { get; set; }

    /// <summary>Sanitised human-readable detail. Never a secret.</summary>
    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class SecurityEventTypes
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string LoginBlocked = "login_blocked";
    public const string LockedOut = "account_locked";
    public const string PasswordResetRequested = "password_reset_requested";
    public const string PasswordResetCompleted = "password_reset_completed";
    public const string PasswordChanged = "password_changed";
    public const string SessionRevoked = "session_revoked";
    public const string SignedOutEverywhere = "signed_out_everywhere";
    public const string PrivilegedAccessDenied = "privileged_access_denied";
    public const string RoleChanged = "role_changed";
    public const string AccountStatusChanged = "account_status_changed";
    public const string NewDeviceLogin = "new_device_login";
}

public static class SecuritySeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}

/// <summary>
/// Platform-wide configuration. One row per key; unknown keys are rejected
/// on write so a typo cannot silently create a setting nothing reads.
/// </summary>
[Table("system_settings")]
public class SystemSetting
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = "";

    [MaxLength(2000)]
    public string Value { get; set; } = "";

    /// <summary>"string", "bool", "int", "json".</summary>
    [MaxLength(20)]
    public string ValueType { get; set; } = "string";

    [MaxLength(60)]
    public string Category { get; set; } = "general";

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>When true the value is never returned raw by the API.</summary>
    public bool IsSecret { get; set; }

    public int? UpdatedByUserId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One health probe result. Rows accumulate so the monitoring page can show
/// a real history instead of an invented graph. Pruned by the API on read.
/// </summary>
[Table("system_health_checks")]
public class SystemHealthSnapshot
{
    public long Id { get; set; }

    /// <summary>"api", "database", "clinic_cms", "web_push", "signalr".</summary>
    [MaxLength(40)]
    public string Component { get; set; } = "";

    /// <summary>"online", "degraded", "offline", "unknown".</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "unknown";

    public int? LatencyMs { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A backup request. The API never invents a "Completed" row: a record only
/// reaches Completed when a real external job (Azure / DBA) reports it back.
/// Secrets and connection strings are never stored here.
/// </summary>
[Table("database_backup_records")]
public class DatabaseBackupRecord
{
    public int Id { get; set; }

    /// <summary>"requested", "running", "completed", "failed", "cancelled".</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "requested";

    /// <summary>"azure_managed", "manual_dump", "external_job".</summary>
    [MaxLength(30)]
    public string Source { get; set; } = "azure_managed";

    public int? RequestedByUserId { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>Opaque reference (backup name / blob path), never credentials.</summary>
    [MaxLength(300)]
    public string? StorageReference { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// A database restore request. Requires a SuperAdmin requester, a second
/// approver, and a typed confirmation phrase. Nothing executes a restore
/// automatically - the record is the authorisation trail for a human DBA.
/// </summary>
[Table("database_restore_requests")]
public class DatabaseRestoreRequest
{
    public int Id { get; set; }

    public int? BackupRecordId { get; set; }

    public int RequestedByUserId { get; set; }

    [MaxLength(1000)]
    public string Reason { get; set; } = "";

    /// <summary>"pending", "approved", "rejected", "cancelled", "completed".</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public int? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public int? RejectedByUserId { get; set; }

    public DateTime? RejectedAt { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One call to the external Clinic CMS. Stored so the integration page can
/// show real response times and failure reasons. The integration key is
/// never written here, and no medical payload is ever logged.
/// </summary>
[Table("clinic_integration_logs")]
public class ClinicIntegrationLog
{
    public long Id { get; set; }

    public int? UserId { get; set; }

    [MaxLength(40)]
    public string Resource { get; set; } = "";

    [MaxLength(10)]
    public string Method { get; set; } = "GET";

    /// <summary>Path only - never the key, query string or student data.</summary>
    [MaxLength(300)]
    public string Endpoint { get; set; } = "";

    public int? StatusCode { get; set; }

    public int DurationMs { get; set; }

    public bool Success { get; set; }

    [MaxLength(300)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
