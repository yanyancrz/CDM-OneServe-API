public class User
{
    public int Id { get; set; }

    public string StudentNumber { get; set; } = "";

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

    public string? Role { get; set; } = "Student";
    public string? Institute { get; set; }
}