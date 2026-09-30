namespace CDM_OneServe_API.DTOs.Admin;

public class AdminNotificationPreferenceDto
{
    public string Email { get; set; } = "";

    public bool EmailOnNewRequest { get; set; } = true;

    public bool EmailOnThreshold { get; set; } = false;

    public int ThresholdCount { get; set; } = 10;
}