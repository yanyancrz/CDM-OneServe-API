namespace CDM_OneServe_API.DTOs.Library;

public class LibraryScanResultDto
{
    public bool Cleared { get; set; }

    public int UserId { get; set; }

    public string IdNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string? Institute { get; set; }

    public int BorrowedBooks { get; set; }

    public int BorrowLimit { get; set; }

    public int RemainingBooks { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}