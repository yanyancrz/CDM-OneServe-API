namespace CDM_OneServe_API.Models.Guidance;

public class GuidanceRescheduleProposal
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public GuidanceAppointment Appointment { get; set; } = null!;
    public int ProposedBy { get; set; }
    public DateOnly ProposedDate { get; set; }
    public string ProposedTimeSlot { get; set; } = "";
    public DateOnly OriginalDate { get; set; }
    public string OriginalTimeSlot { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Instructions { get; set; }
    public string Status { get; set; } = "Pending";
    public int? RespondedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}
