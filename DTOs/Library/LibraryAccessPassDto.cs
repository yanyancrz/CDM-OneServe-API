namespace CDM_OneServe_API.DTOs.Library;

public class LibraryAccessPassDto
{
    public int UserId { get; set; }

    public string IdNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Institute { get; set; }

    public string? Program { get; set; }

    public string Role { get; set; } = string.Empty;

    public long Timestamp { get; set; }

    public string QrData { get; set; } = string.Empty;
}