namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class DamagedReturnFilterDto
{
    // Transaction ID, patron name/ID, or book title.
    public string? Search { get; set; }

    // pending (default) | resolved | all
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
