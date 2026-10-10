namespace CDM_OneServe_API.DTOs.Admin.Library.Librarians;

public class LibrarianFilterDto
{
    public string? Search { get; set; }

    // "" (all) | active | disabled
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}