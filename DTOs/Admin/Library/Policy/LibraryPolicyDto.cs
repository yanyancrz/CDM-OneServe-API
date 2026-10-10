namespace CDM_OneServe_API.DTOs.Admin.Library.Policy;

// What a student or faculty member may read: the borrowing numbers
// and the Terms & Guidelines. Nothing about who edited them.
public class LibraryPolicyDto
{
    public LibraryPolicySettingsDto Settings { get; set; } = new();

    public List<LibraryPolicyTermDto> Terms { get; set; } = new();
}

public class LibraryPolicySettingsDto
{
    public decimal OverdueFinePerDay { get; set; }

    public int StudentBorrowLimit { get; set; }

    public int FacultyBorrowLimit { get; set; }

    public int DefaultLoanDays { get; set; }

    public int ReservationPickupDays { get; set; }

    public int[] LoanDayOptions { get; set; } = Array.Empty<int>();
}

public class LibraryPolicyTermDto
{
    public int Id { get; set; }

    // Optional. A term can be just a paragraph.
    public string? Title { get; set; }

    public string Content { get; set; } = string.Empty;
}