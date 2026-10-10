namespace CDM_OneServe_API.DTOs.Guidance;

/// <summary>
/// Who the Guidance module thinks the caller is. Built ONLY from the JWT
/// + the live Users row - the client never tells the server who it is.
/// </summary>
public class GuidanceMeDto
{
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? Institute { get; set; }   // Faculty
    public string? Position { get; set; }    // Faculty
    public string? ProfilePicture { get; set; }

    /// <summary>Only filled for counselors.</summary>
    public CounselorProfileDto? CounselorProfile { get; set; }
}

public class CounselorProfileDto
{
    public string? Title { get; set; }
    public string? Room { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string[] Specializations { get; set; } = Array.Empty<string>();
    public string? Bio { get; set; }
}