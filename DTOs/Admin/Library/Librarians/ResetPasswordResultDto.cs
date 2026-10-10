namespace CDM_OneServe_API.DTOs.Admin.Library.Librarians;

public class ResetPasswordResultDto
{
    // Shown once. It is never stored in plain text and never logged.
    public string TemporaryPassword { get; set; } = string.Empty;
}