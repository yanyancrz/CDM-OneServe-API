namespace CDM_OneServe_API.Services.Admin.Library;

// Same names as before, so no other file changes. The values now come from the
// LibrarySettings row: loaded at startup and refreshed whenever the Head saves.
public static class LibraryDefaults
{
    private sealed record Snapshot(
        decimal Fine, int StudentLimit, int FacultyLimit, int LoanDays, int PickupDays);

    // Factory values, used only until the database row is loaded.
    private static volatile Snapshot _current = new(10m, 3, 5, 7, 3);

    public static decimal OverdueFinePerDay => _current.Fine;
    public static int StudentBorrowLimit => _current.StudentLimit;
    public static int FacultyBorrowLimit => _current.FacultyLimit;
    public static int DefaultLoanDays => _current.LoanDays;
    public static int ReservationPickupDays => _current.PickupDays;

    // The loan periods offered on the Borrow page.
    public static readonly int[] LoanDayOptions = { 3, 7, 14 };

    public static int BorrowLimitFor(string? role) =>
        string.Equals(role, "Faculty", StringComparison.OrdinalIgnoreCase)
            ? FacultyBorrowLimit
            : StudentBorrowLimit;

    public static void Apply(decimal fine, int studentLimit, int facultyLimit, int loanDays, int pickupDays) =>
        _current = new Snapshot(fine, studentLimit, facultyLimit, loanDays, pickupDays);
}