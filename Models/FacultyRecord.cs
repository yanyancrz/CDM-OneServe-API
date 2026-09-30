namespace CDM_OneServe_API.Models;

public class FacultyRecord
{
    public int Id { get; set; }

    public string FacultyIdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? Email { get; set; }

    public string? Institute { get; set; }

    public string? Position { get; set; }

    public string EmploymentStatus { get; set; } = "Active";

    public string? AcademicYear { get; set; }

    public string? ImportBatchId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}