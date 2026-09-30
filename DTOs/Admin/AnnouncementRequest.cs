namespace CDM_OneServe_API.DTOs.Admin;

public class AnnouncementRequest
{
    public string AdminEmail { get; set; } = "";

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;
}