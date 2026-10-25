namespace CDM_OneServe_API.Models;

public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public string Type { get; set; } = "INFO";

    public bool IsRead { get; set; } = false;

    // Stored as UTC; the API marks it UTC on the way out so browsers do not
    // have to guess which zone the zone-less DATETIME came from.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}