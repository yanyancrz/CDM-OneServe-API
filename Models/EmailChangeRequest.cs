namespace CDM_OneServe_API.Models;

public class EmailChangeRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string NewEmail { get; set; } = "";

    public string OTPCode { get; set; } = "";

    public DateTime ExpiryDate { get; set; }
}