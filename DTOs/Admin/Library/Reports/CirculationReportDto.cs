namespace CDM_OneServe_API.DTOs.Admin.Library.Reports;

public class CirculationPointDto
{
    // Start of the day, the week (Monday), or the month.
    public DateTime Date { get; set; }
    public int Borrowed { get; set; }
    public int Returned { get; set; }
}

public class CirculationReportDto
{
    public string Granularity { get; set; } = "daily";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalBorrowed { get; set; }
    public int TotalReturned { get; set; }
    public List<CirculationPointDto> Points { get; set; } = new();
}