using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class ClaimSchedule
{
    [Key]
    public int ScheduleId { get; set; }


    // =========================================================
    // CLAIM
    // =========================================================

    public int ClaimId { get; set; }

    public ClaimRecord? Claim { get; set; }


    // =========================================================
    // CLAIMANT
    // =========================================================

    public int ClaimantUserId { get; set; }


    // =========================================================
    // CLAIMANT STUDENT ID
    //
    // Student ID stored when the schedule was created.
    // This is for reference only.
    // =========================================================

    public string StudentId { get; set; } = string.Empty;


    // =========================================================
    // SCHEDULE
    // =========================================================

    public DateTime ScheduledDateTime { get; set; }


    // =========================================================
    // PICKUP LOCATION
    // =========================================================

    public string PickupLocation { get; set; } = string.Empty;


    // =========================================================
    // STATUS
    //
    // Scheduled
    // Completed
    // Cancelled
    // NoShow
    // =========================================================

    public string Status { get; set; } = "Scheduled";


    // =========================================================
    // CREATED
    // =========================================================

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;


    // =========================================================
    // HANDOVER RECORD
    //
    // These are the details entered by the Admin
    // when the item is physically released.
    // =========================================================

    public string ReceivedByName { get; set; } =
        string.Empty;

    public string ReceivedByStudentId { get; set; } =
        string.Empty;


    // =========================================================
    // COMPLETION
    // =========================================================

    public DateTime? CompletedAt { get; set; }


    // =========================================================
    // VERIFICATION RECORD
    //
    // This records which Admin processed the handover.
    // It does NOT perform identity validation.
    // =========================================================

    public DateTime? VerifiedAt { get; set; }

    public int? VerifiedByAdminId { get; set; }
}