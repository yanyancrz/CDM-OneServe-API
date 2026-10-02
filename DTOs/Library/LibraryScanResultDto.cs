namespace CDM_OneServe_API.DTOs.Library;

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

    // NEW
    public List<ScanReservationDto> ReservedBooks { get; set; } = new();
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
}