namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class ReturnFilterDto
{
    // Transaction ID, student/employee ID, patron name, or book title.
    public string? Search { get; set; }

    // "" (all active loans) | overdue
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}