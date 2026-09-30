namespace CDM_OneServe_API.Models;

public class OTPVerification
{
    public int Id { get; set; }

    public string IdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string? PhysicalIdDocument { get; set; }

    public string Role { get; set; } = "Student";

    public string OTPCode { get; set; } = "";

    public DateTime ExpiryDate { get; set; }
}