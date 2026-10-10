namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class ReturnBookDto
{
    public int TransactionId { get; set; }

    // Good | Damaged | Lost
    public string Condition { get; set; } = string.Empty;

    // Paid | Unpaid | AddedToAccount. Required only when there is a fine.
    public string? PaymentStatus { get; set; }
}