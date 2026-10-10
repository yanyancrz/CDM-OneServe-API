namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

// Counts for the filter tabs. Active reservations whose date already passed count as Expired.
public class ReservationSummaryDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int AwaitingAction { get; set; } // Reserved + Pending + Approved
    public int ExpiringToday { get; set; }

    public int Reserved { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int ReadyForPickup { get; set; }
    public int Claimed { get; set; }
    public int Rejected { get; set; }
    public int Cancelled { get; set; }
    public int Expired { get; set; }
}
