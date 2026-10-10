namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class BorrowFilterDto
{
    public string? Search { get; set; }

    // "" (all) | active | overdue | returned
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}