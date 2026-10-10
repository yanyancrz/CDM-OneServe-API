namespace CDM_OneServe_API.DTOs.Admin.Library.Librarians;

public class LibrarianDto
{
    public int UserId { get; set; }
    public string? IdNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? AccountStatus { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime? CreatedAt { get; set; }
}