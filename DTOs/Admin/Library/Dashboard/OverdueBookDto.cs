namespace CDM_OneServe_API.DTOs.Admin.Library.Dashboard;

public class OverdueBookDto
{
    public int TransactionId { get; set; }
    public string PatronName { get; set; } = string.Empty;
    public string PatronId { get; set; } = string.Empty;
    public string PatronType { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public DateTime BorrowDate { get; set; }
    public DateTime DueDate { get; set; }
    public int OverdueDays { get; set; }
    public decimal Fine { get; set; }
}