namespace CDM_OneServe_API.DTOs;

public class RegisterRequest
{
    public string StudentNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}