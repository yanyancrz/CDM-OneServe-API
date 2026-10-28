namespace CDM_OneServe_API.DTOs.LostFound;

// =============================================================
// CLAIM SCHEDULE DTOs
// =============================================================

/** A claimant picks a slot inside a published availability period. */
public class BookPickupScheduleDto
{
    public int ClaimId { get; set; }

    public DateTime ScheduledDateTime { get; set; }
}

/** The admin records who physically received the item at handover. */
public class CompleteClaimScheduleDto
{
    public string ReceivedByName { get; set; } = string.Empty;

    public string ReceivedByStudentId { get; set; } = string.Empty;
}

/** A pickup schedule as the admin console and claimant see it. */
public class ClaimScheduleDto
{
    public int ScheduleId { get; set; }

    public int ClaimId { get; set; }

    public string ClaimantName { get; set; } = string.Empty;

    public string ClaimantEmail { get; set; } = string.Empty;

    /** IdNumber of the claimant's OneServe account. */
    public string AccountStudentId { get; set; } = string.Empty;

    public string StudentId { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public int ReportId { get; set; }

    public string ItemCustody { get; set; } = "None";

    public string FinderName { get; set; } = string.Empty;

    public string FinderEmail { get; set; } = string.Empty;

    public DateTime ScheduledDateTime { get; set; }

    public string PickupLocation { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string ReceivedByName { get; set; } = string.Empty;

    public string ReceivedByStudentId { get; set; } = string.Empty;

    public DateTime? CompletedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public int? VerifiedByAdminId { get; set; }

    /** Who recorded the handover (name + role snapshot). */
    public string? VerifiedByName { get; set; }

    public string? VerifiedByRole { get; set; }
}

/** The answer to a successful booking. */
public class BookScheduleResultDto
{
    public int ScheduleId { get; set; }

    public string PickupLocation { get; set; } = string.Empty;

    public DateTime AvailabilityStart { get; set; }

    public DateTime AvailabilityEnd { get; set; }
}
