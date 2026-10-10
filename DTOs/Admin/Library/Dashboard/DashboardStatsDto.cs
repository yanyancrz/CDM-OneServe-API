namespace CDM_OneServe_API.DTOs.Admin.Library.Dashboard;

public class DashboardStatsDto
{
    public int TotalCatalogBooks { get; set; }
    public int TotalPhysicalCopies { get; set; }
    public int ActiveBorrowedBooks { get; set; }
    public int OverdueBooks { get; set; }
    public int PendingReservations { get; set; }
    public int StudentPatrons { get; set; }
    public int FacultyPatrons { get; set; }
}