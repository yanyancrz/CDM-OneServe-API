namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

public class ReservationFilterDto
{
    // Patron name, ID number, book title, ISBN, or reservation number (#12)
    public string? Search { get; set; }

    // "" (all) | active | expired | Reserved | Pending | Approved | ReadyForPickup | Claimed | Rejected | Cancelled
    public string? Status { get; set; }

    // "" (all) | Student | Faculty
    public string? PatronType { get; set; }

    // newest (default) | oldest | expiring
    public string? Sort { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
