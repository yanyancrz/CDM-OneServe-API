namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class ReturnLoanDto
{
    public int TransactionId { get; set; }

    public int UserId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string PatronType { get; set; } = string.Empty;

    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CallNo { get; set; }

    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }

    public bool IsOverdue { get; set; }
    public int OverdueDays { get; set; }

    // Official amount if returned right now. Recomputed again when the return is saved.
    public decimal Fine { get; set; }
    public decimal FinePerDay { get; set; }
}