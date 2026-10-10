namespace CDM_OneServe_API.DTOs.Admin.Library.Books;

public class BookFilterDto
{
    public string? Search { get; set; }
    public string? Institute { get; set; }
    public string? YearLevel { get; set; }
    public string? Semester { get; set; }
    public string? Category { get; set; }

    // "" (all) | Available | Borrowed | Reserved
    public string? Availability { get; set; }

    // title (default) | borrow | newest
    public string? SortBy { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}