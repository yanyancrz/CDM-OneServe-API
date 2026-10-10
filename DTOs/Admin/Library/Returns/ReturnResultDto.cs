namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class ReturnResultDto
{
    public int TransactionId { get; set; }

    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string BookTitle { get; set; } = string.Empty;

    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime ReturnDate { get; set; }

    public int OverdueDays { get; set; }
    public decimal Fine { get; set; }
    public decimal FinePerDay { get; set; }

    public string Condition { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }

    public bool CopyReturnedToShelf { get; set; }
    public string ProcessedBy { get; set; } = string.Empty;
}