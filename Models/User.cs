using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Models;

public class User
{
    public int Id { get; set; }

    public string IdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string? Course { get; set; }

    public string? YearLevel { get; set; }

    public string? ContactNumber { get; set; }

    public string? ProfilePicture { get; set; }

    public bool IsVerified { get; set; }

    public bool IsProfileComplete { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.Now;

    public string Role { get; set; } = "Student";

    public string? AdminModule { get; set; }

    public string? Institute { get; set; }

    public string? StudentStatus { get; set; }

    public string? Position { get; set; }

    // Physical ID Supporting Document
    public string? PhysicalIdDocument { get; set; }

    public string PhysicalIdVerificationStatus { get; set; }
        = "Pending";

    public DateTime? PhysicalIdVerifiedAt { get; set; }

    public int? PhysicalIdReviewedBy { get; set; }

    // Registration Verification Status
    public string AccountStatus { get; set; }
        = "Pending";
}