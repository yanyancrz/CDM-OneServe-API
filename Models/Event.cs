namespace CDM_OneServe_API.Models;

public class Event
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public DateTime EventDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? Location { get; set; }

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
