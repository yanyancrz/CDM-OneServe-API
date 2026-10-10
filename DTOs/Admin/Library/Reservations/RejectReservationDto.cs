namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

// Used by both reject and cancel.
public class RejectReservationDto
{
    public string? Reason { get; set; }
}