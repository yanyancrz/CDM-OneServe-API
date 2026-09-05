using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.LostFound;

public class LostFoundItem
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ItemName { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = "";

    public string? Description { get; set; }

    [Required]
    [MaxLength(10)]
    public string ReportType { get; set; } = "";

    public DateTime DateLostFound { get; set; }

    [Required]
    [MaxLength(255)]
    public string Location { get; set; } = "";

    [MaxLength(500)]
    public string? Photo { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    [Required]
    [MaxLength(20)]
    public string VerificationStatus { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // User relationship
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}