public class DigitalIdRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Role { get; set; } = "";

    public string? StudentNumber { get; set; }

    public string? FacultyIdNumber { get; set; }

    public string FullName { get; set; } = "";

    public string Institute { get; set; } = "";

    public string? Course { get; set; }

    public string? YearLevel { get; set; }

    public string? StudentStatus { get; set; }

    public string? Position { get; set; }

    public string? Address { get; set; }

    public string? ProfilePicture { get; set; }

    public string Status { get; set; } = "Pending";

    public string? ReviewStatus { get; set; }

    public DateTime RequestedAt { get; set; }
        = DateTime.Now;
}