namespace CDM_OneServe_API.DTOs.Admin.Library.Students;

public class StudentLoanDto
{
    public int TransactionId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
    public int OverdueDays { get; set; }
}