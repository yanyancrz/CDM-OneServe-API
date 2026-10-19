namespace CDM_OneServe_API.DTOs;

// Step 1 of the Profile "Change password" flow: the user proves who they
// are with their student number and the email on the account, and we mail
// a one-time code to that address.
public class ProfilePasswordOtpRequest
{
    public string IdNumber { get; set; } = "";

    public string Email { get; set; } = "";
}
