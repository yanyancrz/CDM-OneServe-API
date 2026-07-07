namespace CDM_OneServe_API.DTOs;

public class UpdateProfileRequest
{
    public int UserId { get; set; }

    public string Email { get; set; } = "";

    public string ContactNumber { get; set; } = "";
}