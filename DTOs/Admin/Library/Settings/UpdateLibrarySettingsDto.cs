namespace CDM_OneServe_API.DTOs.Admin.Library.Settings;

public class UpdateLibrarySettingsDto
{
    public int StudentBorrowLimit { get; set; }
    public int FacultyBorrowLimit { get; set; }
    public int DefaultLoanDays { get; set; }
    public int ReservationPickupDays { get; set; }

    // Semester dates. All four are optional; when a start and its end are both
    // set, the start must not be after the end.
    public DateTime? FirstSemStart { get; set; }
    public DateTime? FirstSemEnd { get; set; }
    public DateTime? SecondSemStart { get; set; }
    public DateTime? SecondSemEnd { get; set; }
}
