namespace CDM_OneServe_API.DTOs;

public class ProfileSetupRequest
{
    public int UserId { get; set; }

    public string? ContactNumber { get; set; }

    public string? Role { get; set; }

    public string? Course { get; set; }

    public string? Institute { get; set; }

    public string? YearLevel { get; set; }

    public string? StudentStatus { get; set; }

    public string? Position { get; set; }
}
