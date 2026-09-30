namespace CDM_OneServe_API.DTOs.Admin;

public class AdminProfileDto
{
    public int Id { get; set; }

    public string IdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string? ContactNumber { get; set; }

    public string? ProfilePicture { get; set; }

    public string? Role { get; set; }

    public DateTime DateJoined { get; set; }
}