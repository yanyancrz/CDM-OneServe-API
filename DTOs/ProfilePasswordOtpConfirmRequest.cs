namespace CDM_OneServe_API.DTOs;

// Step 2 of the Profile "Change password" flow: the one-time code plus the
// new password. Same student number and email as step 1 so the code can be
// tied back to the right account.
public class ProfilePasswordOtpConfirmRequest
{
    public string IdNumber { get; set; } = "";

    public string Email { get; set; } = "";

    public string OtpCode { get; set; } = "";

    public string NewPassword { get; set; } = "";
}
