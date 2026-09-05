namespace CDM_OneServe_API.DTOs.DigitalIDAdmin;

public class ChangeAdminPasswordRequest
{
    public string Email { get; set; } = "";

    public string CurrentPassword { get; set; } = "";

    public string NewPassword { get; set; } = "";
}