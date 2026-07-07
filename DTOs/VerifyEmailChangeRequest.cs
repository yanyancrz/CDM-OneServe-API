namespace CDM_OneServe_API.DTOs;

public class VerifyEmailChangeRequest
{
    public int UserId { get; set; }

    public string OTPCode { get; set; } = "";
}