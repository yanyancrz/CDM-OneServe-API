namespace CDM_OneServe_API.DTOs.Admin.Library.Books;

// Validation lives in AdminBookService so errors use the standard ApiResponse format.
public class CreateBookDto
{
    public string? BookCode { get; set; }
    public string ISBN { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public string? Category { get; set; }
    public string? Institute { get; set; }
    public string? YearLevel { get; set; }
    public string? Semester { get; set; }
    public string? DDC { get; set; }
    public string? CallNo { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? Edition { get; set; }
    public int? PublishYear { get; set; }
    public string? ShelfLocation { get; set; }
    public int TotalCopies { get; set; } = 1;
}