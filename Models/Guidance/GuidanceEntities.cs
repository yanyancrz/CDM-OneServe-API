using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Models.Guidance;

// All Guidance tables reference the EXISTING Users table (Users.Id).
// There is no Guidance users / roles / reset-token table.

public class GuidanceCounselorProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    /// <summary>Comma separated, e.g. "Academic,Personal,Career".</summary>
    public string? Specializations { get; set; }
    public string? Bio { get; set; }

    /// <summary>
    /// True once the default availability schedule has been seeded for this
    /// counselor. Prevents re-seeding after the counselor intentionally
    /// deletes all blocks.
    /// </summary>
    public bool AvailabilitySeeded { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GuidanceAvailability
{
    public int Id { get; set; }
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }

    /// <summary>1 = Monday ... 5 = Friday (weekday-only rule from the original module).</summary>
    public int DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;
}

public class GuidanceAppointment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User? Student { get; set; }
    public int? CounselorId { get; set; }
    public User? Counselor { get; set; }

    public DateOnly Date { get; set; }
    public string TimeSlot { get; set; } = "";
    public string? ConcernType { get; set; }
    public string? Notes { get; set; }

    /// <summary>Pending, Confirmed, Arrived, NoShow, Cancelled, Completed, Rejected, Expired.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime? CounselorNotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One-date unavailability (a day off). The weekly availability blocks keep
/// repeating automatically - a day-off only closes the single date, so the
/// next week is back to the default schedule with nothing to restore.
/// </summary>
public class GuidanceDayOff
{
    public int Id { get; set; }
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }

    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GuidanceAppointmentReschedule
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public GuidanceAppointment? Appointment { get; set; }
    public int? RescheduledBy { get; set; }
    public User? RescheduledByUser { get; set; }

    public DateOnly OldDate { get; set; }
    public string OldTimeSlot { get; set; } = "";
    public DateOnly NewDate { get; set; }
    public string NewTimeSlot { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Instructions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A counseling record is also the canonical narrative report. Records written
/// from a completed appointment keep their AppointmentId; a counselor may also
/// write a report about a student/faculty member without an appointment, in
/// which case AppointmentId is null.
/// </summary>
public class GuidanceCounselingRecord
{
    public int Id { get; set; }

    /// <summary>Null for a narrative report written outside an appointment.</summary>
    public int? AppointmentId { get; set; }
    public GuidanceAppointment? Appointment { get; set; }

    /// <summary>The subject: an existing Users row (Student or Faculty).</summary>
    public int StudentId { get; set; }
    public User? Student { get; set; }

    /// <summary>Always the authenticated counselor from the JWT.</summary>
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }

    public DateOnly SessionDate { get; set; }
    public string? ConcernType { get; set; }
    public string? SessionSummary { get; set; }
    public string? PrivateNotes { get; set; }
    public bool IsConfidential { get; set; }

    // ---- Narrative report fields -------------------------------------
    public string? ReportTitle { get; set; }
    public string? SessionObjectives { get; set; }
    public string? Observations { get; set; }
    public string? Interventions { get; set; }
    public string? Recommendations { get; set; }
    public string? FollowUpPlan { get; set; }

    /// <summary>Human reference assigned when the report is finalized.</summary>
    public string? ReportNumber { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>Draft, Finalized or Archived.</summary>
    public string Status { get; set; } = "Draft";

    public DateTime? FinalizedAt { get; set; }
    public int? FinalizedBy { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public int? ArchivedBy { get; set; }
    public int? LastEditedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<GuidanceNarrativeAttachment> Attachments { get; set; } = new();
    public List<GuidanceNarrativeReportEvent> Events { get; set; } = new();
}

/// <summary>
/// A supporting file for a narrative report. Files are stored OUTSIDE wwwroot
/// (App_Data/guidance-narrative) and are only reachable through an authorized
/// download endpoint that writes an audit event.
/// </summary>
public class GuidanceNarrativeAttachment
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public GuidanceCounselingRecord? Record { get; set; }

    public string FileName { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";

    public int UploadedBy { get; set; }
    public User? UploadedByUser { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Immutable audit trail for a narrative report: create/update/finalize/archive,
/// admin views, attachment downloads and exports.
/// </summary>
public class GuidanceNarrativeReportEvent
{
    public long Id { get; set; }
    public int RecordId { get; set; }
    public GuidanceCounselingRecord? Record { get; set; }

    public int? ActorId { get; set; }
    public User? Actor { get; set; }
    public string ActorRole { get; set; } = "";

    /// <summary>Created, Updated, Finalized, Archived, Viewed, Exported, AttachmentAdded, AttachmentDownloaded, AttachmentDeleted.</summary>
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GuidanceFollowUp
{
    public int Id { get; set; }
    public int? RecordId { get; set; }
    public GuidanceCounselingRecord? Record { get; set; }
    public int StudentId { get; set; }
    public User? Student { get; set; }
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }

    public DateOnly FollowUpDate { get; set; }
    public string? FollowUpTime { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Pending, Accepted, Declined, Completed, Cancelled.</summary>
    public string Status { get; set; } = "Pending";
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class GuidanceChatConversation
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public User? Student { get; set; }
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastMessageAt { get; set; }
}

public class GuidanceChatMessage
{
    public long Id { get; set; }
    public int ConversationId { get; set; }
    public GuidanceChatConversation? Conversation { get; set; }
    public int SenderId { get; set; }
    public User? Sender { get; set; }

    public string Body { get; set; } = "";
    /// <summary>Client generated id so a retried send is recognised (idempotency).</summary>
    public Guid? ClientId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

public class GuidanceSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
}