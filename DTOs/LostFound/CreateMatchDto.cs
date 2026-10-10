namespace CDM_OneServe_API.DTOs.LostFound;

/** A possible pairing of a lost report with a found report. */
public class CreateMatchDto
{
    public int LostReportId { get; set; }

    public int FoundReportId { get; set; }

    /** 100 = exact, 50 = strong, 10 = possible. No minimum is enforced. */
    public int MatchPercentage { get; set; } = 50;
}
