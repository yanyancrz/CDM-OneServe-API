namespace CDM_OneServe_API.DTOs.Admin.Library.Students;

public class StudentDto
{
    public int UserId { get; set; }
    public string? IdNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Course { get; set; }
    public string? YearLevel { get; set; }
    public string? AccountStatus { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
}