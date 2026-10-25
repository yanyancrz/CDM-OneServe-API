namespace CDM_OneServe_API.DTOs.Admin.Library.Policy;

// What a student or faculty member may read: the borrowing numbers
// and the Terms & Guidelines. Nothing about who edited them.
public class LibraryPolicyDto
{
    public LibraryPolicySettingsDto Settings { get; set; } = new();

    // The signed-in patron's own borrowing suspension, if any. Null = allowed.
    public DateTime? BannedUntil { get; set; }

    public List<LibraryPolicyTermDto> Terms { get; set; } = new();
}

public class LibraryPolicySettingsDto
{
    public int StudentBorrowLimit { get; set; }

    public int FacultyBorrowLimit { get; set; }

    public int DefaultLoanDays { get; set; }

    public int ReservationPickupDays { get; set; }

    // Semester dates used for the late-return borrowing suspension.
    public DateTime? FirstSemStart { get; set; }
    public DateTime? FirstSemEnd { get; set; }
    public DateTime? SecondSemStart { get; set; }
    public DateTime? SecondSemEnd { get; set; }

    public int[] LoanDayOptions { get; set; } = Array.Empty<int>();
}

public class LibraryPolicyTermDto
{
    public int Id { get; set; }

    // Optional. A term can be just a paragraph.
    public string? Title { get; set; }

    public string Content { get; set; } = string.Empty;

    // Used by the app to decide when the terms gate must show again after
    // the Library Head edits any section.
    public DateTime UpdatedAt { get; set; }
}