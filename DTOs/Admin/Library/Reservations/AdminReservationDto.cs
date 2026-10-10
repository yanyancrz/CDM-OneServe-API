namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

public class AdminReservationDto
{
    public int ReservationId { get; set; }

    public int UserId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string PatronType { get; set; } = string.Empty;

    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CallNo { get; set; }
    public string? CoverImage { get; set; }
    public int AvailableCopies { get; set; }

    public DateTime ReservationAt { get; set; }
    public DateTime ExpirationDate { get; set; }

    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string? Remarks { get; set; }

    // Still open but past its expiration date (waiting for "Mark expired").
    public bool IsPastExpiration { get; set; }

    // The server decides which actions are allowed. The UI only follows these.
    public bool CanApprove { get; set; }
    public bool CanConvert { get; set; }
    public bool CanReject { get; set; }
    public bool CanCancel { get; set; }
}