namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

// Body for reject (reason required) and cancel (reason optional).
public class ReservationReasonDto
{
    public string? Reason { get; set; }
}
