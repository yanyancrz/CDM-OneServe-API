namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class BorrowRequestDto
{
    public int UserId { get; set; }
    public int BookId { get; set; }
    public int LoanDays { get; set; }
    public bool AcceptedTerms { get; set; }

    // True when the librarian chooses inside-library use. The server also
    // forces this automatically when this is the last available copy.
    public bool InsideLibraryOnly { get; set; }
}