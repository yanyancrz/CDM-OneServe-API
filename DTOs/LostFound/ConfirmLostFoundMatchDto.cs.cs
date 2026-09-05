namespace CDM_OneServe_API.DTOs.LostFound;

public class ConfirmLostFoundMatchDto
{
    public int LostItemId { get; set; }

    public int FoundItemId { get; set; }

    public decimal? MatchScore { get; set; }
}