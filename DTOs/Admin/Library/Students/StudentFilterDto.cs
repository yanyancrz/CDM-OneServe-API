namespace CDM_OneServe_API.DTOs.Admin.Library.Students;

public class StudentFilterDto
{
    public string? Search { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}