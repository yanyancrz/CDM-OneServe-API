using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.DTOs.Guidance;

public class CreateCounselorRequest
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = "";

    [Required, StringLength(30)]
    public string IdNumber { get; set; } = "";

    [Required, MinLength(8), StringLength(100)]
    public string Password { get; set; } = "";

    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string[]? Specializations { get; set; }
}