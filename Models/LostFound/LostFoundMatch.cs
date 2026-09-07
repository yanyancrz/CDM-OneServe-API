using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.LostFound;

public class LostFoundMatch
{
    [Key]
    public int Id { get; set; }

    public int LostItemId { get; set; }

    public int FoundItemId { get; set; }

    public decimal? MatchScore { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Lost item relationship
    [ForeignKey(nameof(LostItemId))]
    public LostFoundItem? LostItem { get; set; }

    // Found item relationship
    [ForeignKey(nameof(FoundItemId))]
    public LostFoundItem? FoundItem { get; set; }
}


