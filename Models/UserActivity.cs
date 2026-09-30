namespace CDM_OneServe_API.Models;

public class UserActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = "";

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}