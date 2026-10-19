namespace CDM_OneServe_API.Services.Admin.Library;

// Same names as before, so no other file changes. The values now come from the
// LibrarySettings row: loaded at startup and refreshed whenever the Head saves.
public static class LibraryDefaults
{
    private sealed record Snapshot(
        int StudentLimit, int FacultyLimit, int LoanDays, int PickupDays,
        DateTime? FirstSemStart, DateTime? FirstSemEnd,
        DateTime? SecondSemStart, DateTime? SecondSemEnd);

    // Factory values, used only until the database row is loaded.
    private static volatile Snapshot _current = new(3, 5, 7, 3, null, null, null, null);

    public static int StudentBorrowLimit => _current.StudentLimit;
    public static int FacultyBorrowLimit => _current.FacultyLimit;
    public static int DefaultLoanDays => _current.LoanDays;
    public static int ReservationPickupDays => _current.PickupDays;

    // The semester dates the Library Head configures. A late return bans the
    // patron until the end of the semester in progress.
    public static DateTime? FirstSemStart => _current.FirstSemStart;
    public static DateTime? FirstSemEnd => _current.FirstSemEnd;
    public static DateTime? SecondSemStart => _current.SecondSemStart;
    public static DateTime? SecondSemEnd => _current.SecondSemEnd;

    // The loan periods offered on the Borrow page.
    public static readonly int[] LoanDayOptions = { 3, 7, 14 };

    public static int BorrowLimitFor(string? role) =>
        string.Equals(role, "Faculty", StringComparison.OrdinalIgnoreCase)
            ? FacultyBorrowLimit
            : StudentBorrowLimit;

    public static void Apply(
        int studentLimit, int facultyLimit, int loanDays, int pickupDays,
        DateTime? firstSemStart, DateTime? firstSemEnd,
        DateTime? secondSemStart, DateTime? secondSemEnd) =>
        _current = new Snapshot(studentLimit, facultyLimit, loanDays, pickupDays,
            firstSemStart, firstSemEnd, secondSemStart, secondSemEnd);

    /// <summary>
    /// The end of the semester in progress: the earliest configured semester end
    /// that is today or later. Falls back to 180 days from now when no semester
    /// dates are configured, so a late return always carries a ban.
    /// </summary>
    public static DateTime? ResolveSemesterEnd(DateTime now)
    {
        var candidates = new[] { _current.FirstSemEnd, _current.SecondSemEnd }
            .Where(d => d.HasValue && d.Value.Date >= now.Date)
            .Select(d => d!.Value.Date)
            .ToList();

        return candidates.Count > 0 ? candidates.Min() : now.Date.AddDays(180);
    }

    // A patron is suspended while today is on or before the ban date.
    public static bool IsBanned(DateTime? banUntil, DateTime now) =>
        banUntil.HasValue && now.Date <= banUntil.Value.Date;
}
