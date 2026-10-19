using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Services.Admin.Library;

public static class PatronRules
{
    // Returns null when the patron may borrow, otherwise the reason.
    public static string? CheckEligibility(User user, int activeLoans, int limit)
    {
        if (!user.IsVerified) return "Account is not verified.";
        if (!user.IsProfileComplete) return "Account profile is incomplete.";

        // The database uses "Approved"; the admin screens use "Active". Both are allowed.
        var status = user.AccountStatus?.Trim() ?? string.Empty;

        var allowed =
            status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
            return $"Account is not active (status: {(status.Length == 0 ? "unknown" : status)}).";

        if (LibraryDefaults.IsBanned(user.BorrowBanUntil, DateTime.Now))
            return $"Borrowing is suspended for this semester (until {user.BorrowBanUntil:MMM d, yyyy}).";

        if (activeLoans >= limit)
            return $"Borrowing limit reached ({activeLoans}/{limit} books).";

        return null;
    }
}