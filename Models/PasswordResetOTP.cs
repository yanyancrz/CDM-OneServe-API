public class PasswordResetOTP
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string OTPCode { get; set; } = string.Empty;

    public DateTime ExpiryDate { get; set; }
}