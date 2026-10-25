namespace CDM_OneServe_API.DTOs.Admin.Library.Reservations;

// Body for POST /api/admin/library/reservations/{id}/claim (convert to a loan).
public class ClaimReservationDto
{
    // 3, 7 or 14. Null = LibraryDefaults.DefaultLoanDays (the scanner sends none).
    public int? LoanDays { get; set; }

    public bool AcceptedTerms { get; set; }

    // True when the librarian chooses inside-library use. The server also
    // forces this automatically when no other copy is left on the shelf.
    public bool InsideLibraryOnly { get; set; }
}
