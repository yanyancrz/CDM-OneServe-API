namespace CDM_OneServe_API.Models;

public class Event
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public DateTime EventDate { get; set; }

    public DateTime? EndDate { get; set; }

    /// <summary>
    /// JSON-serialized array of "YYYY-MM-DD" date strings representing the
    /// exact selected event dates (NOT a continuous range). Null for legacy
    /// rows that only had EventDate/EndDate.
    /// </summary>
    public string? EventDatesJson { get; set; }

    /// <summary>
    /// "HH:mm" start time, nullable.
    /// </summary>
    public string? StartTime { get; set; }

    /// <summary>
    /// "HH:mm" end time, nullable.
    /// </summary>
    public string? EndTime { get; set; }

    public string? Location { get; set; }

    public string Tag { get; set; } = "CAMPUS";

    public string Accent { get; set; } = "#F4D35E";

    public bool IsActive { get; set; } = true;

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
