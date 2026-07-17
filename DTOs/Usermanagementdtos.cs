namespace CDM_OneServe_API.DTOs;

public class CreateUserRequest
{
    public string IdNumber { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }          // "Student" | "Faculty" | "Registrar"
    public string Institute { get; set; }     // only meaningful for Role == "Student"
    public string Course { get; set; }        // only meaningful for Role == "Student"
    public string YearLevel { get; set; }     // only meaningful for Role == "Student"
    public string Status { get; set; }        // optional, defaults to "Active"
}

public class UpdateUserRequest
{
    public string IdNumber { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string Institute { get; set; }
    public string Course { get; set; }
    public string YearLevel { get; set; }
    public string Status { get; set; }        // optional; only updated if provided
}

public class UpdateUserStatusRequest
{
    public string Status { get; set; }        // "Active" | "Pending" | "Suspended"
}