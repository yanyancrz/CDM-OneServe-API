using Microsoft.AspNetCore.Http;

namespace CDM_OneServe_API.DTOs;

public class CreateDigitalIdRequestDto
{
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Institute { get; set; } = "";
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? StudentStatus { get; set; }
    public string? Position { get; set; }
    public string Address { get; set; } = "";

    public IFormFile? ProfilePicture { get; set; }
}