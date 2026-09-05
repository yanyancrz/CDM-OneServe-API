using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.LostFound;

public class LostFoundNotification
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? ItemId { get; set; }

    public int? MatchId { get; set; }

    public int? ClaimId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = "";

    [Required]
    public string Message { get; set; } = "";

    [Required]
    [MaxLength(30)]
    public string Type { get; set; } = "";

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; }

    // User relationship
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    // Item relationship
    [ForeignKey(nameof(ItemId))]
    public LostFoundItem? Item { get; set; }

    // Match relationship
    [ForeignKey(nameof(MatchId))]
    public LostFoundMatch? Match { get; set; }

    // Claim relationship
    [ForeignKey(nameof(ClaimId))]
    public LostFoundClaim? Claim { get; set; }
}