namespace CDM_OneServe_API.DTOs.Library;

public class BookDto
{
    public int BookId { get; set; }

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

    public string? Synopsis { get; set; }

    public string? Language { get; set; }

    public string? Edition { get; set; }
    public int? PublishYear { get; set; }

    public string? CoverImage { get; set; }

    public string? ShelfLocation { get; set; }

    public int TotalCopies { get; set; }

    public int AvailableCopies { get; set; }

    public string Status { get; set; } = "Available";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}