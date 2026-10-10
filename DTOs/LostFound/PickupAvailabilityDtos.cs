namespace CDM_OneServe_API.DTOs.LostFound;

// =============================================================
// PICKUP AVAILABILITY DTOs
// =============================================================

public class CreatePickupAvailabilityDto
{
    public DateTime StartDateTime { get; set; }

    public DateTime EndDateTime { get; set; }
}

public class CreatePickupAvailabilityBatchDto
{
    public List<CreatePickupAvailabilityDto> Availability { get; set; } = new();
}
