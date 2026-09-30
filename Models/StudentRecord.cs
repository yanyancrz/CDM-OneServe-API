namespace CDM_OneServe_API.Models;

public class StudentRecord
{
    public int Id { get; set; }

    public string StudentIdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? Email { get; set; }

    public string? Course { get; set; }

    public string? Institute { get; set; }

    public string? YearLevel { get; set; }

    public string EnrollmentStatus { get; set; } = "Enrolled";

    public string? AcademicYear { get; set; }

    public string? Semester { get; set; }

    public string? ImportBatchId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}