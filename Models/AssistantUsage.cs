namespace CDM_OneServe_API.Models;

/// <summary>
/// One AI answer served to a student: who asked, how long it took, whether
/// it came from the model or the on-device fallback, and the thumbs up/down
/// (null until tapped). Feeds the research evaluation, not the app itself.
/// </summary>
public class AssistantUsage
{
    public int UsageId { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Truncated to 300 chars - enough to group topics.</summary>
    public string Question { get; set; } = "";

    public int LatencyMs { get; set; }

    /// <summary>ai | fallback.</summary>
    public string Source { get; set; } = "ai";

    public string? Error { get; set; }
    public bool? Helpful { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
