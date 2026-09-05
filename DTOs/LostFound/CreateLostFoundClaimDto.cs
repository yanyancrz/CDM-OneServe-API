namespace CDM_OneServe_API.DTOs.LostFound;

public class CreateLostFoundClaimDto
{
    public int ItemId { get; set; }

    public int ClaimantUserId { get; set; }

    public string? ClaimDescription { get; set; }
}