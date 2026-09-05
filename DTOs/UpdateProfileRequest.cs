namespace CDM_OneServe_API.DTOs;

public class UpdateProfileRequest
{
    public int UserId { get; set; }

    public string Email { get; set; } = "";

    public string ContactNumber { get; set; } = "";

    // Faculty
    public string? Institute { get; set; }

    public string? Position { get; set; }

    // Student
    public string? StudentStatus { get; set; }
}