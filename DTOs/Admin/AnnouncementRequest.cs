namespace CDM_OneServe_API.DTOs.Admin;

public class AnnouncementRequest
{
    public string AdminEmail { get; set; } = "";

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Admin opt-in for the browser push that goes with publishing.
    /// Optional so an older client that never sends it simply stays silent.
    /// </summary>
    public bool NotifyUsers { get; set; } = false;
}