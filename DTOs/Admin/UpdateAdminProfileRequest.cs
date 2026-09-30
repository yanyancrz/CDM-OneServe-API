namespace CDM_OneServe_API.DTOs.Admin;

public class UpdateAdminProfileRequest
{
    public string FullName { get; set; } = "";

    public string? ContactNumber { get; set; }
}