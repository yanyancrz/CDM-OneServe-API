namespace CDM_OneServe_API.DTOs;

public class RegisterRequest
{
    public string FirstName { get; set; } = "";

    public string MiddleName { get; set; } = "";

    public string LastName { get; set; } = "";

    public string IdNumber { get; set; } = "";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public string? PhysicalIdDocument { get; set; }

    public string Role { get; set; } = "Student";
}