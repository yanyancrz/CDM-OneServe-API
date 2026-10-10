namespace CDM_OneServe_API.DTOs.Admin.Library.Librarians;

// There is intentionally NO Role field. The server always creates a LibraryStaff.
public class CreateLibrarianDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? IdNumber { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}