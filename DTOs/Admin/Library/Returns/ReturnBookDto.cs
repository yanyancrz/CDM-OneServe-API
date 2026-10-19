namespace CDM_OneServe_API.DTOs.Admin.Library.Returns;

public class ReturnBookDto
{
    public int TransactionId { get; set; }

    // Good | Damaged | Lost
    public string Condition { get; set; } = string.Empty;
}
