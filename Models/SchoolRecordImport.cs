namespace CDM_OneServe_API.Models;

public class SchoolRecordImport
{
    public int Id { get; set; }

    public string BatchId { get; set; } = "";

    public string SourceType { get; set; } = "";

    public string? FileName { get; set; }

    public string RecordType { get; set; } = "";

    public string? AcademicYear { get; set; }

    public string? Semester { get; set; }

    public int TotalRecords { get; set; }

    public int? ImportedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}