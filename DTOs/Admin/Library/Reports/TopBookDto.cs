namespace CDM_OneServe_API.DTOs.Admin.Library.Reports;

public class TopBookDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Institute { get; set; }
    public int BorrowCount { get; set; }
}