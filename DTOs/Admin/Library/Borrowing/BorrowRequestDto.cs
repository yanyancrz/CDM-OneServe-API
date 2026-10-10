namespace CDM_OneServe_API.DTOs.Admin.Library.Borrowing;

public class BorrowRequestDto
{
    public int UserId { get; set; }
    public int BookId { get; set; }
    public int LoanDays { get; set; }
    public bool AcceptedTerms { get; set; }
}