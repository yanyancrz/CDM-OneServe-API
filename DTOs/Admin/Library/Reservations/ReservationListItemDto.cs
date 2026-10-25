namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

public class ReservationListItemDto
{
    public int ReservationId { get; set; }

    public int UserId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string PatronType { get; set; } = string.Empty; // Student | Faculty
    public string? Institute { get; set; }

    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImage { get; set; }
    public int AvailableCopies { get; set; }

    public DateTime ReservationAt { get; set; }
    public DateTime ExpirationDate { get; set; }

    // Status = stored value. DisplayStatus = what the UI shows
    // ("Expired" when the date passed but the background job has not run yet).
    public string Status { get; set; } = string.Empty;
    public string DisplayStatus { get; set; } = string.Empty;
    public bool IsExpired { get; set; }

    // True for ReadyForPickup: one copy is already off the shelf for this patron.
    public bool HoldsCopy { get; set; }

    // True when claiming this reservation would use the last copy on the
    // shelf, so the loan must stay inside the library.
    public bool WillBeInsideUse { get; set; }

    // Computed on the server. The UI only shows or hides buttons.
    public bool CanApprove { get; set; }
    public bool CanMarkReady { get; set; }
    public bool CanReject { get; set; }
    public bool CanCancel { get; set; }
    public bool CanClaim { get; set; }
}
