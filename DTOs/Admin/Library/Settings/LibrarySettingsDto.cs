namespace CDM_OneServe_API.DTOs.Admin.Library.Settings;

public class LibrarySettingsDto
{
    public int StudentBorrowLimit { get; set; }
    public int FacultyBorrowLimit { get; set; }
    public int DefaultLoanDays { get; set; }
    public int ReservationPickupDays { get; set; }

    // Semester dates. A late return suspends the patron until the end of the
    // current semester. All four are optional.
    public DateTime? FirstSemStart { get; set; }
    public DateTime? FirstSemEnd { get; set; }
    public DateTime? SecondSemStart { get; set; }
    public DateTime? SecondSemEnd { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }

    // The loan periods the Borrow page offers.
    public int[] LoanDayOptions { get; set; } = Array.Empty<int>();
}
