namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class BorrowResponseDto
{
    public int TransactionId { get; set; }

    public int UserId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string? PatronId { get; set; }
    public string PatronType { get; set; } = string.Empty;

    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? ISBN { get; set; }
    public string? CallNo { get; set; }

    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public int LoanDays { get; set; }

    // True when the book must stay inside the library and is due back at
    // 5:00 PM on the same day.
    public bool IsInsideLibraryOnly { get; set; }

    public int RemainingSlots { get; set; }
    public string ProcessedBy { get; set; } = string.Empty;
}