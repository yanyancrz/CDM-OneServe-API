namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class BorrowListItemDto
{
    public int TransactionId { get; set; }
    public int UserId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string PatronType { get; set; } = string.Empty;
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string Status { get; set; } = string.Empty; // Borrowed | Overdue | Returned
    public bool IsInsideLibraryOnly { get; set; }
    public int OverdueDays { get; set; }
}