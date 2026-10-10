namespace CDM_OneServe_API.DTOs.Admin.Library.Faculty;

public class FacultyFilterDto
{
    public string? Search { get; set; }
    public string? Department { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}