namespace CDMOneServe.API.DTOs.Library;

public class CreateBookDto
{
    public string ISBN { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string? Publisher { get; set; }

    public string? Edition { get; set; }

    public int? PublishedYear { get; set; }

    public string? Category { get; set; }

    public string? Language { get; set; }

    public string? ShelfLocation { get; set; }

    public int TotalCopies { get; set; }

    public int AvailableCopies { get; set; }

    public string? CoverImageUrl { get; set; }

    public string? Description { get; set; }

    public string Status { get; set; } = "Available";
}