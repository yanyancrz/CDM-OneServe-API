namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class PatronLookupDto
{
    public int UserId { get; set; }
    public string? IdNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Institute { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? AccountStatus { get; set; }

    public int BorrowLimit { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
    public int RemainingSlots { get; set; }

    public bool CanBorrow { get; set; }
    public string? Reason { get; set; }

    // Set when the patron is suspended after a late return.
    public DateTime? BannedUntil { get; set; }
}