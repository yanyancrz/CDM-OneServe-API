namespace CDM_OneServe_API.DTOs;

public class VerifyOtpRequest
{
    public string Email { get; set; } = "";

    public string OTPCode { get; set; } = "";
}