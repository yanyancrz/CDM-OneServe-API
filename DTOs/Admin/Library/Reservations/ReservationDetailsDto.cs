namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

public class ReservationDetailsDto : ReservationListItemDto
{
    public string? Remarks { get; set; }

    // Patron
    public string? PatronEmail { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? AccountStatus { get; set; }
    public int BorrowLimit { get; set; }
    public int ActiveLoans { get; set; }
    public bool CanBorrow { get; set; }
    public string? BorrowBlockReason { get; set; }

    // Book
    public string? ISBN { get; set; }
    public string? CallNo { get; set; }
    public string? ShelfLocation { get; set; }
    public int TotalCopies { get; set; }
}
