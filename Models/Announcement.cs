namespace CDM_OneServe_API.Models;

public class Announcement
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}