using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

public class CounselorAppointmentDto
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string TimeSlot { get; set; } = "";
    public string? ConcernType { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int StudentId { get; set; }
    public string StudentName { get; set; } = "";
    public string? StudentEmail { get; set; }
    public string? StudentNumber { get; set; }
    // Whoever booked: a Student (Course / YearLevel) or Faculty (Institute / Position).
    public string Role { get; set; } = "Student";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }
    public string? Position { get; set; }
}

public class CounselorStatsDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Today { get; set; }
    public int CompletedWeek { get; set; }
    public int TotalStudents { get; set; }
}

public class UpdateAppointmentStatusRequest
{
    /// <summary>Confirmed, Cancelled, Completed or Rejected.</summary>
    [Required]
    public string Status { get; set; } = "";
}

public class RescheduleAppointmentRequest
{
    [Required] public string Date { get; set; } = "";

    [Required, StringLength(20)]
    public string TimeSlot { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = "";

    [StringLength(1000)]
    public string? Instructions { get; set; }
}

public class RescheduleAvailabilityDto : GuidanceDayAvailabilityDto
{
    /// <summary>True when the person who booked already has another active appointment that day.</summary>
    public bool StudentBusy { get; set; }
}

public class AvailabilityBlockDto
{
    public int Id { get; set; }
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";   // HH:mm:ss
    public string EndTime { get; set; } = "";
    public bool IsActive { get; set; }
}

public class AddAvailabilityRequest
{
    [Range(1, 5)]
    public int DayOfWeek { get; set; }

    [Required] public string StartTime { get; set; } = "";   // HH:mm or HH:mm:ss
    [Required] public string EndTime { get; set; } = "";
}

public class UpdateAvailabilityRequest
{
    public bool? IsActive { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}