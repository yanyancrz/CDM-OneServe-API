namespace CDM_OneServe_API.DTOs;

public class UpdateProfileRequest
{
    public int UserId { get; set; }

    public string? ContactNumber { get; set; }

    public string? Institute { get; set; }

    public string? Position { get; set; }

    public string? StudentStatus { get; set; }

    public string? Course { get; set; }

    public string? YearLevel { get; set; }
}