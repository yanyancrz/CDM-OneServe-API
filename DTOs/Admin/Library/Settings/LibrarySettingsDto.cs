namespace CDM_OneServe_API.DTOs.Admin.Library.Settings;

public class LibrarySettingsDto
{
    public decimal OverdueFinePerDay { get; set; }
    public int StudentBorrowLimit { get; set; }
    public int FacultyBorrowLimit { get; set; }
    public int DefaultLoanDays { get; set; }
    public int ReservationPickupDays { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }

    // The loan periods the Borrow page offers.
    public int[] LoanDayOptions { get; set; } = Array.Empty<int>();
}