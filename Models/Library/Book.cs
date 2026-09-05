using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.Library;

public class Book
{
    [Key]
    public int BookId { get; set; }

    [Required]
    [MaxLength(20)]
    public string ISBN { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Author { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Publisher { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(50)]
    public string? Edition { get; set; }

    public int? PublishYear { get; set; }

    [MaxLength(500)]
    public string? CoverImage { get; set; }

    [MaxLength(100)]
    public string? ShelfLocation { get; set; }

    public int TotalCopies { get; set; }

    public int AvailableCopies { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Available";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}