namespace CDM_OneServe_API.DTOs.LostFound;

/** A claim as the finder / admin console sees it, with names resolved. */
public class ClaimAdminDto
{
    public int ClaimId { get; set; }

    public int ReportId { get; set; }

    public int ClaimedByUserId { get; set; }

    public string ClaimantName { get; set; } = string.Empty;

    public string ClaimantEmail { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string ReportType { get; set; } = string.Empty;

    public string FinderName { get; set; } = string.Empty;

    public string FinderEmail { get; set; } = string.Empty;

    public string ItemCustody { get; set; } = "None";

    public DateTime ClaimDate { get; set; }

    public string VerificationNotes { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

/** The Lost & Found admin dashboard counters. */
public class AdminStatsDto
{
    public int TotalReports { get; set; }

    public int PendingReports { get; set; }

    public int ApprovedReports { get; set; }

    public int RejectedReports { get; set; }

    public int ClaimedReports { get; set; }

    public int TotalClaims { get; set; }

    public int PendingClaims { get; set; }

    public int ApprovedClaims { get; set; }

    public int RejectedClaims { get; set; }

    public int CompletedClaims { get; set; }

    public int TotalSchedules { get; set; }

    public int ScheduledPickups { get; set; }

    public int CompletedPickups { get; set; }

    public int TotalMatches { get; set; }

    public int ConfirmedMatches { get; set; }

    public int TotalRecovery { get; set; }

    public int PendingRecovery { get; set; }

    public int AtAdminOfficeRecovery { get; set; }

    public int CompletedRecovery { get; set; }
}
