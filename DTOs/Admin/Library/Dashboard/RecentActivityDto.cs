namespace CDM_OneServe_API.DTOs.Admin.Library.Dashboard;

public class RecentActivityDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public bool IsMine { get; set; }
    public string Code { get; set; } = string.Empty;     // e.g. LIBRARY_BOOK_BORROWED
    public string Message { get; set; } = string.Empty;  // e.g. issued "Clean Code" to Juan
    public DateTime CreatedAt { get; set; }
}