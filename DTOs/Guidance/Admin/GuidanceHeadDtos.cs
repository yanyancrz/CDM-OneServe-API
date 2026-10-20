using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance.Admin;

// =====================================================
// Shared
// =====================================================

/// <summary>Label + count, the shape every "break it down" report uses.</summary>
public class GuidanceLabelCountDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

// =====================================================
// GET /api/admin/guidance/me
// =====================================================

public class GuidanceHeadAccessDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public string AccountStatus { get; set; } = "";
    public string? AdminModule { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>UI-level permissions. The API enforces the same rules on its own.</summary>
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

// =====================================================
// GET /api/admin/guidance/dashboard/overview
// =====================================================

public class GuidanceCounselorStatDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public int NeverSignedIn { get; set; }
}

public class GuidanceStudentStatDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public int NewThisMonth { get; set; }
    public int NeverSignedIn { get; set; }
}

public class GuidanceAppointmentStatDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Confirmed { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Rejected { get; set; }
    public int Expired { get; set; }
    public int Today { get; set; }
}

public class GuidanceSessionStatDto
{
    public int Records { get; set; }
    public int Draft { get; set; }
    public int Finalized { get; set; }
    public int Confidential { get; set; }
    public int FollowUpsPending { get; set; }
}

public class GuidanceChatStatDto
{
    public int Conversations { get; set; }
    public int Messages { get; set; }
    public int MessagesLast24Hours { get; set; }
    public int UnreadMessages { get; set; }
}

public class GuidanceActivityDto
{
    /// <summary>appointment | session | account | chat | follow-up</summary>
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime At { get; set; }
}

public class GuidanceHeadOverviewDto
{
    public GuidanceCounselorStatDto Counselors { get; set; } = new();
    public GuidanceStudentStatDto Students { get; set; } = new();
    public GuidanceAppointmentStatDto Appointments { get; set; } = new();
    public GuidanceSessionStatDto Sessions { get; set; } = new();
    public GuidanceChatStatDto Chat { get; set; } = new();
    public List<GuidanceActivityDto> RecentActivity { get; set; } = new();
}

// =====================================================
// Counselors
// =====================================================

public class GuidanceCounselorListItemDto
{
    public int UserId { get; set; }
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ProfilePicture { get; set; }
    public string AccountStatus { get; set; } = "";
    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Department { get; set; }
    public string? Phone { get; set; }
    /// <summary>Comma separated specializations, ready to display.</summary>
    public string? Specializations { get; set; }
    public bool HasBio { get; set; }
    public int AvailabilityDays { get; set; }
    public int Appointments { get; set; }
    public int CompletedSessions { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GuidanceAvailabilitySlotDto
{
    /// <summary>0 = Sunday ... 6 = Saturday (matches Date.DayOfWeek).</summary>
    public int DayOfWeek { get; set; }
    public string DayLabel { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public bool IsActive { get; set; }
}

public class GuidanceCounselorDetailDto : GuidanceCounselorListItemDto
{
    public string? Bio { get; set; }
    public List<GuidanceAvailabilitySlotDto> Availability { get; set; } = new();
    public GuidanceAppointmentStatDto AppointmentsByStatus { get; set; } = new();
}

public class GuidanceCounselorFilterDto
{
    public string? Search { get; set; }
    /// <summary>empty = any, otherwise Active / Suspended / Pending / Rejected.</summary>
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class UpdateCounselorRequest
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = "";

    [StringLength(60)] public string? Title { get; set; }
    [StringLength(60)] public string? Room { get; set; }
    [StringLength(60)] public string? Department { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(2000)] public string? Bio { get; set; }
    public string[]? Specializations { get; set; }
}

public class UpdateAccountStatusRequest
{
    /// <summary>Active, Suspended, Pending or Rejected.</summary>
    [Required]
    public string Status { get; set; } = "Active";
}

// =====================================================
// Students (read-only directory)
// =====================================================

public class GuidanceStudentListItemDto
{
    public int UserId { get; set; }
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string AccountStatus { get; set; } = "";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }
    public bool ProfileComplete { get; set; }
    public int Appointments { get; set; }
    public int ChatConversations { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GuidanceStudentDetailDto : GuidanceStudentListItemDto
{
    public string? ContactNumber { get; set; }
    public string? StudentStatus { get; set; }
    public List<GuidanceStudentAppointmentSummaryDto> AppointmentHistory { get; set; } = new();
}

public class GuidanceStudentAppointmentSummaryDto
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string TimeSlot { get; set; } = "";
    public string ConcernType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CounselorName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GuidanceStudentFilterDto
{
    public string? Search { get; set; }
    /// <summary>empty = Student + Faculty, otherwise Student or Faculty.</summary>
    public string? Role { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

// =====================================================
// Account  (the signed-in Guidance Head's own password)
// =====================================================

public class GuidanceChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

// =====================================================
// Reports
// =====================================================

public class GuidanceCounselorPerformanceDto
{
    public string FullName { get; set; } = "";
    public int Appointments { get; set; }
    public int Completed { get; set; }
    public int Sessions { get; set; }
}

public class GuidanceAccountActivityDto
{
    public int NeverSignedInStudents { get; set; }
    public int NeverSignedInCounselors { get; set; }
    public int SignedInLast7Days { get; set; }
    public int SignedInLast30Days { get; set; }
}

public class GuidanceReportsDto
{
    public int Days { get; set; } = 30;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<GuidanceLabelCountDto> AppointmentsByStatus { get; set; } = new();
    public List<GuidanceLabelCountDto> AppointmentsByConcern { get; set; } = new();
    public List<GuidanceLabelCountDto> AppointmentsByMonth { get; set; } = new();
    public List<GuidanceLabelCountDto> SessionsByMonth { get; set; } = new();
    public List<GuidanceCounselorPerformanceDto> Counselors { get; set; } = new();
    public GuidanceAccountActivityDto Accounts { get; set; } = new();
}
