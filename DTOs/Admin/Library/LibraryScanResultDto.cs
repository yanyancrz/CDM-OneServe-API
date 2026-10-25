namespace CDM_OneServe_API.DTOs.Admin.Library;

public class LibraryScanResultDto
{
    public bool Cleared { get; set; }

    public int UserId { get; set; }

    public string IdNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string? Institute { get; set; }

    public int BorrowedBooks { get; set; }

    public int BorrowLimit { get; set; }

    public int RemainingBooks { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    // Active reservations (para sa "Claimed" button ng staff)
    public List<ScanReservationDto> ReservedBooks { get; set; } = new();

    // Borrowed books na kailangang i-return (para sa "Confirm Return" button)
    public List<ScanLoanDto> ActiveLoans { get; set; } = new();

    // Unresolved damaged returns that block borrowing and reservation claims.
    public List<ScanDamagedReturnDto> DamagedReturns { get; set; } = new();
}

public class ScanDamagedReturnDto
{
    public int TransactionId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? CoverImage { get; set; }
    public DateTime ReturnDate { get; set; }
    public DateTime? ReplacementDueDate { get; set; }
    public bool IsOverdue { get; set; }
    public int? DaysLeft { get; set; }
}

public class ScanReservationDto
{
    public int ReservationId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? CoverImage { get; set; }
    public DateTime ReservationAt { get; set; }
    public DateTime ExpirationDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int AvailableCopies { get; set; }

    // True for ReadyForPickup: a copy is already set aside, so AvailableCopies may be 0.
    public bool HoldsCopy { get; set; }

    // False when the reservation still needs approval (Pending) or no copy is available.
    public bool CanClaim { get; set; }
}

public class ScanLoanDto
{
    public int BorrowId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? CoverImage { get; set; }
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
}