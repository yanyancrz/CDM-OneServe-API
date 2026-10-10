using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

public class BookAppointmentRequest
{
    [Required]
    public int CounselorId { get; set; }

    /// <summary>yyyy-MM-dd</summary>
    [Required]
    public string Date { get; set; } = "";

    /// <summary>"9:30 AM" - must be one of the counselor's real slots for that day.</summary>
    [Required, StringLength(20)]
    public string TimeSlot { get; set; } = "";

    [Required, StringLength(100)]
    public string ConcernType { get; set; } = "";

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class GuidanceScheduleBlockDto
{
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";   // HH:mm
    public string EndTime { get; set; } = "";
}

public class GuidanceCounselorDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ProfilePicture { get; set; }
    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string[] Specializations { get; set; } = Array.Empty<string>();
    public string? Bio { get; set; }
    public List<GuidanceScheduleBlockDto> Schedule { get; set; } = new();
}

public class GuidanceDayAvailabilityDto
{
    public bool OffDay { get; set; }
    public List<string> AllSlots { get; set; } = new();
    public List<string> AvailableSlots { get; set; } = new();
    public List<string> BookedSlots { get; set; } = new();
}

public class GuidanceRescheduleDto
{
    public int Id { get; set; }
    public string OldDate { get; set; } = "";
    public string OldTimeSlot { get; set; } = "";
    public string NewDate { get; set; } = "";
    public string NewTimeSlot { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Instructions { get; set; }
    public string? CounselorName { get; set; }
    public DateTime RescheduledAt { get; set; }
}

public class GuidanceStudentAppointmentDto
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string TimeSlot { get; set; } = "";
    public string? ConcernType { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? CounselorId { get; set; }
    public string? CounselorName { get; set; }
    public List<GuidanceRescheduleDto> Reschedules { get; set; } = new();
}