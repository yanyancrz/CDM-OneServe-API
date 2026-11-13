using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models;

[Table("admin_activity_logs")]
public class AdminActivityLog
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.Now;

    // =========================================================
    // LOST & FOUND AUDIT DETAILS
    //
    // Every column is nullable: this table is shared with the
    // Library and other modules, whose rows never set them.
    // They let the Lost & Found Activity Log show WHO did WHAT
    // to WHICH record, and the before/after status, without ever
    // exposing a password or other secret.
    // =========================================================

    /// <summary>Owning module ("LostFound"). Null on legacy/other rows.</summary>
    public string? Module { get; set; }

    /// <summary>Actor snapshot at the time of the action.</summary>
    public string? ActorName { get; set; }

    public string? ActorRole { get; set; }

    /// <summary>"Report", "Claim", "Pickup", "Administrator".</summary>
    public string? EntityType { get; set; }

    /// <summary>Report id, claim id, schedule id or user id.</summary>
    public int? EntityId { get; set; }

    public string? PreviousStatus { get; set; }

    public string? NewStatus { get; set; }

    /// <summary>Free-text detail; never contains a password.</summary>
    public string? Details { get; set; }

    // =========================================================
    // CENTRAL AUDIT DETAILS (Admin / Super Admin portal)
    //
    // Additive and nullable, so every existing Library and Lost & Found
    // writer keeps working untouched. None of these fields may ever hold
    // a password, token, connection string, medical record or counseling
    // narrative - see AdminAuditService, which sanitises what it writes.
    // =========================================================

    /// <summary>High-level grouping shown in the audit page's filter.</summary>
    public string? Category { get; set; }

    /// <summary>Caller IP, truncated. Never trusted for authorisation.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Opaque per-login device key, for correlating one session.</summary>
    public string? SessionKey { get; set; }

    /// <summary>Why a manual change was made. Required by the admin API.</summary>
    public string? Reason { get; set; }

    /// <summary>Previous value when the action changed one (sanitised).</summary>
    public string? PreviousValue { get; set; }

    /// <summary>New value when the action changed one (sanitised).</summary>
    public string? NewValue { get; set; }

    /// <summary>Groups rows written by one request, e.g. a bulk change.</summary>
    public string? CorrelationId { get; set; }
}