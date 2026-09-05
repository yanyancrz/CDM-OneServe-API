using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.LostFound;

public class LostFoundClaim
{
    [Key]
    public int Id { get; set; }

    public int ItemId { get; set; }

    public int ClaimantUserId { get; set; }

    public string? ClaimDescription { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public int? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Item relationship
    [ForeignKey(nameof(ItemId))]
    public LostFoundItem? Item { get; set; }

    // Claimant relationship
    [ForeignKey(nameof(ClaimantUserId))]
    public User? ClaimantUser { get; set; }

    // Reviewer relationship
    [ForeignKey(nameof(ReviewedBy))]
    public User? Reviewer { get; set; }
}