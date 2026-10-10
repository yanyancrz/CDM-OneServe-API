namespace CDM_OneServe_API.DTOs.Admin.Library.Settings;

public class UpdateLibrarySettingsDto
{
    public decimal OverdueFinePerDay { get; set; }
    public int StudentBorrowLimit { get; set; }
    public int FacultyBorrowLimit { get; set; }
    public int DefaultLoanDays { get; set; }
    public int ReservationPickupDays { get; set; }
}