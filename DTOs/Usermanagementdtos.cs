namespace CDM_OneServe_API.DTOs;

public class CreateUserRequest
{
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";

    // Optional depending on role
    public string? Institute { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }

    // Defaults to Active when creating a user
    public string Status { get; set; } = "Active";
}

public class UpdateUserRequest
{
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";

    // Optional depending on role
    public string? Institute { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }

    // Optional because it should only change when provided
    public string? Status { get; set; }
}

public class UpdateUserStatusRequest
{
    public string Status { get; set; } = "";
}