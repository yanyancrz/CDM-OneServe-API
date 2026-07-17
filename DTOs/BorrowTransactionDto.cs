namespace CDM_OneServe_API.DTOs;

public class BorrowTransactionDto
{
    public int BorrowId { get; set; }

    public int UserId { get; set; }

    public int BookId { get; set; }

    public string BookTitle { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string? CoverImage { get; set; }

    public DateTime BorrowDate { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime? ReturnDate { get; set; }

    public int RenewalCount { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Fine { get; set; }

    public string? Remarks { get; set; }

    public int OverdueDays { get; set; }
}