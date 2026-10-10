namespace CDM_OneServe_API.DTOs.LostFound;

public class PickupAvailabilityDto
{
    public int AvailabilityId { get; set; }

    public DateTime StartDateTime { get; set; }

    public DateTime EndDateTime { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}