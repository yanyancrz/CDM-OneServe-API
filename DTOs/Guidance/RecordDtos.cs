using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

// ---------------- Session records (counselor writes these after a Completed session) ----------------

public class SessionRecordDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string? StudentNumber { get; set; }
    public string Role { get; set; } = "Student";

    public string SessionDate { get; set; } = "";
    public string? ConcernType { get; set; }
    public string? SessionSummary { get; set; }
    public string? PrivateNotes { get; set; }
    public bool IsConfidential { get; set; }

    /// <summary>Draft or Finalized.</summary>
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertSessionRecordRequest
{
    public string? ConcernType { get; set; }

    [StringLength(4000)]
    public string? SessionSummary { get; set; }

    /// <summary>Confidential — never shown to the student.</summary>
    [StringLength(4000)]
    public string? PrivateNotes { get; set; }

    public bool IsConfidential { get; set; }

    /// <summary>Draft or Finalized.</summary>
    public string Status { get; set; } = "Draft";
}

// ---------------- Follow-ups ----------------

public class FollowUpDto
{
    public int Id { get; set; }
    public int? RecordId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string? StudentNumber { get; set; }
    public string Role { get; set; } = "Student";
    public int CounselorId { get; set; }
    public string? CounselorName { get; set; }

    public string FollowUpDate { get; set; } = "";
    public string? FollowUpTime { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Pending, Accepted, Declined, Completed, Cancelled.</summary>
    public string Status { get; set; } = "Pending";
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateFollowUpRequest
{
    [Required] public int StudentId { get; set; }
    public int? RecordId { get; set; }

    [Required] public string FollowUpDate { get; set; } = "";
    public string? FollowUpTime { get; set; }

    [StringLength(1000)]
    public string? Reason { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class RespondFollowUpRequest
{
    /// <summary>Accepted or Declined.</summary>
    [Required] public string Status { get; set; } = "";
}

// ---------------- Student Records / Case Monitoring ----------------

public class UpdateCounselorProfileRequest
{
    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string[]? Specializations { get; set; }
    public string? Bio { get; set; }
}

// ---------------- Reports (counselor stats) ----------------

public class ConcernCountDto
{
    public string Concern { get; set; } = "";
    public int Count { get; set; }
}

public class MonthCountDto
{
    /// <summary>e.g. "2026-10".</summary>
    public string Month { get; set; } = "";
    public int Total { get; set; }
    public int Completed { get; set; }
}

public class RoleCountDto
{
    public string Role { get; set; } = "";
    public int Clients { get; set; }
}

public class CounselorReportDto
{
    public int TotalAppointments { get; set; }
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Rejected { get; set; }
    public int Expired { get; set; }
    public int TotalClients { get; set; }
    public int FollowUpsPending { get; set; }
    public int FollowUpsAccepted { get; set; }
    public int FollowUpsDeclined { get; set; }
    public List<ConcernCountDto> TopConcerns { get; set; } = new();
    public List<MonthCountDto> Monthly { get; set; } = new();
    public List<RoleCountDto> ByRole { get; set; } = new();
}

public class StudentCaseDto
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string? StudentNumber { get; set; }
    public string Role { get; set; } = "Student";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }
    public string? Position { get; set; }

    public int TotalAppointments { get; set; }
    public int CompletedSessions { get; set; }
    public string? LastSessionDate { get; set; }
    public List<string> Concerns { get; set; } = new();
    public int PendingFollowUps { get; set; }
}

public class StudentCaseDetailDto
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string? StudentNumber { get; set; }
    public string Role { get; set; } = "Student";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }
    public string? Position { get; set; }
    public string? Email { get; set; }

    public List<SessionRecordDto> Records { get; set; } = new();
    public List<FollowUpDto> FollowUps { get; set; } = new();
}
