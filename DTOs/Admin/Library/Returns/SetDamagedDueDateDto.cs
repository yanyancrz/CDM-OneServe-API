using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class SetDamagedDueDateDto
{
    // Date only. Stored as yyyy-MM-dd in the loan remarks marker.
    [Required]
    public DateTime ReplacementDueDate { get; set; }
}
