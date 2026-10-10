namespace CDM_OneServe_API.DTOs.Admin.Library.Faculty;

public class FacultyDto
{
    public int UserId { get; set; }
    public string? IdNumber { get; set; }          // Employee ID
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }        // Institute / Department
    public string? AccountStatus { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
}