namespace CDM_OneServe_API.DTOs.Admin.Library.Librarians;

public class LibrarianActivityDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}