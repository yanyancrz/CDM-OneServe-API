using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class PickupAvailability
{
    [Key]
    public int AvailabilityId { get; set; }

    // ---------------------------------------------------------
    // START OF THE PUBLISHED AVAILABILITY PERIOD
    // Example: September 30, 2026 10:00 AM
    // ---------------------------------------------------------
    public DateTime StartDateTime { get; set; }

    // ---------------------------------------------------------
    // END OF THE PUBLISHED AVAILABILITY PERIOD
    // Example: September 30, 2026 12:00 PM
    // ---------------------------------------------------------
    public DateTime EndDateTime { get; set; }

    // ---------------------------------------------------------
    // ADMIN CAN DEACTIVATE A PERIOD WITHOUT DELETING IT
    // ---------------------------------------------------------
    public bool IsActive { get; set; } = true;

    // ---------------------------------------------------------
    // CREATED DATE
    // ---------------------------------------------------------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}