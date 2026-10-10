namespace CDM_OneServe_API.DTOs.Admin.Library.Reports;

// A flat table. The frontend turns it into a CSV file.
public class ExportTableDto
{
    public string FileName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}