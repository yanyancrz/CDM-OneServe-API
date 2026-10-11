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

public class GuidanceCounselingRecord
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public GuidanceAppointment? Appointment { get; set; }
    public int StudentId { get; set; }
    public User? Student { get; set; }
    public int CounselorId { get; set; }
    public User? Counselor { get; set; }

    public DateOnly SessionDate { get; set; }
    public string? ConcernType { get; set; }
    public string? SessionSummary { get; set; }
    public string? PrivateNotes { get; set; }
    public bool IsConfidential { get; set; }

    /// <summary>Draft or Finalized.</summary>
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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