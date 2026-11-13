using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models;

public class Announcement
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;

    // =========================================================
    // AUDIENCE + SCHEDULING (central admin announcements)
    //
    // Additive columns: existing announcements keep working with the
    // defaults below ("All", no schedule, not archived). A scheduled
    // announcement is published by the admin page at PublishAt; the
    // public feed only returns rows that are active, not archived, and
    // whose PublishAt is null or in the past.
    // =========================================================

    /// <summary>"All", "Students", "Faculty" or "Staff".</summary>
    [MaxLength(40)]
    public string TargetAudience { get; set; } = "All";

    /// <summary>When it should go live. Null means "immediately".</summary>
    public DateTime? PublishAt { get; set; }

    public bool IsArchived { get; set; }

    public int? ArchivedByUserId { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
