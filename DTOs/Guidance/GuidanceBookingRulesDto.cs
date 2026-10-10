namespace CDM_OneServe_API.DTOs.Guidance;

/// <summary>
/// Booking rules the student "Book an appointment" screen needs BEFORE it draws
/// the form. Everything here comes from guidance_settings (Guidance Head > Settings).
///
/// Without this the student UI can only discover the rules after a failed
/// submit - by which point the form has already been filled in.
/// </summary>
public class GuidanceBookingRulesDto
{
    /// <summary>
    /// Students may book for today. When false the Book screen shows a warning
    /// instead of the form ("hindi pwede magpa-appoint ngayong araw").
    /// </summary>
    public bool AllowSameDay { get; set; } = true;

    /// <summary>How many days ahead a student may book.</summary>
    public int MaxAdvanceDays { get; set; } = 60;

    /// <summary>Earliest date the API accepts, yyyy-MM-dd (today, or tomorrow when same-day is off).</summary>
    public string EarliestDate { get; set; } = "";

    /// <summary>Latest date the API accepts, yyyy-MM-dd.</summary>
    public string LatestDate { get; set; } = "";
}
