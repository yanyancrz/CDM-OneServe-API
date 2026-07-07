public class DigitalId
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int RequestId { get; set; }

    public string DigitalIdNumber { get; set; } = "";

    public string? QRCode { get; set; }

    public DateTime IssuedDate { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public string Status { get; set; } = "Active";
}