namespace CDM_OneServe_API.DTOs.Admin;

public class AdminActivityLogDto
{
    public int Id { get; set; }

    public string Action { get; set; } = "";

    public DateTime Timestamp { get; set; }
}