namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

// A returned copy that was recorded as Damaged and still needs a
// replacement (or a repair) before the deadline.
public class DamagedReturnDto
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
    public string? CoverImage { get; set; }

    public DateTime ReturnDate { get; set; }

    // Null until the library sets a deadline.
    public DateTime? ReplacementDueDate { get; set; }

    public bool IsOverdue { get; set; }
    public int? DaysLeft { get; set; }

    public bool IsResolved { get; set; }
    public DateTime? ResolvedDate { get; set; }
}
